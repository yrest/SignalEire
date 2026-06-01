namespace IrelandLiveSignals.Core.Models;

public record AlertDelivery
{
    public string Id { get; init; } = string.Empty;
    public string AlertRuleId { get; init; } = string.Empty;
    public string GridReadingId { get; init; } = string.Empty;
    public DateTimeOffset FiredAtUtc { get; init; }
    public string Message { get; init; } = string.Empty;
    public double TriggerCo2GPerKwh { get; init; }
    public double TriggerRenewablesPercent { get; init; }
    public double TriggerGreenScore { get; init; }

    // "pending" | "sent" | "suppressed"
    public string DeliveryStatus { get; init; } = "pending";
}
