using GameNest.Application;
using GameNest.Domain;

namespace GameNest.Application.Tests;

public sealed class GameCandidateGrouperTests
{
    [Fact]
    public void SteamManifestWinsPrimarySelectionAndAlternativesRemain()
    {
        var installRoot = @"D:\Games\Example";
        var generic = CreateCandidate(GameCandidateSource.GenericExecutable, 95, "Example-Win64.exe", installRoot);
        var steam = CreateCandidate(GameCandidateSource.Steam, 80, "ExampleLauncher.exe", installRoot);

        var grouped = new GameCandidateGrouper().Group([generic, steam]);

        Assert.Equal(2, grouped.Count);
        Assert.Equal(
            GameCandidateSource.Steam,
            Assert.Single(grouped, static candidate => candidate.IsPrimary).Source);
        Assert.Single(grouped, static candidate => !candidate.IsPrimary);
    }

    private static GameCandidate CreateCandidate(
        GameCandidateSource source,
        int score,
        string fileName,
        string installRoot) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "test",
            source,
            null,
            Path.GetFileNameWithoutExtension(fileName),
            Path.Combine(installRoot, fileName),
            null,
            installRoot,
            installRoot,
            "volume",
            Guid.NewGuid().ToString("N"),
            score,
            [],
            installRoot.ToUpperInvariant(),
            true,
            GameCandidateDecision.Pending,
            DateTimeOffset.UtcNow);
}
