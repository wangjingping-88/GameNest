using GameNest.Domain;

namespace GameNest.Domain.Tests;

public sealed class OverlayProfileTests
{
    [Fact]
    public void DefaultProfileMatchesProductDefaults()
    {
        var profile = OverlayProfile.CreateDefault(nowUtc: new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero));

        Assert.Null(profile.GameId);
        Assert.True(profile.IsEnabled);
        Assert.Equal(OverlayPosition.TopRight, profile.Position);
        Assert.Equal(100, profile.ScalePercent);
        Assert.Equal(88, profile.BackgroundOpacityPercent);
        Assert.Equal("Ctrl+Shift+F12", profile.ToggleHotkey);
        Assert.True(profile.HideWhenGameNotForeground);
        Assert.True(profile.ShowFps && profile.ShowCpu && profile.ShowGpu && profile.ShowRam);
    }

    [Theory]
    [InlineData("Ctrl+Shift+F12", OverlayHotkeyModifiers.Control | OverlayHotkeyModifiers.Shift, "F12")]
    [InlineData("alt + a", OverlayHotkeyModifiers.Alt, "A")]
    [InlineData("Win+Ctrl+9", OverlayHotkeyModifiers.Windows | OverlayHotkeyModifiers.Control, "9")]
    public void HotkeyParserNormalizesSupportedGestures(
        string value,
        OverlayHotkeyModifiers modifiers,
        string key)
    {
        var hotkey = OverlayHotkey.Parse(value);

        Assert.Equal(modifiers, hotkey.Modifiers);
        Assert.Equal(key, hotkey.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("F12")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+Mouse1")]
    [InlineData("Ctrl+A+B")]
    public void HotkeyParserRejectsUnsafeOrAmbiguousGestures(string value) =>
        Assert.False(OverlayHotkey.TryParse(value, out _));

    [Fact]
    public void PerGameProfileReplacesGlobalProfileAsAWhole()
    {
        var global = OverlayProfile.CreateDefault();
        var gameId = Guid.NewGuid();
        var game = new OverlayProfile(
            Guid.NewGuid(),
            gameId,
            false,
            OverlayPosition.BottomLeft,
            125,
            72,
            false,
            true,
            false,
            true,
            "Alt+F11",
            false,
            DateTimeOffset.UtcNow);

        Assert.Same(game, OverlayProfile.Resolve(global, game));
        Assert.Same(global, OverlayProfile.Resolve(global, null));
    }
}
