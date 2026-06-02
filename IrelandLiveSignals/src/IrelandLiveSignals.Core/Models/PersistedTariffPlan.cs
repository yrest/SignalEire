namespace IrelandLiveSignals.Core.Models;

public class TariffPlanEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; } = false;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public List<TariffRatePeriod> Periods { get; set; } = [];
}

public class TariffRatePeriod
{
    public string Id { get; set; } = string.Empty;
    public string TariffPlanId { get; set; } = string.Empty;
    public string PeriodName { get; set; } = string.Empty;
    public string DayType { get; set; } = "all";
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public decimal RatePerKwh { get; set; }
}
