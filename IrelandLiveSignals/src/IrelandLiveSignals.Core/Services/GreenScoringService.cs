namespace IrelandLiveSignals.Core.Services;

public static class GreenScoringService
{
    public static (double Score, string Status, string Recommendation) Compute(
        double renewablesPercent,
        double co2GPerKwh,
        int freshnessSeconds)
    {
        var normalizedRenewables = Math.Clamp(renewablesPercent / 100.0, 0.0, 1.0);
        var inverseNormalizedCo2 = 1.0 - Math.Clamp(co2GPerKwh / 600.0, 0.0, 1.0);
        var dataFreshnessScore = 1.0 - Math.Clamp(freshnessSeconds / 300.0, 0.0, 1.0);
        const double trendScore = 0.5;

        var score = 0.45 * normalizedRenewables
                  + 0.35 * inverseNormalizedCo2
                  + 0.10 * dataFreshnessScore
                  + 0.10 * trendScore;

        score = Math.Clamp(score, 0.0, 1.0);

        var (status, recommendation) = score switch
        {
            >= 0.65 => ("good", "Good time for flexible electricity use."),
            >= 0.40 => ("moderate", "Grid conditions are average. Non-urgent loads can wait."),
            _       => ("poor", "Grid is carbon-heavy. Defer flexible loads if possible.")
        };

        return (score, status, recommendation);
    }
}
