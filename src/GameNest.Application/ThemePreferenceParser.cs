namespace GameNest.Application;

public static class ThemePreferenceParser
{
    public static ThemePreference ParseOrDefault(
        string? value,
        ThemePreference fallback = ThemePreference.Light)
    {
        return Enum.TryParse<ThemePreference>(value, ignoreCase: true, out var preference) &&
               Enum.IsDefined(preference)
            ? preference
            : fallback;
    }
}
