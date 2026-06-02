using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IrelandLiveSignals.Tests;

public class TariffRateServiceTests
{
    // Night rate: 23:00–08:00 @ 0.1256, Day rate: 08:00–23:00 @ 0.4321
    private static TariffPlanEntity BuildNightRatePlan() => new()
    {
        Id = "test_night_rate",
        Name = "Test Night Rate",
        Provider = "Test",
        PlanType = "night_rate",
        Periods =
        [
            new TariffRatePeriod
            {
                Id = "night",
                TariffPlanId = "test_night_rate",
                PeriodName = "Night Rate",
                DayType = "all",
                StartTime = new TimeOnly(23, 0),
                EndTime   = new TimeOnly(8, 0),
                RatePerKwh = 0.1256m
            },
            new TariffRatePeriod
            {
                Id = "day",
                TariffPlanId = "test_night_rate",
                PeriodName = "Day Rate",
                DayType = "all",
                StartTime = new TimeOnly(8, 0),
                EndTime   = new TimeOnly(23, 0),
                RatePerKwh = 0.4321m
            }
        ]
    };

    // Flat rate: start == end == 00:00 means all-day
    private static TariffPlanEntity BuildFlatRatePlan() => new()
    {
        Id = "test_flat_rate",
        Name = "Test Flat Rate",
        Provider = "Test",
        PlanType = "flat",
        Periods =
        [
            new TariffRatePeriod
            {
                Id = "flat",
                TariffPlanId = "test_flat_rate",
                PeriodName = "Standard Rate",
                DayType = "all",
                StartTime = new TimeOnly(0, 0),
                EndTime   = new TimeOnly(0, 0),
                RatePerKwh = 0.3850m
            }
        ]
    };

    // Weekend-aware plan: weekday rate + weekend rate
    private static TariffPlanEntity BuildWeekendPlan() => new()
    {
        Id = "test_weekend",
        Name = "Test Weekend Plan",
        Provider = "Test",
        PlanType = "weekend",
        Periods =
        [
            new TariffRatePeriod
            {
                Id = "weekday",
                TariffPlanId = "test_weekend",
                PeriodName = "Weekday Rate",
                DayType = "weekday",
                StartTime = new TimeOnly(0, 0),
                EndTime   = new TimeOnly(0, 0),
                RatePerKwh = 0.4000m
            },
            new TariffRatePeriod
            {
                Id = "weekend",
                TariffPlanId = "test_weekend",
                PeriodName = "Weekend Rate",
                DayType = "weekend",
                StartTime = new TimeOnly(0, 0),
                EndTime   = new TimeOnly(0, 0),
                RatePerKwh = 0.2500m
            }
        ]
    };

    private static TariffRateService CreateService() =>
        new(NullLogger<TariffRateService>.Instance);

    // ── GetRateAt tests ────────────────────────────────────────────────────

    [Fact]
    public void NightRate_At0030_ReturnsNightRate()
    {
        var svc = CreateService();
        var rate = svc.GetRateAt(BuildNightRatePlan(), new TimeOnly(0, 30), DayOfWeek.Monday);
        Assert.Equal(0.1256m, rate);
    }

    [Fact]
    public void NightRate_At1000_ReturnsDayRate()
    {
        var svc = CreateService();
        var rate = svc.GetRateAt(BuildNightRatePlan(), new TimeOnly(10, 0), DayOfWeek.Wednesday);
        Assert.Equal(0.4321m, rate);
    }

    [Fact]
    public void NightRate_At2330_ReturnsNightRate_MidnightSpanning()
    {
        // 23:30 is after night start (23:00) so still night rate
        var svc = CreateService();
        var rate = svc.GetRateAt(BuildNightRatePlan(), new TimeOnly(23, 30), DayOfWeek.Thursday);
        Assert.Equal(0.1256m, rate);
    }

    [Fact]
    public void NightRate_At0800_ReturnsDayRate_BoundaryInclusion()
    {
        // 08:00 is the start of the day period (day period uses >= 08:00)
        var svc = CreateService();
        var rate = svc.GetRateAt(BuildNightRatePlan(), new TimeOnly(8, 0), DayOfWeek.Friday);
        Assert.Equal(0.4321m, rate);
    }

    [Fact]
    public void NightRate_At2259_ReturnsDayRate_StillDay()
    {
        // 22:59 is before night start (23:00) so still day rate
        var svc = CreateService();
        var rate = svc.GetRateAt(BuildNightRatePlan(), new TimeOnly(22, 59), DayOfWeek.Tuesday);
        Assert.Equal(0.4321m, rate);
    }

    [Fact]
    public void FlatRate_AnyTime_ReturnsFlatRate()
    {
        var svc = CreateService();
        var plan = BuildFlatRatePlan();
        Assert.Equal(0.3850m, svc.GetRateAt(plan, new TimeOnly(0, 0), DayOfWeek.Monday));
        Assert.Equal(0.3850m, svc.GetRateAt(plan, new TimeOnly(12, 0), DayOfWeek.Wednesday));
        Assert.Equal(0.3850m, svc.GetRateAt(plan, new TimeOnly(23, 59), DayOfWeek.Sunday));
    }

    [Fact]
    public void WeekendDayType_Saturday1400_ReturnsWeekendRate()
    {
        var svc = CreateService();
        // Saturday = weekend
        var rate = svc.GetRateAt(BuildWeekendPlan(), new TimeOnly(14, 0), DayOfWeek.Saturday);
        Assert.Equal(0.2500m, rate);
    }

    [Fact]
    public void WeekendDayType_Monday1400_ReturnsWeekdayRate()
    {
        var svc = CreateService();
        var rate = svc.GetRateAt(BuildWeekendPlan(), new TimeOnly(14, 0), DayOfWeek.Monday);
        Assert.Equal(0.4000m, rate);
    }

    // ── GetAverageRateForWindow tests ──────────────────────────────────────

    [Fact]
    public void GetAverageRateForWindow_FullyInNight_ReturnsNightRate()
    {
        // 00:00–04:00 UTC is 00:00–04:00 Ireland time (no DST issue in winter)
        // Use a fixed winter date to avoid DST ambiguity
        var svc = CreateService();
        var plan = BuildNightRatePlan();

        // 2025-01-15 00:00–04:00 UTC (Ireland is UTC in January)
        var start = new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var end   = new DateTimeOffset(2025, 1, 15, 4, 0, 0, TimeSpan.Zero);

        var avg = svc.GetAverageRateForWindow(plan, start, end);

        Assert.Equal(0.1256m, avg);
    }

    [Fact]
    public void GetAverageRateForWindow_SpanningNightDayBoundary_ReturnsWeightedAverage()
    {
        // 07:00–09:00 UTC on 2025-01-15 (UTC == Ireland time in January)
        // 07:00–08:00 = night rate (0.1256), 08:00–09:00 = day rate (0.4321)
        // Each hour has 12 five-minute slots, 24 total: 12 night + 12 day
        var svc = CreateService();
        var plan = BuildNightRatePlan();

        var start = new DateTimeOffset(2025, 1, 15, 7, 0, 0, TimeSpan.Zero);
        var end   = new DateTimeOffset(2025, 1, 15, 9, 0, 0, TimeSpan.Zero);

        var avg = svc.GetAverageRateForWindow(plan, start, end);

        var expected = (0.1256m + 0.4321m) / 2m;
        Assert.Equal(expected, avg, precision: 4);
    }
}
