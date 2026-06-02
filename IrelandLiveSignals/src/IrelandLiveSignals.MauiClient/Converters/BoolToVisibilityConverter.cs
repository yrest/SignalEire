using System.Globalization;

namespace IrelandLiveSignals.MauiClient.Converters;

/// <summary>
/// Converts a boolean to a visibility (true → visible, false → hidden).
/// Set ConverterParameter to "Invert" to reverse the logic.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        bool invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        return invert ? !flag : flag;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
