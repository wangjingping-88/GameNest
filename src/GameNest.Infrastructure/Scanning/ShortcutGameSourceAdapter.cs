using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using GameNest.Application;
using GameNest.Domain;
using Microsoft.Extensions.Logging;

namespace GameNest.Infrastructure.Scanning;

public sealed class ShortcutGameSourceAdapter(
    ILocalGameFileInspector fileInspector,
    IShortcutSourceLocator sourceLocator,
    ILogger<ShortcutGameSourceAdapter> logger) : IGameSourceAdapter
{
    private static readonly Action<ILogger, string, Exception?> ShortcutSkipped =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(3120, nameof(ShortcutSkipped)),
            "跳过无法解析的快捷方式。路径：{ShortcutPath}");

    public string Id => "windows-shortcut";

    public async Task<IReadOnlyList<DiscoveredGame>> ScanAsync(
        GameScanContext context,
        IProgress<GameScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var stopwatch = Stopwatch.StartNew();
        var shortcutPaths = await sourceLocator.FindAsync(cancellationToken).ConfigureAwait(false);
        var candidates = new List<DiscoveredGame>();
        long checkedDirectories = 0;
        foreach (var shortcutPath in shortcutPaths)
        {
            await context.PauseToken.WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var inspection = await fileInspector
                    .InspectAsync(shortcutPath, cancellationToken)
                    .ConfigureAwait(false);
                if (IsExcluded(inspection.WorkingDirectory, context.ExcludedDirectories))
                {
                    continue;
                }

                var info = new FileInfo(inspection.ExecutablePath);
                var fingerprint = GameCandidateFingerprint.Create(
                    inspection.ExecutablePath,
                    info.Length,
                    info.LastWriteTimeUtc);
                context.PreviousCandidates.TryGetValue(inspection.ExecutablePath, out var previous);
                var isUnchanged = previous?.Fingerprint == fingerprint && previous.AdapterId == Id;
                var root = FindRoot(context.Roots, inspection.ExecutablePath);
                candidates.Add(
                    new DiscoveredGame(
                        root?.Id,
                        Id,
                        GameCandidateSource.Shortcut,
                        null,
                        inspection.SuggestedTitle,
                        inspection.ExecutablePath,
                        inspection.Arguments,
                        inspection.WorkingDirectory,
                        inspection.WorkingDirectory,
                        root?.VolumeIdentity,
                        info.Length,
                        info.LastWriteTimeUtc,
                        isUnchanged
                            ? []
                            : [new GameCandidateEvidence("visible-shortcut", "来自用户可见的桌面或开始菜单入口", 15)],
                        isUnchanged ? previous : null));
            }
            catch (Exception exception) when (exception is IOException
                                                   or UnauthorizedAccessException
                                                   or NotSupportedException
                                                   or InvalidOperationException
                                                   or Win32Exception
                                                   or COMException)
            {
                ShortcutSkipped(logger, shortcutPath, exception);
            }
            finally
            {
                checkedDirectories++;
                progress?.Report(
                    new GameScanProgress(
                        "快捷方式",
                        shortcutPath,
                        checkedDirectories,
                        candidates.Count,
                        stopwatch.Elapsed));
            }
        }

        return candidates;
    }

    private static ScanRoot? FindRoot(IEnumerable<ScanRoot> roots, string path) =>
        roots
            .Where(root => path.StartsWith(
                Path.TrimEndingDirectorySeparator(root.CurrentPath) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static root => root.CurrentPath.Length)
            .FirstOrDefault();

    private static bool IsExcluded(string path, IEnumerable<string> exclusions) =>
        exclusions.Any(excluded => path.StartsWith(
            Path.TrimEndingDirectorySeparator(excluded) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase));
}
