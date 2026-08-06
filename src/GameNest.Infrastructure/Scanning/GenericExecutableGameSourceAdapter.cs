using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using GameNest.Application;
using GameNest.Domain;
using Microsoft.Extensions.Logging;

namespace GameNest.Infrastructure.Scanning;

public sealed class GenericExecutableGameSourceAdapter(
    ILogger<GenericExecutableGameSourceAdapter> logger) : IGameSourceAdapter
{
    private static readonly Action<ILogger, string, Exception?> ExecutableSkipped =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(3100, nameof(ExecutableSkipped)),
            "跳过无法读取的 EXE 候选。路径：{ExecutablePath}");

    private static readonly Action<ILogger, string, Exception?> DirectorySkipped =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(3101, nameof(DirectorySkipped)),
            "跳过无法访问的扫描目录。路径：{DirectoryPath}");

    private const int WorkerCount = 3;
    private static readonly string[] DefaultExcludedDirectoryNames =
    [
        "Windows", "$Recycle.Bin", "System Volume Information", "node_modules",
        ".git", ".vs", "packages", "Cache", "Caches", "Temp",
    ];

    public string Id => "generic-executable";

    public Task<IReadOnlyList<DiscoveredGame>> ScanAsync(
        GameScanContext context,
        IProgress<GameScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.Run(() => ScanCoreAsync(context, progress, cancellationToken), cancellationToken);
    }

    private async Task<IReadOnlyList<DiscoveredGame>> ScanCoreAsync(
        GameScanContext context,
        IProgress<GameScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.StartNew();
        var results = new ConcurrentBag<DiscoveredGame>();
        var visited = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var channel = Channel.CreateUnbounded<ScanDirectory>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
        var state = new ScanState();

        foreach (var root in context.Roots)
        {
            if (TryQueueDirectory(channel.Writer, visited, root, root.CurrentPath, state))
            {
                continue;
            }
        }

        if (state.PendingDirectoryCount == 0)
        {
            channel.Writer.TryComplete();
        }

        var workers = Enumerable.Range(0, WorkerCount)
            .Select(_ => Task.Run(
                async () =>
                {
                    await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                    {
                        try
                        {
                            await context.PauseToken.WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
                            await ProcessDirectoryAsync(
                                item,
                                context,
                                channel.Writer,
                                visited,
                                results,
                                startedAt,
                                progress,
                                state,
                                cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            if (Interlocked.Decrement(ref state.PendingDirectoryCount) == 0)
                            {
                                channel.Writer.TryComplete();
                            }
                        }
                    }
                },
                cancellationToken))
            .ToArray();
        await Task.WhenAll(workers).ConfigureAwait(false);
        return results.ToArray();
    }

    private async Task ProcessDirectoryAsync(
        ScanDirectory item,
        GameScanContext context,
        ChannelWriter<ScanDirectory> writer,
        ConcurrentDictionary<string, byte> visited,
        ConcurrentBag<DiscoveredGame> results,
        Stopwatch elapsed,
        IProgress<GameScanProgress>? progress,
        ScanState state,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ShouldSkipDirectory(item.Path, context.ExcludedDirectories))
            {
                return;
            }

            var entries = Directory.GetFileSystemEntries(item.Path);
            var files = entries.Where(File.Exists).ToArray();
            var directories = entries.Where(Directory.Exists).ToArray();

            foreach (var executablePath in files.Where(static file =>
                         Path.GetExtension(file).Equals(".exe", StringComparison.OrdinalIgnoreCase)))
            {
                await context.PauseToken.WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(executablePath);
                    var fingerprint = GameCandidateFingerprint.Create(
                        executablePath,
                        info.Length,
                        info.LastWriteTimeUtc);
                    context.PreviousCandidates.TryGetValue(executablePath, out var previous);
                    var isUnchanged = previous?.Fingerprint == fingerprint && previous.AdapterId == Id;
                    var evidence = isUnchanged
                        ? []
                        : ExecutableDiscoverySignals.Inspect(executablePath, files, directories);
                    results.Add(
                        new DiscoveredGame(
                            item.Root.Id,
                            Id,
                            GameCandidateSource.GenericExecutable,
                            null,
                            isUnchanged
                                ? previous!.Title
                                : ExecutableDiscoverySignals.GetTitle(executablePath),
                            executablePath,
                            null,
                            item.Path,
                            item.Path,
                            item.Root.VolumeIdentity,
                            info.Length,
                            info.LastWriteTimeUtc,
                            evidence,
                            isUnchanged ? previous : null));
                }
                catch (Exception exception) when (IsRecoverableFileError(exception))
                {
                    ExecutableSkipped(logger, executablePath, exception);
                }
            }

            foreach (var directory in directories)
            {
                if (ShouldSkipDirectory(directory, context.ExcludedDirectories))
                {
                    continue;
                }

                TryQueueDirectory(writer, visited, item.Root, directory, state);
            }
        }
        catch (Exception exception) when (IsRecoverableDirectoryError(exception))
        {
            DirectorySkipped(logger, item.Path, exception);
        }
        finally
        {
            var checkedCount = Interlocked.Increment(ref state.CheckedDirectoryCount);
            progress?.Report(
                new GameScanProgress(
                    "通用 EXE 扫描",
                    item.Path,
                    checkedCount,
                    results.Count,
                    elapsed.Elapsed));
        }
    }

    private static bool TryQueueDirectory(
        ChannelWriter<ScanDirectory> writer,
        ConcurrentDictionary<string, byte> visited,
        ScanRoot root,
        string directory,
        ScanState state)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (!visited.TryAdd(fullPath, 0))
        {
            return false;
        }

        try
        {
            if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }
        }
        catch (Exception exception) when (IsRecoverableDirectoryError(exception))
        {
            return false;
        }

        Interlocked.Increment(ref state.PendingDirectoryCount);
        if (!writer.TryWrite(new ScanDirectory(root, fullPath)))
        {
            Interlocked.Decrement(ref state.PendingDirectoryCount);
            return false;
        }

        return true;
    }

    private static bool ShouldSkipDirectory(string directory, IReadOnlyList<string> exclusions)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        if (DefaultExcludedDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return exclusions.Any(excluded =>
            normalized.Equals(excluded, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(excluded)) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRecoverableDirectoryError(Exception exception) =>
        exception is UnauthorizedAccessException or IOException;

    private static bool IsRecoverableFileError(Exception exception) =>
        exception is UnauthorizedAccessException or IOException;

    private sealed record ScanDirectory(ScanRoot Root, string Path);

    private sealed class ScanState
    {
        public long PendingDirectoryCount;

        public long CheckedDirectoryCount;
    }
}
