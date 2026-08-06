using GameNest.Application;
using GameNest.Domain;
using GameNest.Infrastructure;
using GameNest.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameNest.Infrastructure.Tests;

public sealed class SqliteGameLibraryRepositoryTests
{
    [Fact]
    public async Task TwentyGamesAndUserEditsPersistAcrossRepositoryInstances()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = GameNestDataPaths.CreateForRoot(directory.Path);

        using (var initializer = CreateInitializer(paths))
        {
            var repository = new SqliteGameLibraryRepository(paths, initializer);
            for (var index = 0; index < 20; index++)
            {
                await repository.AddAsync(
                    CreateGame(index),
                    TestContext.Current.CancellationToken);
            }

            var first = (await repository.GetAllAsync(TestContext.Current.CancellationToken))[0];
            await repository.UpdateAsync(
                first.WithUserEdits(
                        "用户编辑名称",
                        "用户编辑简介",
                        "--测试 参数",
                        first.LaunchProfile.WorkingDirectory)
                    .WithFavorite(true),
                TestContext.Current.CancellationToken);
        }

        using (var restartedInitializer = CreateInitializer(paths))
        {
            var restartedRepository = new SqliteGameLibraryRepository(paths, restartedInitializer);
            var games = await restartedRepository.GetAllAsync(TestContext.Current.CancellationToken);
            var edited = Assert.Single(games, static game => game.Title == "用户编辑名称");

            Assert.Equal(20, games.Count);
            Assert.True(edited.IsFavorite);
            Assert.Equal("用户编辑简介", edited.Description);
            Assert.Equal("--测试 参数", edited.LaunchProfile.Arguments);
            Assert.Contains("游戏 库", edited.LaunchProfile.ExecutablePath, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RemoveAsyncCascadesProfileAndAsset()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = GameNestDataPaths.CreateForRoot(directory.Path);
        using var initializer = CreateInitializer(paths);
        var repository = new SqliteGameLibraryRepository(paths, initializer);
        var game = CreateGame(1, includeIcon: true);

        await repository.AddAsync(game, TestContext.Current.CancellationToken);
        Assert.True(await repository.RemoveAsync(game.Id, TestContext.Current.CancellationToken));

        await using var connection = SqliteDatabaseInitializerTests.CreateConnection(paths);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        foreach (var table in new[] { "Games", "LaunchProfiles", "GameAssets" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task DiscoveryMetadataAndOfflineStatusPersist()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = GameNestDataPaths.CreateForRoot(directory.Path);
        using var initializer = CreateInitializer(paths);
        var repository = new SqliteGameLibraryRepository(paths, initializer);
        var original = CreateGame(7);
        var discovered = new Game(
            original.Id,
            original.Title,
            original.Description,
            original.InstallRoot,
            GameSourceType.Steam,
            original.IsFavorite,
            original.Availability,
            original.DateAddedUtc,
            original.LastPlayedUtc,
            original.TotalPlaySeconds,
            original.LaunchProfile,
            original.Icon,
            new GameDiscoveryMetadata("12345", "volume-1", 92));

        await repository.AddAsync(discovered, TestContext.Current.CancellationToken);
        await repository.SetAvailabilityByVolumeAsync(
            "volume-1",
            GameAvailability.VolumeOffline,
            TestContext.Current.CancellationToken);
        var stored = Assert.Single(await repository.GetAllAsync(TestContext.Current.CancellationToken));

        Assert.Equal("12345", stored.DiscoveryMetadata?.SourceGameId);
        Assert.Equal(92, stored.DiscoveryMetadata?.DetectionConfidence);
        Assert.Equal(GameAvailability.VolumeOffline, stored.Availability);

        await repository.RebindVolumeAsync(
            "volume-1",
            @"D:\",
            @"E:\",
            TestContext.Current.CancellationToken);
        var rebound = Assert.Single(await repository.GetAllAsync(TestContext.Current.CancellationToken));

        Assert.StartsWith(@"E:\", rebound.InstallRoot, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(@"E:\", rebound.LaunchProfile.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(@"E:\", rebound.LaunchProfile.WorkingDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static SqliteDatabaseInitializer CreateInitializer(GameNestDataPaths paths) =>
        new(paths, NullLogger<SqliteDatabaseInitializer>.Instance);

    private static Game CreateGame(int index, bool includeIcon = false)
    {
        var gameId = Guid.NewGuid();
        var executablePath = $@"D:\游戏 库\第 {index:00} 款 [测试]\Game #{index:00}.exe";
        var installRoot = Path.GetDirectoryName(executablePath)!;
        var profile = new LaunchProfile(
            Guid.NewGuid(),
            gameId,
            "默认",
            LaunchKind.Executable,
            executablePath,
            null,
            installRoot,
            false,
            true);
        var icon = includeIcon
            ? new GameAsset(
                Guid.NewGuid(),
                gameId,
                GameAssetType.Icon,
                Path.Combine(installRoot, "icon.bmp"),
                executablePath,
                64,
                64,
                DateTimeOffset.UtcNow)
            : null;

        return new Game(
            gameId,
            $"测试游戏 {index:00}",
            null,
            installRoot,
            GameSourceType.ManualExecutable,
            false,
            GameAvailability.Available,
            DateTimeOffset.UtcNow.AddMinutes(index),
            null,
            0,
            profile,
            icon);
    }
}
