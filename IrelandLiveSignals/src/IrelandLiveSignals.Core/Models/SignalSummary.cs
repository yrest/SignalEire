namespace IrelandLiveSignals.Core.Models;

public record SignalSummary
{
    public Guid Id { get; init; }
    public string Module { get; init; } = "";
    public string Region { get; init; } = "";
    public DateOnly Date { get; init; }
    public string SummaryText { get; init; } = "";
    public Dictionary<string, object> Metadata { get; init; } = new();
}
