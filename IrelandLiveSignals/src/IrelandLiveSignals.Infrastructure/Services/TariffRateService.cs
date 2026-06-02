using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Models;
using Microsoft.Extensions.Logging;

namespace IrelandLiveSignals.Infrastructure.Services;

public sealed class TariffRateService : ITariffRateService
{
    private readonly ILogger<TariffRateService> _logger;

    public TariffRateService(ILogger<TariffRateService> logger)
    {
        _logger = logger;
    }

    public decimal GetRateAt(TariffPlanEntity plan, TimeOnly irelandLocalTime, DayOfWeek dayOfWeek)
    {
        // Special case: flat rate (start == end means all-day)
        var candidates = plan.Periods.Where(p =>
            MatchesDayType(p.DayType, dayOfWeek) &&
            (p.StartTime == p.EndTime || IsInPeriod(irelandLocalTime, p))
        ).ToList();

        if (candidates.Count == 0)
        {
            _logger.LogWarning("No tariff period matched for plan {Plan} at {Time} on {Day}. Using first period fallback.",
                plan.Name, irelandLocalTime, dayOfWeek);
            return plan.Periods.Count > 0 ? plan.Periods[0].RatePerKwh : 0.35m;
        }

        return candidates[0].RatePerKwh;
    }

    public decimal GetAverageRateForWindow(TariffPlanEntity plan, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        var slots = new List<decimal>();
        var irelandTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");
        var current = windowStart;

        while (current < windowEnd)
        {
            var local = TimeZoneInfo.ConvertTime(current, irelandTz);
            var localTime = TimeOnly.FromDateTime(local.DateTime);
            var dayOfWeek = local.DayOfWeek;
            slots.Add(GetRateAt(plan, localTime, dayOfWeek));
            current = current.AddMinutes(5);
        }

        return slots.Count > 0 ? slots.Average() : 0;
    }

    public static bool IsInPeriod(TimeOnly time, TariffRatePeriod period)
    {
        if (period.StartTime == period.EndTime) return true; // all-day
        return period.StartTime <= period.EndTime
            ? time >= period.StartTime && time < period.EndTime
            : time >= period.StartTime || time < period.EndTime;
    }

    private static bool MatchesDayType(string dayType, DayOfWeek dayOfWeek) =>
        dayType switch
        {
            "weekday" => dayOfWeek >= DayOfWeek.Monday && dayOfWeek <= DayOfWeek.Friday,
            "weekend" => dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday,
            _         => true // "all"
        };
}
