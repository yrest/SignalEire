using IrelandLiveSignals.Core.Services;
using Xunit;

namespace IrelandLiveSignals.Tests;

public class WindowRecommendationServiceTests
{
    private static List<ForecastSlot> MakeSlots(int count, double co2, double renewables, DateTimeOffset? start = null)
    {
        var t = start ?? DateTimeOffset.UtcNow.AddMinutes(30);
        var (score, _, _) = GreenScoringService.Compute(renewables, co2, 0);
        return Enumerable.Range(0, count).Select(i => new ForecastSlot
        {
            SlotUtc = t.AddMinutes(i * 30),
            EstimatedCo2GPerKwh = co2,
            EstimatedRenewablesPercent = renewables,
            EstimatedGreenScore = score,
            Confidence = 0.8,
            IsActual = false
        }).ToList();
    }

    [Fact]
    public void FindBestWindow_ReturnsNull_WhenNoSlots()
    {
        var (best, _) = WindowRecommendationService.FindBestWindow(
            new List<ForecastSlot>(), 60, "balanced", TariffCatalogue.GetById("standard"));
        Assert.Null(best);
    }

    [Fact]
    public void FindBestWindow_ReturnsNull_WhenDurationExceedsAvailableSlots()
    {
        var slots = MakeSlots(2, 300, 40); // only 1 hour of slots, need 3 hours
        var (best, _) = WindowRecommendationService.FindBestWindow(
            slots, 180, "balanced", TariffCatalogue.GetById("standard"));
        Assert.Null(best);
    }

    [Fact]
    public void FindBestWindow_CleanestMode_PrefersLowerCo2Window()
    {
        // First 3 slots: dirty (high CO2), next 3: clean (low CO2)
        var start = DateTimeOffset.UtcNow.AddMinutes(30);
        var dirtySlots = MakeSlots(3, 450, 15, start);
        var cleanSlots = MakeSlots(3, 120, 75, start.AddMinutes(90));
        var allSlots = dirtySlots.Concat(cleanSlots).ToList();

        var (best, _) = WindowRecommendationService.FindBestWindow(
            allSlots, 90, "cleanest", TariffCatalogue.GetById("standard"));

        Assert.NotNull(best);
        Assert.True(best!.AverageCo2 < 200, $"Expected low CO2 window, got {best.AverageCo2}");
    }

    [Fact]
    public void FindBestWindow_CostFirstMode_PrefersNightRateWindow()
    {
        // Night rate starts at 23:00 — create slots straddling midnight
        var daySlot = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month,
            DateTimeOffset.UtcNow.Day, 14, 0, 0, TimeSpan.Zero);
        var nightSlot = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month,
            DateTimeOffset.UtcNow.Day, 23, 0, 0, TimeSpan.Zero);

        var daySlots   = MakeSlots(2, 300, 40, daySlot);
        var nightSlots = MakeSlots(2, 300, 40, nightSlot);
        var allSlots   = daySlots.Concat(nightSlots).ToList();

        var tariff = TariffCatalogue.GetById("electric_ireland_home_ev_smart");
        var (best, _) = WindowRecommendationService.FindBestWindow(allSlots, 60, "cost_first", tariff);

        Assert.NotNull(best);
        // Night-rate window should win in cost_first mode
        Assert.True(best!.AverageRelativeCost < 0.5, $"Expected night rate, got relative cost {best.AverageRelativeCost}");
    }

    [Fact]
    public void FindBestWindow_QuietHours_ExcludesOverlappingWindows()
    {
        var start = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month,
            DateTimeOffset.UtcNow.Day, 23, 0, 0, TimeSpan.Zero);
        var slots = MakeSlots(4, 100, 90, start); // very clean but all during quiet hours

        var (best, _) = WindowRecommendationService.FindBestWindow(
            slots, 60, "cleanest", TariffCatalogue.GetById("standard"),
            quietStart: new TimeOnly(22, 0),
            quietEnd: new TimeOnly(7, 0));

        Assert.Null(best); // all windows fall in quiet hours
    }

    [Fact]
    public void FindBestWindow_Duration_RoundsUpToHalfHourSlots()
    {
        var slots = MakeSlots(6, 200, 60);
        // 45 minutes should require 2 slots (2 × 30 min)
        var (best, _) = WindowRecommendationService.FindBestWindow(
            slots, 45, "balanced", TariffCatalogue.GetById("standard"));
        Assert.NotNull(best);
        var windowMinutes = (best!.End - best.Start).TotalMinutes;
        Assert.True(windowMinutes >= 45, $"Window too short: {windowMinutes} min");
    }
}
