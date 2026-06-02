using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Core.Services;
using Xunit;

namespace IrelandLiveSignals.Tests;

public class AlertEvaluationServiceTests
{
    private static GridReading MakeReading(double co2 = 200, double renewables = 60, double greenScore = 0.7)
        => new GridReading
        {
            Id = "r1", Region = "ROI", TimestampUtc = DateTimeOffset.UtcNow,
            Co2IntensityGPerKwh = co2, RenewablesPercent = renewables, GreenScore = greenScore,
            SystemDemandMw = 4000, WindGenerationMw = 1500, DataFreshnessSeconds = 30,
            GreenStatus = "good", Recommendation = "Good time.", QualityStatus = "ok"
        };

    private static AlertRule MakeRule(double? co2Below = null, double? renewablesAbove = null,
        double? greenScoreAbove = null, TimeOnly? quietStart = null, TimeOnly? quietEnd = null,
        int maxPerDay = 5)
        => new AlertRule
        {
            Id = "rule1", RuleName = "Test", Region = "ROI",
            Co2BelowGPerKwh = co2Below, RenewablesAbovePercent = renewablesAbove,
            GreenScoreAbove = greenScoreAbove, QuietHoursStart = quietStart, QuietHoursEnd = quietEnd,
            MaxAlertsPerDay = maxPerDay, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow
        };

    [Fact]
    public void Fires_WhenCo2BelowThreshold()
    {
        var result = AlertEvaluationService.Evaluate(MakeRule(co2Below: 250), MakeReading(co2: 200), 0);
        Assert.True(result.Fired);
        Assert.NotNull(result.Delivery);
    }

    [Fact]
    public void DoesNotFire_WhenCo2AboveThreshold()
    {
        var result = AlertEvaluationService.Evaluate(MakeRule(co2Below: 150), MakeReading(co2: 200), 0);
        Assert.False(result.Fired);
    }

    [Fact]
    public void Fires_WhenRenewablesAboveThreshold()
    {
        var result = AlertEvaluationService.Evaluate(MakeRule(renewablesAbove: 50), MakeReading(renewables: 65), 0);
        Assert.True(result.Fired);
    }

    [Fact]
    public void DoesNotFire_WhenRenewablesBelowThreshold()
    {
        var result = AlertEvaluationService.Evaluate(MakeRule(renewablesAbove: 70), MakeReading(renewables: 60), 0);
        Assert.False(result.Fired);
    }

    [Fact]
    public void Suppressed_WhenDailyCapReached()
    {
        var rule = MakeRule(co2Below: 300, maxPerDay: 2);
        var result = AlertEvaluationService.Evaluate(rule, MakeReading(co2: 200), deliveriesTodayCount: 2);
        Assert.False(result.Fired);
        Assert.Equal("daily_cap_reached", result.SuppressReason);
    }

    [Fact]
    public void AllConditions_MustPass()
    {
        // co2 OK but renewables not met
        var rule = MakeRule(co2Below: 300, renewablesAbove: 80);
        var result = AlertEvaluationService.Evaluate(rule, MakeReading(co2: 200, renewables: 60), 0);
        Assert.False(result.Fired);
    }

    [Fact]
    public void Delivery_ContainsTriggerValues()
    {
        var result = AlertEvaluationService.Evaluate(MakeRule(co2Below: 300), MakeReading(co2: 200, renewables: 65), 0);
        Assert.True(result.Fired);
        Assert.Equal(200, result.Delivery!.TriggerCo2GPerKwh);
        Assert.Equal(65, result.Delivery.TriggerRenewablesPercent);
    }

    [Fact]
    public void Delivery_MessageContainsCo2Value()
    {
        var result = AlertEvaluationService.Evaluate(MakeRule(co2Below: 300), MakeReading(co2: 200), 0);
        Assert.Contains("200", result.Delivery!.Message);
    }
}
