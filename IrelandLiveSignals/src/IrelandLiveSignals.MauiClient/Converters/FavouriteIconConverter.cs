using System.Globalization;

namespace IrelandLiveSignals.MauiClient.Converters;

public sealed class FavouriteIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "♥ Saved" : "♡ Save";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
