using GameNest.Application;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace GameNest.App.Converters;

public sealed class ThemePreferenceToElementThemeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        _ = targetType;
        _ = parameter;
        _ = language;

        return value is ThemePreference preference
            ? preference switch
            {
                ThemePreference.Light => ElementTheme.Light,
                ThemePreference.Dark => ElementTheme.Dark,
                ThemePreference.System => ElementTheme.Default,
                _ => ElementTheme.Light,
            }
            : ElementTheme.Light;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
