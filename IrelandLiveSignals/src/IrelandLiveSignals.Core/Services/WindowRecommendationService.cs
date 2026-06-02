using IrelandLiveSignals.Core.Models;

namespace IrelandLiveSignals.Core.Services;

public record WindowCandidate
{
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset End { get; init; }
    public double AverageCo2 { get; init; }
    public double AverageRenewables { get; init; }
    public double AverageGreenScore { get; init; }
    public double AverageRelativeCost { get; init; }
    public double Confidence { get; init; }
    public double CompositeScore { get; init; }
}

public static class WindowRecommendationService
{
    /// <summary>
    /// Evaluates all valid windows in the forecast and returns the best one
    /// according to the requested mode. Also returns the current window score
    /// (first slot) for comparison.
    /// </summary>
    public static (WindowCandidate? Best, double CurrentCo2) FindBestWindow(
        IReadOnlyList<ForecastSlot> forecast,
        int durationMinutes,
        string mode,
        LegacyTariffPlan tariff,
        TimeOnly? quietStart = null,
        TimeOnly? quietEnd = null)
    {
        if (forecast.Count == 0) return (null, 0);

        var currentCo2 = forecast[0].EstimatedCo2GPerKwh;
        var slotsNeeded = (int)Math.Ceiling(durationMinutes / 30.0);
        if (slotsNeeded < 1) slotsNeeded = 1;

        var candidates = new List<WindowCandidate>();

        for (int i = 0; i <= forecast.Count - slotsNeeded; i++)
        {
            var window = forecast.Skip(i).Take(slotsNeeded).ToList();

            // Skip windows that overlap quiet hours
            if (quietStart.HasValue && quietEnd.HasValue && WindowOverlapsQuietHours(window, quietStart.Value, quietEnd.Value))
                continue;

            var avgCo2 = window.Average(s => s.EstimatedCo2GPerKwh);
            var avgRen = window.Average(s => s.EstimatedRenewablesPercent);
            var avgGreen = window.Average(s => s.EstimatedGreenScore);
            var avgConf = window.Average(s => s.Confidence);
            var avgCost = window.Average(s => TariffCatalogue.RelativeCostAt(tariff, TimeOnly.FromDateTime(s.SlotUtc.LocalDateTime)));

            var composite = ComputeComposite(avgGreen, avgCost, mode);

            candidates.Add(new WindowCandidate
            {
                Start = window.First().SlotUtc,
                End = window.Last().SlotUtc.AddMinutes(30),
                AverageCo2 = avgCo2,
                AverageRenewables = avgRen,
                AverageGreenScore = avgGreen,
                AverageRelativeCost = avgCost,
                Confidence = avgConf,
                CompositeScore = composite
            });
        }

        if (candidates.Count == 0) return (null, currentCo2);

        var best = candidates.OrderByDescending(c => c.CompositeScore).First();
        return (best, currentCo2);
    }

    private static double ComputeComposite(double greenScore, double relativeCost, string mode)
    {
        var cheapnessScore = 1.0 - Math.Clamp(relativeCost, 0.0, 1.0);
        return mode switch
        {
            "cleanest"   => 0.90 * greenScore + 0.10 * cheapnessScore,
            "cost_first" => 0.20 * greenScore + 0.80 * cheapnessScore,
            _            => 0.55 * greenScore + 0.45 * cheapnessScore  // balanced
        };
    }

    private static bool WindowOverlapsQuietHours(List<ForecastSlot> window, TimeOnly quietStart, TimeOnly quietEnd)
    {
        foreach (var slot in window)
        {
            var t = TimeOnly.FromDateTime(slot.SlotUtc.LocalDateTime);
            bool inQuiet = quietStart <= quietEnd
                ? t >= quietStart && t <= quietEnd
                : t >= quietStart || t <= quietEnd;
            if (inQuiet) return true;
        }
        return false;
    }
}
