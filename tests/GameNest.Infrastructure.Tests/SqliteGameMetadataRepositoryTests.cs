using GameNest.Application;
using GameNest.Domain;
using GameNest.Infrastructure;
using GameNest.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameNest.Infrastructure.Tests;

public sealed class SqliteGameMetadataRepositoryTests
{
    [Fact]
    public async Task ApplyPersistsAttributionProtectsManualTitleAndUndoRestoresSnapshot()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = GameNestDataPaths.CreateForRoot(directory.Path);
        using var initializer = CreateInitializer(paths);
        var games = new SqliteGameLibraryRepository(paths, initializer);
        var original = CreateGame([GameEditableField.Title]);
        await games.AddAsync(original, TestContext.Current.CancellationToken);
        var service = new GameMetadataService(
            games,
            new SqliteGameMetadataRepository(paths, initializer, games),
            []);

        var applied = await service.ApplyAsync(
            original.Id,
            new MetadataCandidate(
                "fake-provider",
                "测试提供者",
                "source-77",
                "不应覆盖的标题",
                "补全后的简介"),
            TestContext.Current.CancellationToken);
        var persisted = await games.GetByIdAsync(original.Id, TestContext.Current.CancellationToken);
        var undone = await service.UndoLastAsync(original.Id, TestContext.Current.CancellationToken);

        Assert.Equal("本地标题", applied.Title);
        Assert.Equal("补全后的简介", persisted?.Description);
        Assert.Equal("fake-provider", persisted?.MetadataAttribution?.ProviderId);
        Assert.Null(undone?.Description);
        Assert.Null(undone?.MetadataAttribution);
        Assert.Null(await service.UndoLastAsync(original.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CoverAndContentHashPersistAndManualRemovalPreventsRediscovery()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = GameNestDataPaths.CreateForRoot(directory.Path);
        using var initializer = CreateInitializer(paths);
        var games = new SqliteGameLibraryRepository(paths, initializer);
        var original = CreateGame([]);
        var cover = new GameAsset(
            Guid.NewGuid(),
            original.Id,
            GameAssetType.Cover,
            Path.Combine(directory.Path, "cached-cover.jpg"),
            "UserImport:test.jpg",
            600,
            900,
            DateTimeOffset.UtcNow,
            "sha256-value");

        await games.AddAsync(original, TestContext.Current.CancellationToken);
        await games.SetCoverAsync(cover, isUserEdited: true, TestContext.Current.CancellationToken);
        var imported = await games.GetByIdAsync(original.Id, TestContext.Current.CancellationToken);
        await games.RemoveCoverAsync(original.Id, TestContext.Current.CancellationToken);
        var removed = await games.GetByIdAsync(original.Id, TestContext.Current.CancellationToken);

        var importedGame = Assert.IsType<Game>(imported);
        var removedGame = Assert.IsType<Game>(removed);
        Assert.Equal("sha256-value", importedGame.Cover?.ContentHash);
        Assert.Contains(GameEditableField.Cover, importedGame.UserEditedFields);
        Assert.Null(removedGame.Cover);
        Assert.Contains(GameEditableField.Cover, removedGame.UserEditedFields);
    }

    private static SqliteDatabaseInitializer CreateInitializer(GameNestDataPaths paths) =>
        new(paths, NullLogger<SqliteDatabaseInitializer>.Instance);

    private static Game CreateGame(IEnumerable<GameEditableField> editedFields)
    {
        var gameId = Guid.NewGuid();
        return new Game(
            gameId,
            "本地标题",
            null,
            @"D:\Games\Local",
            GameSourceType.ManualExecutable,
            false,
            GameAvailability.Available,
            DateTimeOffset.UtcNow,
            null,
            0,
            new LaunchProfile(
                Guid.NewGuid(),
                gameId,
                "默认",
                LaunchKind.Executable,
                @"D:\Games\Local\game.exe",
                null,
                @"D:\Games\Local",
                false,
                true),
            null,
            userEditedFields: editedFields);
    }
}
