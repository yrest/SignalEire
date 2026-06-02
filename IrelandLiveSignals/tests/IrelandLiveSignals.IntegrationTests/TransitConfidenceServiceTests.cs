using IrelandLiveSignals.Core.Services;
using Xunit;

namespace IrelandLiveSignals.IntegrationTests;

public class TransitConfidenceServiceTests
{
    [Fact]
    public void VehicleConfirmed_FreshGps_TripMatched_ReturnsHighScore()
    {
        var result = TransitConfidenceService.Compute(new ConfidenceInput
        {
            Presence = VehiclePresence.VehicleConfirmed,
            GpsAgeSeconds = 20,
            TripIdMatched = true,
            RouteMatched = true,
            DistanceToStopMeters = 500,
            HasServiceAlert = false
        });

        Assert.True(result.Score > 0.65, $"Expected score > 0.65, got {result.Score}");
        Assert.Equal("low", result.GhostRisk);
        Assert.Equal("Vehicle confirmed", result.StatusLabel);
    }

    [Fact]
    public void Cancelled_ReturnsZeroScore()
    {
        var result = TransitConfidenceService.Compute(new ConfidenceInput
        {
            Presence = VehiclePresence.Cancelled,
            HasServiceAlert = true,
            AlertEffect = "NO_SERVICE"
        });

        Assert.Equal(0.0, result.Score);
        Assert.Equal("high", result.GhostRisk);
        Assert.Equal("Cancelled", result.StatusLabel);
    }

    [Fact]
    public void TimetableOnly_NoAlert_ReturnsMediumOrLowerScore()
    {
        var result = TransitConfidenceService.Compute(new ConfidenceInput
        {
            Presence = VehiclePresence.TimetableOnly,
            HasServiceAlert = false
        });

        Assert.True(result.Score < 0.65, $"Expected score < 0.65, got {result.Score}");
        Assert.Equal("Timetable only", result.StatusLabel);
    }

    [Fact]
    public void StaleGps_IsLabelled()
    {
        var result = TransitConfidenceService.Compute(new ConfidenceInput
        {
            Presence = VehiclePresence.VehicleConfirmed,
            GpsAgeSeconds = 400,
            TripIdMatched = true,
            RouteMatched = true,
            HasServiceAlert = false
        });

        Assert.Equal("Stale GPS", result.StatusLabel);
    }

    [Fact]
    public void SignificantDelayAlert_ReducesScore()
    {
        var noAlert = TransitConfidenceService.Compute(new ConfidenceInput
        {
            Presence = VehiclePresence.VehicleConfirmed,
            GpsAgeSeconds = 30,
            TripIdMatched = true,
            RouteMatched = true,
            HasServiceAlert = false
        });

        var withAlert = TransitConfidenceService.Compute(new ConfidenceInput
        {
            Presence = VehiclePresence.VehicleConfirmed,
            GpsAgeSeconds = 30,
            TripIdMatched = true,
            RouteMatched = true,
            HasServiceAlert = true,
            AlertEffect = "SIGNIFICANT_DELAYS"
        });

        Assert.True(withAlert.Score < noAlert.Score);
    }
}
