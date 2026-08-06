using GameNest.Domain;

namespace GameNest.Application;

public sealed class GameCandidateScorer : IGameCandidateScorer
{
    private static readonly string[] StrongNegativeFileNames =
    [
        "uninstall", "unins", "setup", "crash", "report", "updater", "update",
    ];

    private static readonly string[] HelperPathSegments =
    [
        "\\redist\\", "\\redistributable", "\\prereq", "\\runtime", "\\tools\\",
        "\\helper", "\\config", "\\support\\",
    ];

    public GameCandidate Score(DiscoveredGame discovery, DateTimeOffset discoveredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        if (discovery.PreviousCandidate is { } previous)
        {
            return previous;
        }

        var evidence = discovery.Evidence.ToList();
        AddSourceEvidence(discovery.Source, evidence);

        var normalizedPath = discovery.ExecutablePath.Replace('/', '\\');
        var fileName = Path.GetFileNameWithoutExtension(normalizedPath);
        if (StrongNegativeFileNames.Any(name => fileName.Contains(name, StringComparison.OrdinalIgnoreCase)))
        {
            evidence.Add(new("blocked-file-name", "文件名像卸载器、安装器、更新器或崩溃上报器", -80));
        }

        if (IsSystemPath(normalizedPath))
        {
            evidence.Add(new("system-path", "位于 Windows、驱动或系统恢复目录", -100));
        }

        if (HelperPathSegments.Any(segment => normalizedPath.Contains(segment, StringComparison.OrdinalIgnoreCase)))
        {
            evidence.Add(new("helper-path", "位于运行库、配置或辅助工具目录", -40));
        }

        if (normalizedPath.Contains("\\Games\\", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("\\Game\\", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("\\游戏\\", StringComparison.OrdinalIgnoreCase))
        {
            evidence.Add(new("game-path", "路径包含常见游戏目录名称", 10));
        }

        if (discovery.FileSize < 256 * 1024
            && evidence.Where(static item => item.Score > 0).Sum(static item => item.Score) < 25)
        {
            evidence.Add(new("small-file", "文件小于 256 KB 且缺少其他游戏特征", -20));
        }

        var score = Math.Clamp(evidence.Sum(static item => item.Score), -100, 100);
        var fingerprint = GameCandidateFingerprint.Create(
            discovery.ExecutablePath,
            Math.Max(0, discovery.FileSize),
            discovery.LastWriteUtc);
        var groupKey = NormalizeDirectory(discovery.InstallRoot);

        return new GameCandidate(
            Guid.NewGuid(),
            discovery.ScanRootId,
            discovery.AdapterId,
            discovery.Source,
            discovery.SourceGameId,
            discovery.Title,
            discovery.ExecutablePath,
            discovery.Arguments,
            discovery.WorkingDirectory,
            discovery.InstallRoot,
            discovery.VolumeIdentity,
            fingerprint,
            score,
            evidence,
            groupKey,
            isPrimary: true,
            GameCandidateDecision.Pending,
            discoveredAtUtc);
    }

    private static void AddSourceEvidence(
        GameCandidateSource source,
        List<GameCandidateEvidence> evidence)
    {
        switch (source)
        {
            case GameCandidateSource.Steam:
                evidence.Add(new("platform-manifest", "来自 Steam 平台清单", 100));
                break;
            case GameCandidateSource.Shortcut:
                evidence.Add(new("user-shortcut", "来自桌面或开始菜单快捷方式", 45));
                break;
            case GameCandidateSource.GenericExecutable:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(source));
        }
    }

    private static bool IsSystemPath(string path) =>
        path.Contains("\\Windows\\", StringComparison.OrdinalIgnoreCase)
        || path.Contains("\\$Recycle.Bin\\", StringComparison.OrdinalIgnoreCase)
        || path.Contains("\\System Volume Information\\", StringComparison.OrdinalIgnoreCase)
        || path.Contains("\\DriverStore\\", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToUpperInvariant();
}
