using IrelandLiveSignals.Core.Models;

namespace IrelandLiveSignals.Core.Services;

public record AlertEvaluationResult
{
    public AlertRule Rule { get; init; } = null!;
    public bool Fired { get; init; }
    public string? SuppressReason { get; init; }
    public AlertDelivery? Delivery { get; init; }
}

public static class AlertEvaluationService
{
    public static AlertEvaluationResult Evaluate(
        AlertRule rule,
        GridReading reading,
        int deliveriesTodayCount)
    {
        // Condition check
        bool conditionMet = CheckConditions(rule, reading);
        if (!conditionMet)
            return new AlertEvaluationResult { Rule = rule, Fired = false };

        // Daily cap
        if (deliveriesTodayCount >= rule.MaxAlertsPerDay)
            return new AlertEvaluationResult { Rule = rule, Fired = false, SuppressReason = "daily_cap_reached" };

        // Quiet hours check
        if (rule.QuietHoursStart.HasValue && rule.QuietHoursEnd.HasValue)
        {
            var now = TimeOnly.FromDateTime(reading.TimestampUtc.LocalDateTime);
            bool inQuiet = IsInQuietHours(now, rule.QuietHoursStart.Value, rule.QuietHoursEnd.Value);
            if (inQuiet)
                return new AlertEvaluationResult { Rule = rule, Fired = false, SuppressReason = "quiet_hours" };
        }

        var message = BuildMessage(rule, reading);
        var delivery = new AlertDelivery
        {
            Id = $"alert_{rule.Id}_{reading.TimestampUtc:yyyyMMddHHmmss}",
            AlertRuleId = rule.Id,
            GridReadingId = reading.Id,
            FiredAtUtc = DateTimeOffset.UtcNow,
            Message = message,
            TriggerCo2GPerKwh = reading.Co2IntensityGPerKwh,
            TriggerRenewablesPercent = reading.RenewablesPercent,
            TriggerGreenScore = reading.GreenScore,
            DeliveryStatus = "pending"
        };

        return new AlertEvaluationResult { Rule = rule, Fired = true, Delivery = delivery };
    }

    private static bool CheckConditions(AlertRule rule, GridReading reading)
    {
        if (rule.Co2BelowGPerKwh.HasValue && reading.Co2IntensityGPerKwh >= rule.Co2BelowGPerKwh.Value)
            return false;
        if (rule.RenewablesAbovePercent.HasValue && reading.RenewablesPercent <= rule.RenewablesAbovePercent.Value)
            return false;
        if (rule.GreenScoreAbove.HasValue && reading.GreenScore <= rule.GreenScoreAbove.Value)
            return false;
        return true;
    }

    private static bool IsInQuietHours(TimeOnly now, TimeOnly start, TimeOnly end)
    {
        return start <= end
            ? now >= start && now <= end
            : now >= start || now <= end;
    }

    private static string BuildMessage(AlertRule rule, GridReading reading)
    {
        var parts = new List<string>();

        if (rule.Co2BelowGPerKwh.HasValue)
            parts.Add($"CO₂ intensity is {reading.Co2IntensityGPerKwh:F0} g/kWh (below your threshold of {rule.Co2BelowGPerKwh:F0}).");
        if (rule.RenewablesAbovePercent.HasValue)
            parts.Add($"Renewable share is {reading.RenewablesPercent:F1}% (above your threshold of {rule.RenewablesAbovePercent:F1}%).");

        parts.Add(reading.Recommendation);
        return string.Join(" ", parts);
    }
}
