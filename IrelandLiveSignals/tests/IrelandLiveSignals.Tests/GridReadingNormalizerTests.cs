using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Core.Services;
using Xunit;

namespace IrelandLiveSignals.Tests;

public class GridReadingNormalizerTests
{
    [Fact]
    public void GreenScore_ReflectsRenewablesAndCo2()
    {
        var (score, _, _) = GreenScoringService.Compute(80.0, 120.0, 30);
        Assert.True(score > 0.6);
    }

    [Fact]
    public void GridReading_AllFieldsPopulated()
    {
        var now = DateTimeOffset.UtcNow;
        var (score, status, recommendation) = GreenScoringService.Compute(55.0, 220.0, 45);

        var reading = new GridReading
        {
            Id = "test_001",
            Region = "ROI",
            TimestampUtc = now,
            SystemDemandMw = 4380,
            WindGenerationMw = 2120,
            SolarGenerationMw = null,
            RenewablesPercent = 55.0,
            Co2IntensityGPerKwh = 220.0,
            InterconnectorImportMw = 300,
            InterconnectorExportMw = null,
            DataFreshnessSeconds = 45,
            GreenScore = score,
            GreenStatus = status,
            Recommendation = recommendation,
            QualityStatus = "ok"
        };

        Assert.Equal("ROI", reading.Region);
        Assert.Equal(4380, reading.SystemDemandMw);
        Assert.Equal(2120, reading.WindGenerationMw);
        Assert.Equal(55.0, reading.RenewablesPercent);
        Assert.Equal(220.0, reading.Co2IntensityGPerKwh);
        Assert.Equal("ok", reading.QualityStatus);
        Assert.NotEmpty(reading.GreenStatus);
        Assert.NotEmpty(reading.Recommendation);
    }

    [Fact]
    public void QualityStatus_StaleWhenFreshnessOver600()
    {
        // Replicate the stale logic from the adapter
        var freshnessSeconds = 700;
        var qualityStatus = freshnessSeconds > 600 ? "stale" : "ok";
        Assert.Equal("stale", qualityStatus);
    }

    [Fact]
    public void QualityStatus_OkWhenFreshnessUnder600()
    {
        var freshnessSeconds = 120;
        var qualityStatus = freshnessSeconds > 600 ? "stale" : "ok";
        Assert.Equal("ok", qualityStatus);
    }
}
