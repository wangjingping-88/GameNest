using GameNest.Domain;
using GameNest.Infrastructure;
using GameNest.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameNest.Infrastructure.Tests;

public sealed class SqliteGameRuntimeRepositoryTests
{
    [Fact]
    public async Task CompletingSessionPersistsExitAndUpdatesGameTotalsExactlyOnce()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = GameNestDataPaths.CreateForRoot(directory.Path);
        using var initializer = new SqliteDatabaseInitializer(
            paths,
            NullLogger<SqliteDatabaseInitializer>.Instance);
        var games = new SqliteGameLibraryRepository(paths, initializer);
        var sessions = new SqliteGameRuntimeRepository(paths, initializer);
        var game = CreateGame(totalPlaySeconds: 10);
        var startedAt = new DateTimeOffset(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);
        var session = new PlaySession(
            Guid.NewGuid(),
            game.Id,
            startedAt,
            null,
            null,
            null,
            [100]);
        await games.AddAsync(game, TestContext.Current.CancellationToken);
        await sessions.StartSessionAsync(session, TestContext.Current.CancellationToken);
        await sessions.UpdateTrackedProcessIdsAsync(
            session.Id,
            [100, 101],
            TestContext.Current.CancellationToken);

        var completed = await sessions.CompleteSessionAsync(
            session.Id,
            startedAt.AddSeconds(95),
            GameExitKind.Forced,
            TestContext.Current.CancellationToken);
        var duplicateCompletion = await sessions.CompleteSessionAsync(
            session.Id,
            startedAt.AddSeconds(120),
            GameExitKind.Natural,
            TestContext.Current.CancellationToken);
        var storedGame = await games.GetByIdAsync(game.Id, TestContext.Current.CancellationToken);
        var storedSession = Assert.Single(
            await sessions.GetSessionsAsync(game.Id, TestContext.Current.CancellationToken));

        Assert.Equal(95, completed?.DurationSeconds);
        Assert.Equal(GameExitKind.Forced, duplicateCompletion?.ExitKind);
        Assert.Equal([100, 101], storedSession.TrackedProcessIds);
        Assert.Equal(105, storedGame?.TotalPlaySeconds);
        Assert.Equal(startedAt, storedGame?.LastPlayedUtc);
        Assert.Equal(
            ["Runtime", "RuntimeHelper"],
            storedGame?.LaunchProfile.ExpectedProcessNames,
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(12, storedGame?.LaunchProfile.GracefulStopTimeoutSeconds);
    }

    [Fact]
    public async Task OnlyOneActiveSessionPerGameIsAllowed()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = GameNestDataPaths.CreateForRoot(directory.Path);
        using var initializer = new SqliteDatabaseInitializer(
            paths,
            NullLogger<SqliteDatabaseInitializer>.Instance);
        var games = new SqliteGameLibraryRepository(paths, initializer);
        var sessions = new SqliteGameRuntimeRepository(paths, initializer);
        var game = CreateGame(0);
        await games.AddAsync(game, TestContext.Current.CancellationToken);
        var first = CreateActiveSession(game.Id, 10);
        var second = CreateActiveSession(game.Id, 20);

        await sessions.StartSessionAsync(first, TestContext.Current.CancellationToken);

        Assert.Equal(
            first.Id,
            Assert.Single(
                await sessions.GetActiveSessionsAsync(TestContext.Current.CancellationToken)).Id);

        await Assert.ThrowsAsync<SqliteException>(
            () => sessions.StartSessionAsync(second, TestContext.Current.CancellationToken));
    }

    private static PlaySession CreateActiveSession(Guid gameId, int processId) =>
        new(
            Guid.NewGuid(),
            gameId,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            [processId]);

    private static Game CreateGame(long totalPlaySeconds)
    {
        var gameId = Guid.NewGuid();
        var profile = new LaunchProfile(
            Guid.NewGuid(),
            gameId,
            "默认",
            LaunchKind.Executable,
            @"D:\Games\Runtime.exe",
            null,
            @"D:\Games",
            false,
            true,
            ["Runtime", "RuntimeHelper.exe"],
            12);
        return new Game(
            gameId,
            "会话测试",
            null,
            @"D:\Games",
            GameSourceType.ManualExecutable,
            false,
            GameAvailability.Available,
            DateTimeOffset.UtcNow,
            null,
            totalPlaySeconds,
            profile,
            null);
    }
}
