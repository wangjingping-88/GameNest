using System.Diagnostics;
using System.Text.RegularExpressions;
using GameNest.Application;
using GameNest.Domain;
using Microsoft.Extensions.Logging;

namespace GameNest.Infrastructure.Scanning;

public sealed partial class SteamGameSourceAdapter(
    ILogger<SteamGameSourceAdapter> logger) : IGameSourceAdapter
{
    private static readonly Action<ILogger, string, Exception?> SteamItemSkipped =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(3110, nameof(SteamItemSkipped)),
            "跳过无法读取的 Steam 扫描项。路径：{Path}");

    public string Id => "steam";

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
        var stopwatch = Stopwatch.StartNew();
        var steamAppsDirectories = FindSteamAppsDirectories(context.Roots);
        foreach (var file in FindLibraryFolderFiles(steamAppsDirectories))
        {
            await context.PauseToken.WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var steamApps = Path.GetDirectoryName(file)!;
                var text = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                foreach (Match match in SteamLibraryPathRegex().Matches(text))
                {
                    var libraryPath = match.Groups[1].Value.Replace("\\\\", "\\");
                    var discoveredSteamApps = Path.Combine(libraryPath, "steamapps");
                    if (FindRoot(context.Roots, discoveredSteamApps) is not null &&
                        Directory.Exists(discoveredSteamApps))
                    {
                        steamAppsDirectories.Add(Path.GetFullPath(discoveredSteamApps));
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                SteamItemSkipped(logger, file, exception);
            }
        }

        var candidates = new List<DiscoveredGame>();
        long checkedDirectories = 0;
        foreach (var steamApps in steamAppsDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] manifests;
            try
            {
                manifests = await Task.Run(
                    () => Directory.GetFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                SteamItemSkipped(logger, steamApps, exception);
                continue;
            }

            foreach (var manifest in manifests)
            {
                await context.PauseToken.WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var text = await File.ReadAllTextAsync(manifest, cancellationToken).ConfigureAwait(false);
                    var appId = ReadAcfValue(text, "appid");
                    var name = ReadAcfValue(text, "name");
                    var installDirectoryName = ReadAcfValue(text, "installdir");
                    if (string.IsNullOrWhiteSpace(installDirectoryName))
                    {
                        continue;
                    }

                    var installRoot = Path.GetFullPath(Path.Combine(steamApps, "common", installDirectoryName));
                    var root = FindRoot(context.Roots, installRoot);
                    if (root is null || !Directory.Exists(installRoot) || IsExcluded(installRoot, context.ExcludedDirectories))
                    {
                        continue;
                    }

                    var executablePath = await Task.Run(
                        () => SelectPrimaryExecutable(installRoot, name ?? installDirectoryName),
                        cancellationToken).ConfigureAwait(false);
                    if (executablePath is null)
                    {
                        continue;
                    }

                    var info = new FileInfo(executablePath);
                    var fingerprint = GameCandidateFingerprint.Create(
                        executablePath,
                        info.Length,
                        info.LastWriteTimeUtc);
                    context.PreviousCandidates.TryGetValue(executablePath, out var previous);
                    var isUnchanged = previous?.Fingerprint == fingerprint && previous.AdapterId == Id;
                    var directory = Path.GetDirectoryName(executablePath)!;
                    var siblingFiles = Directory.GetFiles(directory);
                    var childDirectories = Directory.GetDirectories(directory);
                    var evidence = isUnchanged
                        ? []
                        : ExecutableDiscoverySignals.Inspect(executablePath, siblingFiles, childDirectories);
                    candidates.Add(
                        new DiscoveredGame(
                            root.Id,
                            Id,
                            GameCandidateSource.Steam,
                            appId,
                            name ?? installDirectoryName,
                            executablePath,
                            null,
                            directory,
                            installRoot,
                            root.VolumeIdentity,
                            info.Length,
                            info.LastWriteTimeUtc,
                            evidence,
                            isUnchanged ? previous : null));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    SteamItemSkipped(logger, manifest, exception);
                }
                finally
                {
                    checkedDirectories++;
                    progress?.Report(
                        new GameScanProgress(
                            "Steam 清单",
                            manifest,
                            checkedDirectories,
                            candidates.Count,
                            stopwatch.Elapsed));
                }
            }
        }

        return candidates;
    }

    private static HashSet<string> FindSteamAppsDirectories(IReadOnlyList<ScanRoot> roots)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.CurrentPath));
            var rootName = Path.GetFileName(rootPath);
            var parentPath = Path.GetDirectoryName(rootPath);
            var parentName = parentPath is null ? null : Path.GetFileName(parentPath);
            if (rootName.Equals("common", StringComparison.OrdinalIgnoreCase) &&
                parentName?.Equals("steamapps", StringComparison.OrdinalIgnoreCase) == true)
            {
                AddIfDirectoryExists(candidates, parentPath!);
                continue;
            }

            if (rootName.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
            {
                AddIfDirectoryExists(candidates, rootPath);
                continue;
            }

            AddIfDirectoryExists(candidates, Path.Combine(rootPath, "steamapps"));
            AddIfDirectoryExists(candidates, Path.Combine(rootPath, "Steam", "steamapps"));
            AddIfDirectoryExists(candidates, Path.Combine(rootPath, "SteamLibrary", "steamapps"));
        }

        return candidates;
    }

    private static string[] FindLibraryFolderFiles(IEnumerable<string> steamAppsDirectories) =>
        steamAppsDirectories
            .Select(steamApps => Path.Combine(steamApps, "libraryfolders.vdf"))
            .Where(File.Exists)
            .ToArray();

    private static void AddIfDirectoryExists(HashSet<string> paths, string path)
    {
        if (Directory.Exists(path))
        {
            paths.Add(Path.GetFullPath(path));
        }
    }

    private static string? ReadAcfValue(string text, string key)
    {
        var match = Regex.Match(
            text,
            $"\"{Regex.Escape(key)}\"\\s+\"([^\"]*)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? SelectPrimaryExecutable(string installRoot, string gameName)
    {
        var executablePaths = EnumerateExecutablePaths(installRoot).ToArray();
        return executablePaths
            .Where(static path => !LooksLikeTool(path))
            .OrderByDescending(path => GetExecutableRank(path, installRoot, gameName))
            .FirstOrDefault();
    }

    private static IEnumerable<string> EnumerateExecutablePaths(string root)
    {
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if (!visited.Add(Path.GetFullPath(directory)))
            {
                continue;
            }

            string[] files;
            string[] children;
            try
            {
                files = Directory.GetFiles(directory, "*.exe", SearchOption.TopDirectoryOnly);
                children = Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var child in children)
            {
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(child);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static int GetExecutableRank(string path, string installRoot, string gameName)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var normalizedGame = new string(gameName.Where(char.IsLetterOrDigit).ToArray());
        var normalizedFile = new string(fileName.Where(char.IsLetterOrDigit).ToArray());
        var score = string.Equals(normalizedGame, normalizedFile, StringComparison.OrdinalIgnoreCase) ? 100 : 0;
        score += Path.GetDirectoryName(path)!.Equals(installRoot, StringComparison.OrdinalIgnoreCase) ? 30 : 0;
        try
        {
            score += (int)Math.Min(new FileInfo(path).Length / (10 * 1024 * 1024), 30);
        }
        catch (IOException)
        {
        }

        return score;
    }

    private static bool LooksLikeTool(string path)
    {
        var value = path.Replace('/', '\\');
        var fileName = Path.GetFileNameWithoutExtension(value);
        return fileName.Contains("unins", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("crash", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("report", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("setup", StringComparison.OrdinalIgnoreCase)
            || value.Contains("\\redist", StringComparison.OrdinalIgnoreCase)
            || value.Contains("\\prereq", StringComparison.OrdinalIgnoreCase)
            || value.Contains("\\tools\\", StringComparison.OrdinalIgnoreCase);
    }

    private static ScanRoot? FindRoot(IEnumerable<ScanRoot> roots, string path)
    {
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return roots
            .Where(root =>
            {
                var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.CurrentPath));
                return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                       normalizedPath.StartsWith(
                           normalizedRoot + Path.DirectorySeparatorChar,
                           StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(static root => root.CurrentPath.Length)
            .FirstOrDefault();
    }

    private static bool IsExcluded(string path, IEnumerable<string> exclusions) =>
        exclusions.Any(excluded => path.StartsWith(
            Path.TrimEndingDirectorySeparator(excluded) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SteamLibraryPathRegex();
}
