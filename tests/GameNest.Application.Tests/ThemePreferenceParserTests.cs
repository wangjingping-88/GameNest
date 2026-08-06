using GameNest.Application;

namespace GameNest.Application.Tests;

public sealed class ThemePreferenceParserTests
{
    [Theory]
    [InlineData("Light", ThemePreference.Light)]
    [InlineData("dark", ThemePreference.Dark)]
    [InlineData("SYSTEM", ThemePreference.System)]
    public void ParseOrDefaultReturnsKnownValue(string value, ThemePreference expected)
    {
        var actual = ThemePreferenceParser.ParseOrDefault(value);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData("42")]
    public void ParseOrDefaultReturnsLightForInvalidValue(string? value)
    {
        var actual = ThemePreferenceParser.ParseOrDefault(value);

        Assert.Equal(ThemePreference.Light, actual);
    }
}
