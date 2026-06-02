namespace IrelandLiveSignals.Core.Models;

public record RawGridSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public DateTimeOffset RetrievedAtUtc { get; init; }
    public string RawPayloadPath { get; init; } = string.Empty;
    public string Hash { get; init; } = string.Empty;
}
