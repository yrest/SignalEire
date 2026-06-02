namespace IrelandLiveSignals.Core.Models;

public record AlertRule
{
    public string Id { get; init; } = string.Empty;
    public string RuleName { get; init; } = string.Empty;
    public string Region { get; init; } = "ROI";

    // Threshold conditions — null means "don't check"
    public double? Co2BelowGPerKwh { get; init; }
    public double? RenewablesAbovePercent { get; init; }
    public double? GreenScoreAbove { get; init; }

    // Quiet hours — no alerts fired during this window
    public TimeOnly? QuietHoursStart { get; init; }
    public TimeOnly? QuietHoursEnd { get; init; }

    public int MaxAlertsPerDay { get; init; } = 2;
    public bool IsActive { get; init; } = true;
    public DateTimeOffset CreatedAtUtc { get; init; }

    // Phase 5 user association
    public string? UserId { get; init; }

    // Phase 6 digest fields
    public string DeliveryMode { get; init; } = "immediate";   // "immediate" | "digest"
    public string DigestSchedule { get; init; } = "daily";
    public TimeOnly DigestTime { get; init; } = new(08, 00);
}
