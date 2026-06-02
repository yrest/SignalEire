namespace IrelandLiveSignals.Core.Models;

public record GridRecommendation
{
    public string Id { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public string DeviceType { get; init; } = string.Empty;   // "ev" | "dishwasher" | "washing_machine" | "generic"
    public DateTimeOffset CreatedAtUtc { get; init; }

    // Input parameters
    public double RequiredKwh { get; init; }
    public double ChargerKw { get; init; }
    public DateTimeOffset DeadlineUtc { get; init; }
    public string Mode { get; init; } = "balanced";           // "cleanest" | "balanced" | "cost_first"
    public string TariffPlan { get; init; } = "standard";

    // Recommendation
    public string Decision { get; init; } = string.Empty;     // "start_now" | "wait"
    public DateTimeOffset? RecommendedStartUtc { get; init; }
    public DateTimeOffset? RecommendedEndUtc { get; init; }
    public int RequiredDurationMinutes { get; init; }

    // Estimates
    public double? EstimatedAverageCo2GPerKwh { get; init; }
    public double? EstimatedSavingKgCo2 { get; init; }
    public double Confidence { get; init; }
    public string[] Explanation { get; init; } = Array.Empty<string>();
}
