using IrelandLiveSignals.Core.Services;
using Xunit;

namespace IrelandLiveSignals.Tests;

public class GreenScoringServiceTests
{
    [Fact]
    public void AllRenewables_LowCo2_ScoreNearOne()
    {
        var (score, status, _) = GreenScoringService.Compute(100.0, 50.0, 0);
        Assert.True(score > 0.85, $"Expected score > 0.85, got {score}");
        Assert.Equal("good", status);
    }

    [Fact]
    public void AllFossil_HighCo2_ScoreNearZero()
    {
        var (score, status, _) = GreenScoringService.Compute(0.0, 600.0, 300);
        Assert.True(score < 0.15, $"Expected score < 0.15, got {score}");
        Assert.Equal("poor", status);
    }

    [Fact]
    public void StaleData_FreshnessSecords400_PenaltyApplied()
    {
        var (scoreFresh, _, _) = GreenScoringService.Compute(50.0, 300.0, 0);
        var (scoreStale, _, _) = GreenScoringService.Compute(50.0, 300.0, 400);
        // stale score should be 0.10 * (1 - clamp(400/300, 0, 1)) = 0.10 * 0 = 0
        // fresh score has 0.10 * 1
        Assert.True(scoreStale < scoreFresh, "Stale data should produce lower score");
        var expectedFreshnessDelta = 0.10 * 1.0;
        Assert.True(Math.Abs((scoreFresh - scoreStale) - expectedFreshnessDelta) < 0.001);
    }

    [Theory]
    [InlineData(70.0, 150.0, 10, "good")]
    [InlineData(40.0, 350.0, 60, "moderate")]
    [InlineData(5.0,  580.0, 280, "poor")]
    public void StatusThresholds_CorrectLabel(double renewables, double co2, int freshness, string expectedStatus)
    {
        var (_, status, _) = GreenScoringService.Compute(renewables, co2, freshness);
        Assert.Equal(expectedStatus, status);
    }

    [Fact]
    public void GoodStatus_RecommendationTextIsCorrect()
    {
        var (_, _, recommendation) = GreenScoringService.Compute(100.0, 50.0, 0);
        Assert.Equal("Good time for flexible electricity use.", recommendation);
    }

    [Fact]
    public void ModerateStatus_RecommendationTextIsCorrect()
    {
        var (_, _, recommendation) = GreenScoringService.Compute(30.0, 350.0, 60);
        Assert.Equal("Grid conditions are average. Non-urgent loads can wait.", recommendation);
    }

    [Fact]
    public void PoorStatus_RecommendationTextIsCorrect()
    {
        var (_, _, recommendation) = GreenScoringService.Compute(0.0, 600.0, 300);
        Assert.Equal("Grid is carbon-heavy. Defer flexible loads if possible.", recommendation);
    }

    [Fact]
    public void Score_ClampedBetween0And1()
    {
        var (score1, _, _) = GreenScoringService.Compute(200.0, -100.0, -999);
        var (score2, _, _) = GreenScoringService.Compute(-100.0, 9999.0, 99999);
        Assert.InRange(score1, 0.0, 1.0);
        Assert.InRange(score2, 0.0, 1.0);
    }
}
