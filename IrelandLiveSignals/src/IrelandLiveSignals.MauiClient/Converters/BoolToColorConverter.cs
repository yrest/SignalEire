using System.Globalization;

namespace IrelandLiveSignals.MauiClient.Converters;

/// <summary>
/// Converts a boolean value to one of two colours.
/// ConverterParameter format: "TrueColor|FalseColor" (named MAUI colours, e.g. "Green|Red").
/// </summary>
public sealed class BoolToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        var parts = (parameter as string)?.Split('|');
        if (parts?.Length == 2)
        {
            var colorName = flag ? parts[0] : parts[1];
            if (Color.TryParse(colorName, out var parsed))
                return parsed;
        }

        return flag ? Colors.Green : Colors.Red;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
