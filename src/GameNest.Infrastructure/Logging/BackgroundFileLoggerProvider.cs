using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace GameNest.Infrastructure.Logging;

public sealed class BackgroundFileLoggerProvider : ILoggerProvider, IAsyncDisposable
{
    private const long MaximumLogFileBytes = 5 * 1024 * 1024;
    private readonly GameNestDataPaths _paths;
    private readonly Channel<LogEntry> _entries;
    private readonly Task _writerTask;
    private int _disposed;

    public BackgroundFileLoggerProvider(GameNestDataPaths paths)
    {
        _paths = paths;
        _entries = Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(1024)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
        _writerTask = Task.Run(WriteLoopAsync);
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return new BackgroundFileLogger(categoryName, _entries.Writer);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _entries.Writer.TryComplete();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await _writerTask.ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            Directory.CreateDirectory(_paths.LogDirectory);

            await foreach (var entry in _entries.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                var logPath = ResolveLogPath(entry.Timestamp);
                var line = FormatEntry(entry);
                await File.AppendAllTextAsync(
                        logPath,
                        line + Environment.NewLine,
                        Encoding.UTF8,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // 日志系统不能因为自身 I/O 失败而终止主应用。
        }
        catch (UnauthorizedAccessException)
        {
            // 无写入权限时降级为无文件日志，避免递归记录失败。
        }
    }

    private string ResolveLogPath(DateTimeOffset timestamp)
    {
        var datePart = timestamp.ToLocalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var basePath = Path.Combine(_paths.LogDirectory, $"gamenest-{datePart}.log");

        if (!File.Exists(basePath) || new FileInfo(basePath).Length < MaximumLogFileBytes)
        {
            return basePath;
        }

        for (var sequence = 1; ; sequence++)
        {
            var candidate = Path.Combine(_paths.LogDirectory, $"gamenest-{datePart}-{sequence}.log");
            if (!File.Exists(candidate) || new FileInfo(candidate).Length < MaximumLogFileBytes)
            {
                return candidate;
            }
        }
    }

    private static string FormatEntry(LogEntry entry)
    {
        var message = Sanitize(entry.Message);
        var exception = entry.Exception is null ? string.Empty : $" | {Sanitize(entry.Exception)}";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{entry.Timestamp:O} [{entry.Level}] {entry.Category} ({entry.EventId.Id}): {message}{exception}");
    }

    private static string Sanitize(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ');

    private sealed record LogEntry(
        DateTimeOffset Timestamp,
        LogLevel Level,
        string Category,
        EventId EventId,
        string Message,
        string? Exception);

    private sealed class BackgroundFileLogger(
        string category,
        ChannelWriter<LogEntry> writer) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel))
            {
                return;
            }

            writer.TryWrite(
                new LogEntry(
                    DateTimeOffset.UtcNow,
                    logLevel,
                    category,
                    eventId,
                    formatter(state, exception),
                    exception?.ToString()));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
