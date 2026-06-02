using System.Text.Json.Serialization;

namespace IrelandLiveSignals.Infrastructure.EirGrid;

public class EirGridResponse
{
    [JsonPropertyName("Rows")]
    public List<EirGridRow> Rows { get; set; } = new();
}

public class EirGridRow
{
    [JsonPropertyName("EffectiveTime")]
    public string? EffectiveTime { get; set; }

    [JsonPropertyName("Value")]
    public double? Value { get; set; }

    [JsonPropertyName("FieldName")]
    public string? FieldName { get; set; }

    // generationactual rows use these
    [JsonPropertyName("PUMP_STORAGE_GENERATION")]
    public double? PumpStorageGeneration { get; set; }

    [JsonPropertyName("OTHER_FOSSIL_GENERATION")]
    public double? OtherFossilGeneration { get; set; }

    [JsonPropertyName("HYDRO_GENERATION")]
    public double? HydroGeneration { get; set; }

    [JsonPropertyName("WIND_GENERATION")]
    public double? WindGeneration { get; set; }

    [JsonPropertyName("COAL_GENERATION")]
    public double? CoalGeneration { get; set; }

    [JsonPropertyName("GAS_GENERATION")]
    public double? GasGeneration { get; set; }

    [JsonPropertyName("NET_IMPORT")]
    public double? NetImport { get; set; }

    [JsonPropertyName("ACTUAL_GENERATION")]
    public double? ActualGeneration { get; set; }
}
