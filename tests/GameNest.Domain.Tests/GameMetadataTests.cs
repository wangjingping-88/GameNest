using GameNest.Domain;

namespace GameNest.Domain.Tests;

public sealed class GameMetadataTests
{
    [Fact]
    public void OnlineMetadataNeverOverwritesManualFields()
    {
        var game = CreateGame([GameEditableField.Title, GameEditableField.Description]);
        var attribution = new GameMetadataAttribution(
            "fake",
            "source-1",
            "测试提供者",
            DateTimeOffset.UtcNow);

        var updated = game.WithMetadata("在线标题", "在线简介", attribution);

        Assert.Equal("本地标题", updated.Title);
        Assert.Equal("本地简介", updated.Description);
        Assert.Same(attribution, updated.MetadataAttribution);
    }

    [Fact]
    public void MetadataCanFillFieldsThatWereNotEditedManually()
    {
        var game = CreateGame([]);
        var attribution = new GameMetadataAttribution(
            "fake",
            "source-2",
            "测试提供者",
            DateTimeOffset.UtcNow);

        var updated = game.WithMetadata("补全标题", "补全简介", attribution);

        Assert.Equal("补全标题", updated.Title);
        Assert.Equal("补全简介", updated.Description);
    }

    [Fact]
    public void ManualCoverIsMarkedAndCanBeRemovedWithoutLosingProtection()
    {
        var game = CreateGame([]);
        var cover = new GameAsset(
            Guid.NewGuid(),
            game.Id,
            GameAssetType.Cover,
            @"D:\cache\cover.jpg",
            "UserImport",
            600,
            900,
            DateTimeOffset.UtcNow,
            "abc");

        var imported = game.WithCover(cover, isUserEdited: true);
        var removed = imported.WithCover(null, isUserEdited: true);

        Assert.Same(cover, imported.Cover);
        Assert.Null(removed.Cover);
        Assert.Contains(GameEditableField.Cover, removed.UserEditedFields);
    }

    private static Game CreateGame(IEnumerable<GameEditableField> editedFields)
    {
        var gameId = Guid.NewGuid();
        return new Game(
            gameId,
            "本地标题",
            "本地简介",
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
