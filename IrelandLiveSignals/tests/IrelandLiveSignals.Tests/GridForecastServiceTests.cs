using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Core.Services;
using Xunit;

namespace IrelandLiveSignals.Tests;

public class GridForecastServiceTests
{
    private static GridReading MakeReading(DateTimeOffset timestamp, double co2 = 250, double renewables = 50)
    {
        var (score, status, rec) = GreenScoringService.Compute(renewables, co2, 30);
        return new GridReading
        {
            Id = $"r_{timestamp:HHmmss}",
            Region = "ROI",
            TimestampUtc = timestamp,
            Co2IntensityGPerKwh = co2,
            RenewablesPercent = renewables,
            GreenScore = score,
            GreenStatus = status,
            Recommendation = rec,
            SystemDemandMw = 4000,
            WindGenerationMw = 1000,
            DataFreshnessSeconds = 30,
            QualityStatus = "ok"
        };
    }

    [Fact]
    public void ReturnsEmpty_WhenNoHistory()
    {
        var result = GridForecastService.Forecast(new List<GridReading>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2));
        Assert.Empty(result);
    }

    [Fact]
    public void ProducesSlots_ForDeadline()
    {
        var now = DateTimeOffset.UtcNow;
        var history = Enumerable.Range(0, 48)
            .Select(i => MakeReading(now.AddMinutes(-30 * i)))
            .ToList();

        var slots = GridForecastService.Forecast(history, now, now.AddHours(4));
        Assert.NotEmpty(slots);
        Assert.True(slots.Count >= 7, $"Expected at least 7 half-hour slots in 4 hours, got {slots.Count}");
    }

    [Fact]
    public void ActualReading_MarkedAsActual()
    {
        var now = DateTimeOffset.UtcNow;
        // Place a reading exactly at the first slot time (30 min from now, rounded)
        var slotTime = now.AddMinutes(30);
        var history = new List<GridReading> { MakeReading(slotTime) };

        var slots = GridForecastService.Forecast(history, now, now.AddHours(1));
        var actualSlot = slots.FirstOrDefault(s => s.IsActual);
        Assert.NotNull(actualSlot);
    }

    [Fact]
    public void ForecastSlots_ConfidenceDegradesWithLookahead()
    {
        var now = DateTimeOffset.UtcNow;
        var history = Enumerable.Range(0, 7 * 48)
            .Select(i => MakeReading(now.AddMinutes(-30 * i)))
            .ToList();

        var slots = GridForecastService.Forecast(history, now, now.AddHours(24));
        var nearSlot = slots.FirstOrDefault(s => !s.IsActual && (s.SlotUtc - now).TotalHours < 2);
        var farSlot  = slots.LastOrDefault(s => !s.IsActual);

        if (nearSlot is not null && farSlot is not null && nearSlot != farSlot)
            Assert.True(nearSlot.Confidence >= farSlot.Confidence,
                $"Near confidence {nearSlot.Confidence} should be >= far confidence {farSlot.Confidence}");
    }
}
