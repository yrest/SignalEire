using IrelandLiveSignals.Core.Models;

namespace IrelandLiveSignals.Core.Services;

/// <summary>
/// Hardcoded Irish tariff plans. Night-rate hours sourced from published plan documents;
/// replace with a database-backed catalogue when live tariff data is available.
/// </summary>
public static class TariffCatalogue
{
    public static IReadOnlyList<TariffPlan> All { get; } = new[]
    {
        new TariffPlan
        {
            Id = "standard",
            Name = "Standard (Day Rate Only)",
            Provider = "generic",
            Windows = new[]
            {
                new TariffWindow { Start = TimeOnly.MinValue, End = new TimeOnly(23, 59), Label = "day_rate", RelativeCost = 1.0 }
            }
        },
        new TariffPlan
        {
            Id = "electric_ireland_home_ev_smart",
            Name = "Home EV Smart",
            Provider = "Electric Ireland",
            // Night rate 23:00–08:00; day rate remainder
            Windows = new[]
            {
                new TariffWindow { Start = new TimeOnly(23, 0), End = new TimeOnly(23, 59, 59), Label = "night_rate", RelativeCost = 0.35 },
                new TariffWindow { Start = TimeOnly.MinValue,   End = new TimeOnly(7, 59, 59),  Label = "night_rate", RelativeCost = 0.35 },
                new TariffWindow { Start = new TimeOnly(8, 0),  End = new TimeOnly(22, 59, 59), Label = "day_rate",   RelativeCost = 1.0  }
            }
        },
        new TariffPlan
        {
            Id = "bord_gais_night_saver",
            Name = "Night Saver",
            Provider = "Bord Gáis Energy",
            // Night rate 23:00–09:00
            Windows = new[]
            {
                new TariffWindow { Start = new TimeOnly(23, 0), End = new TimeOnly(23, 59, 59), Label = "night_rate", RelativeCost = 0.32 },
                new TariffWindow { Start = TimeOnly.MinValue,   End = new TimeOnly(8, 59, 59),  Label = "night_rate", RelativeCost = 0.32 },
                new TariffWindow { Start = new TimeOnly(9, 0),  End = new TimeOnly(22, 59, 59), Label = "day_rate",   RelativeCost = 1.0  }
            }
        }
    };

    public static TariffPlan GetById(string id) =>
        All.FirstOrDefault(p => p.Id == id) ?? All[0];

    public static double RelativeCostAt(TariffPlan plan, TimeOnly time)
    {
        // Find the most specific window matching the time
        foreach (var w in plan.Windows)
        {
            bool inWindow = w.Start <= w.End
                ? time >= w.Start && time <= w.End
                : time >= w.Start || time <= w.End;  // wraps midnight
            if (inWindow) return w.RelativeCost;
        }
        return 1.0;
    }
}
