using System.Globalization;

namespace IrelandLiveSignals.MauiClient.Converters;

/// <summary>
/// Maps a CO2 intensity value (gCO₂/kWh) to a colour indicating grid cleanliness.
/// Clean (less than 100): green; moderate (less than 250): orange; dirty: red.
/// </summary>
public sealed class Co2ToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double co2)
        {
            if (co2 < 100) return Colors.Green;
            if (co2 < 250) return Colors.Orange;
            return Colors.Red;
        }

        return Colors.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
