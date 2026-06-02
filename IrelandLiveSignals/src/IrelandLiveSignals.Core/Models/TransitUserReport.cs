namespace IrelandLiveSignals.Core.Models;

/// <summary>Report types: bus_seen | bus_not_appeared | bus_passed_full | wrong_destination | gps_marker_wrong</summary>
public class TransitUserReport
{
    public string Id { get; set; } = string.Empty;
    public string StopId { get; set; } = string.Empty;
    public string RouteId { get; set; } = string.Empty;
    public string? TripId { get; set; }
    public string ReportType { get; set; } = string.Empty;
    public DateTimeOffset ReportedAtUtc { get; set; }
    public double? ReporterLat { get; set; }
    public double? ReporterLon { get; set; }
    /// <summary>0.0–1.0 trust weighting applied during aggregation</summary>
    public double TrustWeight { get; set; } = 0.4;
}
