namespace IrelandLiveSignals.Core.Models;

public record StopArrivalPrediction
{
    public string StopId { get; init; } = string.Empty;
    public string RouteId { get; init; } = string.Empty;
    public string RouteShortName { get; init; } = string.Empty;
    public string TripId { get; init; } = string.Empty;
    public string Destination { get; init; } = string.Empty;
    public DateTimeOffset ScheduledArrivalUtc { get; init; }
    public DateTimeOffset? PredictedArrivalUtc { get; init; }
    public bool VehicleConfirmed { get; init; }
    public string? VehicleId { get; init; }
    public int? GpsAgeSeconds { get; init; }
    public double? DistanceToStopMeters { get; init; }
    public double Confidence { get; init; }
    // "low" | "medium" | "high"
    public string GhostRisk { get; init; } = "high";
    // "Vehicle confirmed" | "Likely active" | "Timetable only" | "Stale GPS" | "Cancelled"
    public string StatusLabel { get; init; } = string.Empty;
}
