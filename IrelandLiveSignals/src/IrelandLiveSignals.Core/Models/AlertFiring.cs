namespace IrelandLiveSignals.Core.Models;

public class AlertFiring
{
    public string Id { get; set; } = "";
    public string AlertRuleId { get; set; } = "";
    public string? UserId { get; set; }
    public DateTimeOffset FiredAtUtc { get; set; }
    public double? Co2Value { get; set; }
    public double? RenewablesPercent { get; set; }
    public bool IncludedInDigest { get; set; } = false;
}
