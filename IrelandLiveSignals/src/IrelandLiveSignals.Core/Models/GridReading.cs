namespace IrelandLiveSignals.Core.Models;

public record GridReading
{
    public string Id { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; }
    public double SystemDemandMw { get; init; }
    public double WindGenerationMw { get; init; }
    public double? SolarGenerationMw { get; init; }
    public double RenewablesPercent { get; init; }
    public double Co2IntensityGPerKwh { get; init; }
    public double? InterconnectorImportMw { get; init; }
    public double? InterconnectorExportMw { get; init; }
    public int DataFreshnessSeconds { get; init; }
    public double GreenScore { get; init; }
    public string GreenStatus { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public string QualityStatus { get; init; } = string.Empty;
}
