using IrelandLiveSignals.Core.Services;
using Xunit;

namespace IrelandLiveSignals.Tests;

public class TariffCatalogueTests
{
    [Fact]
    public void StandardPlan_AlwaysDayRate()
    {
        var plan = TariffCatalogue.GetById("standard");
        Assert.Equal(1.0, TariffCatalogue.RelativeCostAt(plan, new TimeOnly(14, 0)));
        Assert.Equal(1.0, TariffCatalogue.RelativeCostAt(plan, new TimeOnly(2, 0)));
    }

    [Theory]
    [InlineData(0, 0)]    // midnight — night rate
    [InlineData(3, 0)]    // 03:00 — night rate
    [InlineData(7, 30)]   // 07:30 — night rate
    [InlineData(23, 30)]  // 23:30 — night rate
    public void ElectricIreland_NightRate_AtNightHours(int hour, int minute)
    {
        var plan = TariffCatalogue.GetById("electric_ireland_home_ev_smart");
        var cost = TariffCatalogue.RelativeCostAt(plan, new TimeOnly(hour, minute));
        Assert.True(cost < 0.5, $"Expected night rate at {hour:D2}:{minute:D2}, got {cost}");
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(15, 0)]
    [InlineData(20, 0)]
    public void ElectricIreland_DayRate_AtDayHours(int hour, int minute)
    {
        var plan = TariffCatalogue.GetById("electric_ireland_home_ev_smart");
        var cost = TariffCatalogue.RelativeCostAt(plan, new TimeOnly(hour, minute));
        Assert.Equal(1.0, cost);
    }

    [Fact]
    public void BordGais_NightSaver_9amIsStillNightRate()
    {
        var plan = TariffCatalogue.GetById("bord_gais_night_saver");
        // Bord Gáis night rate ends at 09:00 — 08:50 should be cheap
        var cost = TariffCatalogue.RelativeCostAt(plan, new TimeOnly(8, 50));
        Assert.True(cost < 0.5);
    }

    [Fact]
    public void UnknownPlanId_FallsBackToStandard()
    {
        var plan = TariffCatalogue.GetById("nonexistent_plan");
        Assert.Equal("standard", plan.Id);
    }
}
