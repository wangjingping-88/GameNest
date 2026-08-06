using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GameNest.App.Converters;

public sealed class LocalPathToBitmapImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        _ = targetType;
        _ = language;

        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var image = new BitmapImage(new Uri(path, UriKind.Absolute));
        if (parameter is string sizeText && int.TryParse(sizeText, out var decodeWidth))
        {
            image.DecodePixelWidth = decodeWidth;
        }

        return image;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
