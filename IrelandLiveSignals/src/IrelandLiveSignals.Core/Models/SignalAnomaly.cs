namespace IrelandLiveSignals.Core.Models;

public class SignalAnomaly
{
    public string Id { get; set; } = "";
    public string Module { get; set; } = "";          // "grid" | "transit"
    public string AnomalyType { get; set; } = "";
    public string Region { get; set; } = "";
    public string? RouteId { get; set; }
    public string? StopId { get; set; }
    public DateOnly Date { get; set; }
    public double ObservedValue { get; set; }
    public double BaselineValue { get; set; }
    public double DeviationZScore { get; set; }
    public string ExplanationText { get; set; } = "";
    public bool IndexedToQdrant { get; set; }
    public DateTimeOffset DetectedAtUtc { get; set; }
}
