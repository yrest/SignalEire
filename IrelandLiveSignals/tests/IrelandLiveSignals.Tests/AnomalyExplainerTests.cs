using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Core.Services;
using Xunit;

namespace IrelandLiveSignals.Tests;

public class AnomalyExplainerTests
{
    private static SignalAnomaly MakeAnomaly(string type, double observed = 350, double baseline = 250,
        double zScore = 2.5, string? routeId = null, string? stopId = null) => new()
    {
        Id = "test",
        Module = type.StartsWith("route") || type.StartsWith("stop") || type == "feed_gap" ? "transit" : "grid",
        AnomalyType = type,
        Region = "ROI",
        RouteId = routeId,
        StopId = stopId,
        Date = new DateOnly(2026, 6, 1),
        ObservedValue = observed,
        BaselineValue = baseline,
        DeviationZScore = zScore,
        ExplanationText = "",
        IndexedToQdrant = false,
        DetectedAtUtc = DateTimeOffset.UtcNow
    };

    [Theory]
    [InlineData("high_co2_day")]
    [InlineData("low_co2_day")]
    [InlineData("low_renewables_day")]
    [InlineData("route_reliability_drop")]
    [InlineData("stop_ghost_spike")]
    [InlineData("feed_gap")]
    [InlineData("unknown_type")]
    public void Explain_ReturnsNonEmptyString(string anomalyType)
    {
        var anomaly = MakeAnomaly(anomalyType, routeId: "R1", stopId: "S1");
        var explanation = AnomalyExplainer.Explain(anomaly);
        Assert.NotNull(explanation);
        Assert.NotEmpty(explanation);
    }

    [Fact]
    public void Explain_HighCo2_ReferencesObservedAndBaseline()
    {
        var anomaly = MakeAnomaly("high_co2_day", observed: 480, baseline: 300, zScore: 2.8);
        var explanation = AnomalyExplainer.Explain(anomaly);
        Assert.Contains("480", explanation);
        Assert.Contains("300", explanation);
    }

    [Fact]
    public void Explain_LowCo2_ReferencesObservedAndBaseline()
    {
        var anomaly = MakeAnomaly("low_co2_day", observed: 100, baseline: 300, zScore: 2.1);
        var explanation = AnomalyExplainer.Explain(anomaly);
        Assert.Contains("100", explanation);
    }

    [Fact]
    public void Explain_LowRenewables_ReferencesPercentages()
    {
        var anomaly = MakeAnomaly("low_renewables_day", observed: 0.2, baseline: 0.5, zScore: 2.3);
        var explanation = AnomalyExplainer.Explain(anomaly);
        // ":P0" format produces "20%" or "20 %" depending on locale — check for "20"
        Assert.Contains("20", explanation);
        Assert.Contains("50", explanation);
    }

    [Fact]
    public void Explain_RouteReliabilityDrop_ReferencesRouteId()
    {
        var anomaly = MakeAnomaly("route_reliability_drop", observed: 0.6, baseline: 0.8, routeId: "Route123");
        var explanation = AnomalyExplainer.Explain(anomaly);
        Assert.Contains("Route123", explanation);
    }

    [Fact]
    public void Explain_StopGhostSpike_ReferencesStopId()
    {
        var anomaly = MakeAnomaly("stop_ghost_spike", observed: 45, baseline: 10, stopId: "Stop456");
        var explanation = AnomalyExplainer.Explain(anomaly);
        Assert.Contains("Stop456", explanation);
    }

    [Fact]
    public void Explain_EdgeValues_DoNotThrow()
    {
        var types = new[] { "high_co2_day", "low_co2_day", "low_renewables_day" };
        foreach (var type in types)
        {
            var anomaly = MakeAnomaly(type, observed: 0, baseline: 0, zScore: 0);
            // Should not throw
            var explanation = AnomalyExplainer.Explain(anomaly);
            Assert.NotEmpty(explanation);
        }
    }

    [Fact]
    public void Explain_PercentageEdgeValues_DoNotThrow()
    {
        var anomaly = MakeAnomaly("low_renewables_day", observed: 1.0, baseline: 1.0, zScore: 0);
        var explanation = AnomalyExplainer.Explain(anomaly);
        Assert.NotEmpty(explanation);
    }

    [Fact]
    public void Explain_CountZero_DoesNotThrow()
    {
        var anomaly = MakeAnomaly("stop_ghost_spike", observed: 0, baseline: 0, stopId: "S1");
        var explanation = AnomalyExplainer.Explain(anomaly);
        Assert.NotEmpty(explanation);
    }
}
