using GameNest.Domain;

namespace GameNest.Domain.Tests;

public sealed class PlaySessionTests
{
    [Fact]
    public void CompleteCalculatesDurationAndPreservesDistinctProcessIds()
    {
        var startedAt = new DateTimeOffset(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);
        var session = new PlaySession(
            Guid.NewGuid(),
            Guid.NewGuid(),
            startedAt,
            null,
            null,
            null,
            [42, 42, 84]);

        var completed = session.Complete(startedAt.AddSeconds(95.9), GameExitKind.Natural);

        Assert.Equal(95, completed.DurationSeconds);
        Assert.Equal(GameExitKind.Natural, completed.ExitKind);
        Assert.Equal([42, 84], completed.TrackedProcessIds);
    }

    [Fact]
    public void LaunchProfileNormalizesExpectedProcessNamesAndValidatesStopTimeout()
    {
        var gameId = Guid.NewGuid();
        var profile = new LaunchProfile(
            Guid.NewGuid(),
            gameId,
            "默认",
            LaunchKind.Executable,
            @"D:\Games\Example.exe",
            null,
            @"D:\Games",
            false,
            true,
            ["Example.exe", "example", " helper.exe "],
            15);

        Assert.Equal(["example", "helper"], profile.ExpectedProcessNames, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(15, profile.GracefulStopTimeoutSeconds);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LaunchProfile(
                Guid.NewGuid(),
                gameId,
                "默认",
                LaunchKind.Executable,
                @"D:\Games\Example.exe",
                null,
                @"D:\Games",
                false,
                true,
                gracefulStopTimeoutSeconds: 0));
    }
}
