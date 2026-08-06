using GameNest.Application;
using GameNest.Domain;

namespace GameNest.Application.Tests;

public sealed class GameCandidateScorerTests
{
    private readonly GameCandidateScorer _scorer = new();

    [Fact]
    public void SteamUninstallerIsNotHighConfidence()
    {
        var discovery = CreateDiscovery(
            GameCandidateSource.Steam,
            @"D:\Games\Example\uninstall.exe",
            []);

        var candidate = _scorer.Score(discovery, DateTimeOffset.UtcNow);

        Assert.Equal(GameCandidateConfidence.Ignored, candidate.Confidence);
        Assert.Contains(candidate.Evidence, static evidence => evidence.Code == "blocked-file-name");
    }

    [Fact]
    public void GenericExecutableWithMultipleGameSignalsIsHighConfidence()
    {
        var discovery = CreateDiscovery(
            GameCandidateSource.GenericExecutable,
            @"D:\Games\Example\Example.exe",
            [
                new GameCandidateEvidence("steam-api", "同目录包含 steam_api.dll", 35),
                new GameCandidateEvidence("engine-layout", "检测到游戏引擎目录", 25),
                new GameCandidateEvidence("version-metadata", "包含产品元数据", 15),
            ]);

        var candidate = _scorer.Score(discovery, DateTimeOffset.UtcNow);

        Assert.Equal(GameCandidateConfidence.High, candidate.Confidence);
        Assert.Equal(85, candidate.Score);
    }

    [Fact]
    public void FingerprintChangesWhenFileMetadataChanges()
    {
        var timestamp = new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero);
        var first = GameCandidateFingerprint.Create(@"D:\Games\Example.exe", 1024, timestamp);
        var same = GameCandidateFingerprint.Create(@"d:\games\example.exe", 1024, timestamp);
        var changed = GameCandidateFingerprint.Create(@"D:\Games\Example.exe", 2048, timestamp);

        Assert.Equal(first, same);
        Assert.NotEqual(first, changed);
    }

    private static DiscoveredGame CreateDiscovery(
        GameCandidateSource source,
        string executablePath,
        IReadOnlyList<GameCandidateEvidence> evidence) =>
        new(
            Guid.NewGuid(),
            "test-adapter",
            source,
            source == GameCandidateSource.Steam ? "100" : null,
            "Example",
            executablePath,
            null,
            Path.GetDirectoryName(executablePath)!,
            Path.GetDirectoryName(executablePath)!,
            "test-volume",
            10 * 1024 * 1024,
            DateTimeOffset.UtcNow,
            evidence);
}
