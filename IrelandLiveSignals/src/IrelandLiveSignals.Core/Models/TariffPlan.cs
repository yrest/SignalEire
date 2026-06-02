namespace IrelandLiveSignals.Core.Models;

public record TariffWindow
{
    public TimeOnly Start { get; init; }
    public TimeOnly End { get; init; }
    public string Label { get; init; } = string.Empty;   // "night_rate" | "day_rate" | "peak"
    public double RelativeCost { get; init; }              // 1.0 = standard; <1.0 = cheaper
}

public record LegacyTariffPlan
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public IReadOnlyList<TariffWindow> Windows { get; init; } = Array.Empty<TariffWindow>();
}
