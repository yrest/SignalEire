namespace IrelandLiveSignals.Core.Models;

// Latest known position per vehicle — upserted on each poll
public class VehicleObservation
{
    public string VehicleId { get; set; } = string.Empty;
    public string? TripId { get; set; }
    public string? RouteId { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public float? Bearing { get; set; }
    public float? SpeedKph { get; set; }
    public int GpsAgeSeconds { get; set; }
    // "ok" | "stale" | "no_trip"
    public string QualityStatus { get; set; } = "ok";
}
