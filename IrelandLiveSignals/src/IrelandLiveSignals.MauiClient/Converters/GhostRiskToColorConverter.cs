using System.Globalization;

namespace IrelandLiveSignals.MauiClient.Converters;

/// <summary>
/// Maps a ghost-risk score (0.0–1.0) to a traffic-light colour.
/// Low risk (less than 0.3): green; medium (less than 0.7): orange; high: red.
/// </summary>
public sealed class GhostRiskToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double risk)
        {
            if (risk < 0.3) return Colors.Green;
            if (risk < 0.7) return Colors.Orange;
            return Colors.Red;
        }

        return Colors.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
