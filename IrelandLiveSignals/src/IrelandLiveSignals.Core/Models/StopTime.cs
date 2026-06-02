namespace IrelandLiveSignals.Core.Models;

public class StopTime
{
    public string TripId { get; set; } = string.Empty;
    public string StopId { get; set; } = string.Empty;
    public int StopSequence { get; set; }
    // Seconds from midnight — GTFS times can exceed 86400 (next-day trips)
    public int ArrivalSeconds { get; set; }
    public int DepartureSeconds { get; set; }
}
