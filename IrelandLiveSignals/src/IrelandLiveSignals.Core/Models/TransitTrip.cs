namespace IrelandLiveSignals.Core.Models;

public class TransitTrip
{
    public string TripId { get; set; } = string.Empty;
    public string RouteId { get; set; } = string.Empty;
    public string ServiceId { get; set; } = string.Empty;
    public string? TripHeadsign { get; set; }
    public int DirectionId { get; set; }
}
