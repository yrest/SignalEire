namespace IrelandLiveSignals.Core.Models;

public class VehicleTrailPoint
{
    public long Id { get; set; }
    public string VehicleId { get; set; } = string.Empty;
    public string? TripId { get; set; }
    public string? RouteId { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public float? Bearing { get; set; }
    public float? SpeedKph { get; set; }
    public int GpsAgeSeconds { get; set; }
}
