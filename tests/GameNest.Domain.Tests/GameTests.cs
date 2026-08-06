using GameNest.Domain;

namespace GameNest.Domain.Tests;

public sealed class GameTests
{
    [Fact]
    public void ConstructorRejectsLaunchProfileFromAnotherGame()
    {
        var gameId = Guid.NewGuid();
        var profile = CreateProfile(Guid.NewGuid());

        var exception = Assert.Throws<ArgumentException>(
            () => new Game(
                gameId,
                "测试游戏",
                null,
                @"D:\Games\测试游戏",
                GameSourceType.ManualExecutable,
                false,
                GameAvailability.Available,
                DateTimeOffset.UtcNow,
                null,
                0,
                profile,
                null));

        Assert.Contains("启动配置", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UserEditsAndFavoriteKeepStableIdentityAndLaunchPath()
    {
        var gameId = Guid.NewGuid();
        var game = new Game(
            gameId,
            "原名称",
            null,
            @"D:\Games\原名称",
            GameSourceType.ManualExecutable,
            false,
            GameAvailability.Available,
            DateTimeOffset.UtcNow,
            null,
            0,
            CreateProfile(gameId),
            null);

        var updated = game
            .WithUserEdits("新名称", "本地简介", "--safe mode", @"D:\Games\新目录")
            .WithFavorite(true);

        Assert.Equal(gameId, updated.Id);
        Assert.Equal("新名称", updated.Title);
        Assert.Equal("本地简介", updated.Description);
        Assert.True(updated.IsFavorite);
        Assert.Equal(@"D:\Games\游戏.exe", updated.LaunchProfile.ExecutablePath);
        Assert.Equal("--safe mode", updated.LaunchProfile.Arguments);
        Assert.Equal(@"D:\Games\新目录", updated.LaunchProfile.WorkingDirectory);
    }

    private static LaunchProfile CreateProfile(Guid gameId) =>
        new(
            Guid.NewGuid(),
            gameId,
            "默认",
            LaunchKind.Executable,
            @"D:\Games\游戏.exe",
            null,
            @"D:\Games",
            false,
            true);
}
