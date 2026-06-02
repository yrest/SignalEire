namespace IrelandLiveSignals.Core.Models;

public class TransitReliabilityAggregate
{
    public string RouteId { get; set; } = string.Empty;
    public string StopId { get; set; } = string.Empty;
    public int TotalObservations { get; set; }
    public int VehicleConfirmedCount { get; set; }
    public int TimetableOnlyCount { get; set; }
    public int GhostCount { get; set; }
    public double AverageDelaySeconds { get; set; }
    public double ReliabilityScore { get; set; }
    public DateTimeOffset LastUpdatedUtc { get; set; }
    public DateTimeOffset? OldestObservationUtc { get; set; }
}
