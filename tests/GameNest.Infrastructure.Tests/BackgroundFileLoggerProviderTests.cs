using GameNest.Infrastructure;
using GameNest.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace GameNest.Infrastructure.Tests;

public sealed class BackgroundFileLoggerProviderTests
{
    private static readonly Action<ILogger, int, Exception?> WriteTestEntry =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(9000, nameof(WriteTestEntry)),
            "日志测试 {Sequence}");

    [Fact]
    public async Task DisposeAsyncFlushesQueuedLogEntries()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = GameNestDataPaths.CreateForRoot(directory.Path);
        var provider = new BackgroundFileLoggerProvider(paths);
        var logger = provider.CreateLogger("GameNest.Tests");

        WriteTestEntry(logger, 42, null);
        await provider.DisposeAsync();

        var logFile = Assert.Single(Directory.GetFiles(paths.LogDirectory, "gamenest-*.log"));
        var content = await File.ReadAllTextAsync(logFile, TestContext.Current.CancellationToken);
        Assert.Contains("日志测试 42", content, StringComparison.Ordinal);
    }
}
