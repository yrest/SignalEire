using IrelandLiveSignals.Core.Models;

namespace IrelandLiveSignals.Core.Services;

public record ForecastSlot
{
    public DateTimeOffset SlotUtc { get; init; }
    public double EstimatedCo2GPerKwh { get; init; }
    public double EstimatedRenewablesPercent { get; init; }
    public double EstimatedGreenScore { get; init; }
    public double Confidence { get; init; }
    public bool IsActual { get; init; }
}

public static class GridForecastService
{
    private const int SlotMinutes = 30;
    private const int MaxLookbackDays = 7;

    /// <summary>
    /// Produces forecast slots from now until the deadline.
    /// Uses actual readings where available, then a time-of-day bucket average from
    /// historical data as a trend proxy. Confidence degrades linearly with lookahead.
    /// </summary>
    public static IReadOnlyList<ForecastSlot> Forecast(
        IReadOnlyList<GridReading> history,
        DateTimeOffset now,
        DateTimeOffset deadline)
    {
        var slots = new List<ForecastSlot>();
        if (history.Count == 0) return slots;

        // Build time-of-day lookup: key = (DayOfWeek, HalfHourBucket) -> avg metrics
        var buckets = BuildTimeBuckets(history);

        var cursor = RoundUpToSlot(now);
        while (cursor <= deadline)
        {
            var actual = history.FirstOrDefault(r =>
                Math.Abs((r.TimestampUtc - cursor).TotalMinutes) <= SlotMinutes / 2.0);

            if (actual is not null)
            {
                slots.Add(new ForecastSlot
                {
                    SlotUtc = cursor,
                    EstimatedCo2GPerKwh = actual.Co2IntensityGPerKwh,
                    EstimatedRenewablesPercent = actual.RenewablesPercent,
                    EstimatedGreenScore = actual.GreenScore,
                    Confidence = 1.0,
                    IsActual = true
                });
            }
            else
            {
                var (co2, renewables, baseConf) = LookupBucket(buckets, cursor, history);
                // Confidence degrades: 0.85 at 1h lookahead → ~0.40 at 24h lookahead
                var hoursAhead = (cursor - now).TotalHours;
                var lookaheadPenalty = Math.Clamp(hoursAhead / 48.0, 0.0, 0.45);
                var confidence = Math.Max(0.15, baseConf - lookaheadPenalty);

                var (score, _, _) = GreenScoringService.Compute(renewables, co2, 0);

                slots.Add(new ForecastSlot
                {
                    SlotUtc = cursor,
                    EstimatedCo2GPerKwh = co2,
                    EstimatedRenewablesPercent = renewables,
                    EstimatedGreenScore = score,
                    Confidence = confidence,
                    IsActual = false
                });
            }

            cursor = cursor.AddMinutes(SlotMinutes);
        }

        return slots;
    }

    private static DateTimeOffset RoundUpToSlot(DateTimeOffset t)
    {
        var minutes = t.Minute < SlotMinutes ? SlotMinutes : 60;
        return new DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Offset)
            .AddMinutes(minutes);
    }

    private static Dictionary<(int Hour, int HalfHour), (double Co2, double Renewables, int Count)> BuildTimeBuckets(
        IReadOnlyList<GridReading> history)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-MaxLookbackDays);
        var result = new Dictionary<(int, int), (double, double, int)>();

        foreach (var r in history.Where(r => r.TimestampUtc >= cutoff))
        {
            var key = (r.TimestampUtc.Hour, r.TimestampUtc.Minute < 30 ? 0 : 1);
            if (result.TryGetValue(key, out var existing))
                result[key] = (existing.Item1 + r.Co2IntensityGPerKwh, existing.Item2 + r.RenewablesPercent, existing.Item3 + 1);
            else
                result[key] = (r.Co2IntensityGPerKwh, r.RenewablesPercent, 1);
        }

        return result;
    }

    private static (double Co2, double Renewables, double Confidence) LookupBucket(
        Dictionary<(int, int), (double, double, int)> buckets,
        DateTimeOffset slot,
        IReadOnlyList<GridReading> history)
    {
        var key = (slot.Hour, slot.Minute < 30 ? 0 : 1);
        if (buckets.TryGetValue(key, out var val) && val.Item3 > 0)
        {
            var conf = Math.Min(0.85, 0.5 + (val.Item3 / 14.0) * 0.35); // more samples → higher base conf
            return (val.Item1 / val.Item3, val.Item2 / val.Item3, conf);
        }

        // Fall back to overall average
        var avgCo2 = history.Average(r => r.Co2IntensityGPerKwh);
        var avgRen = history.Average(r => r.RenewablesPercent);
        return (avgCo2, avgRen, 0.40);
    }
}
