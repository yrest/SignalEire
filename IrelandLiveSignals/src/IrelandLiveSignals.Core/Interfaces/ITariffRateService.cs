using IrelandLiveSignals.Core.Models;

namespace IrelandLiveSignals.Core.Interfaces;

public interface ITariffRateService
{
    decimal GetRateAt(TariffPlanEntity plan, TimeOnly irelandLocalTime, DayOfWeek dayOfWeek);
    decimal GetAverageRateForWindow(TariffPlanEntity plan, DateTimeOffset windowStart, DateTimeOffset windowEnd);
}
