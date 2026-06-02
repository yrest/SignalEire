using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IrelandLiveSignals.Infrastructure.Services;

public static class TariffPlanSeeder
{
    public static async Task SeedAsync(GridDbContext db)
    {
        if (await db.TariffPlans.AnyAsync()) return;

        var nightRate = new TariffPlanEntity
        {
            Id = "generic_night_rate",
            Name = "Night Rate (Generic)",
            Provider = "Generic",
            PlanType = "night_rate",
            IsDefault = true,
            IsActive = true,
            Description = "Illustrative night rate plan. Update rates to match your actual plan.",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Periods =
            [
                new TariffRatePeriod
                {
                    Id = Guid.NewGuid().ToString("N"),
                    TariffPlanId = "generic_night_rate",
                    PeriodName = "Night Rate",
                    DayType = "all",
                    StartTime = new TimeOnly(23, 0),
                    EndTime = new TimeOnly(8, 0),
                    RatePerKwh = 0.1256m
                },
                new TariffRatePeriod
                {
                    Id = Guid.NewGuid().ToString("N"),
                    TariffPlanId = "generic_night_rate",
                    PeriodName = "Day Rate",
                    DayType = "all",
                    StartTime = new TimeOnly(8, 0),
                    EndTime = new TimeOnly(23, 0),
                    RatePerKwh = 0.4321m
                }
            ]
        };

        var flatRate = new TariffPlanEntity
        {
            Id = "generic_flat_rate",
            Name = "Flat Rate (Generic)",
            Provider = "Generic",
            PlanType = "flat",
            IsDefault = false,
            IsActive = true,
            Description = "Illustrative flat rate. Update to match your actual plan.",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Periods =
            [
                new TariffRatePeriod
                {
                    Id = Guid.NewGuid().ToString("N"),
                    TariffPlanId = "generic_flat_rate",
                    PeriodName = "Standard Rate",
                    DayType = "all",
                    StartTime = new TimeOnly(0, 0),
                    EndTime = new TimeOnly(0, 0),
                    RatePerKwh = 0.3850m
                }
            ]
        };

        db.TariffPlans.AddRange(nightRate, flatRate);
        await db.SaveChangesAsync();
    }
}
