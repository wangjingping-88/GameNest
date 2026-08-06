using GameNest.Domain;
using GameNest.Infrastructure;
using GameNest.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameNest.Infrastructure.Tests;

public sealed class SqliteOverlayProfileRepositoryTests
{
    [Fact]
    public async Task GlobalAndPerGameProfilesPersistWithoutRebuildingInitialSchema()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = GameNestDataPaths.CreateForRoot(directory.Path);
        using var initializer = new SqliteDatabaseInitializer(
            paths,
            NullLogger<SqliteDatabaseInitializer>.Instance);
        var repository = new SqliteOverlayProfileRepository(paths, initializer);
        var games = new SqliteGameLibraryRepository(paths, initializer);
        var game = CreateGame();
        await games.AddAsync(game, TestContext.Current.CancellationToken);

        var initial = await repository.GetGlobalAsync(TestContext.Current.CancellationToken);
        var updated = new OverlayProfile(
            initial.Id,
            null,
            true,
            OverlayPosition.BottomRight,
            125,
            72,
            true,
            false,
            true,
            true,
            "Alt+F11",
            false,
            DateTimeOffset.UtcNow);
        await repository.SaveAsync(updated, TestContext.Current.CancellationToken);
        var gameProfile = new OverlayProfile(
            Guid.NewGuid(),
            game.Id,
            false,
            OverlayPosition.TopLeft,
            75,
            50,
            false,
            true,
            false,
            true,
            "Ctrl+F10",
            true,
            DateTimeOffset.UtcNow);
        await repository.SaveAsync(gameProfile, TestContext.Current.CancellationToken);

        var storedGlobal = await repository.GetGlobalAsync(TestContext.Current.CancellationToken);
        var storedGame = await repository.GetForGameAsync(game.Id, TestContext.Current.CancellationToken);

        Assert.Equal(OverlayPosition.BottomRight, storedGlobal.Position);
        Assert.Equal(125, storedGlobal.ScalePercent);
        Assert.Equal("Alt+F11", storedGlobal.ToggleHotkey);
        Assert.Equal(gameProfile, storedGame);

        await repository.RemoveForGameAsync(game.Id, TestContext.Current.CancellationToken);
        Assert.Null(await repository.GetForGameAsync(game.Id, TestContext.Current.CancellationToken));
        Assert.NotNull(await repository.GetGlobalAsync(TestContext.Current.CancellationToken));
    }

    private static Game CreateGame()
    {
        var id = Guid.NewGuid();
        return new Game(
            id,
            "覆盖层配置测试",
            null,
            @"D:\Games",
            GameSourceType.ManualExecutable,
            false,
            GameAvailability.Available,
            DateTimeOffset.UtcNow,
            null,
            0,
            new LaunchProfile(
                Guid.NewGuid(),
                id,
                "默认",
                LaunchKind.Executable,
                @"D:\Games\OverlayTest.exe",
                null,
                @"D:\Games",
                false,
                true),
            null);
    }
}
