using IrelandLiveSignals.Infrastructure.Persistence;
using IrelandLiveSignals.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IrelandLiveSignals.IntegrationTests;

/// <summary>
/// Tests that TariffPlanSeeder correctly populates the TariffPlans table
/// and is idempotent when run multiple times.
/// </summary>
public class TariffPlanSeederTests
{
    private static GridDbContext CreateFreshDb()
    {
        var options = new DbContextOptionsBuilder<GridDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new GridDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task SeedAsync_OnFreshDb_CreatesTwoPlans()
    {
        await using var db = CreateFreshDb();

        await TariffPlanSeeder.SeedAsync(db);

        var count = await db.TariffPlans.CountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task SeedAsync_OnFreshDb_ContainsNightRateAndFlatRate()
    {
        await using var db = CreateFreshDb();

        await TariffPlanSeeder.SeedAsync(db);

        var plans = await db.TariffPlans.ToListAsync();
        Assert.Contains(plans, p => p.PlanType == "night_rate");
        Assert.Contains(plans, p => p.PlanType == "flat");
    }

    [Fact]
    public async Task SeedAsync_RunTwice_StillOnlyTwoPlans()
    {
        await using var db = CreateFreshDb();

        await TariffPlanSeeder.SeedAsync(db);
        await TariffPlanSeeder.SeedAsync(db); // second run should be a no-op

        var count = await db.TariffPlans.CountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task SeedAsync_NightRatePlan_HasExpectedPeriods()
    {
        await using var db = CreateFreshDb();

        await TariffPlanSeeder.SeedAsync(db);

        var nightPlan = await db.TariffPlans
            .Include(p => p.Periods)
            .FirstAsync(p => p.PlanType == "night_rate");

        Assert.Equal(2, nightPlan.Periods.Count);
        Assert.Contains(nightPlan.Periods, p => p.RatePerKwh == 0.1256m);
        Assert.Contains(nightPlan.Periods, p => p.RatePerKwh == 0.4321m);
    }

    [Fact]
    public async Task SeedAsync_FlatRatePlan_HasSingleAllDayPeriod()
    {
        await using var db = CreateFreshDb();

        await TariffPlanSeeder.SeedAsync(db);

        var flatPlan = await db.TariffPlans
            .Include(p => p.Periods)
            .FirstAsync(p => p.PlanType == "flat");

        Assert.Single(flatPlan.Periods);
        var period = flatPlan.Periods[0];
        // All-day period: start == end == 00:00
        Assert.Equal(period.StartTime, period.EndTime);
        Assert.Equal(0.3850m, period.RatePerKwh);
    }
}
