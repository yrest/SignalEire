using IrelandLiveSignals.Core.Services;
using Xunit;

namespace IrelandLiveSignals.Tests;

public class CarbonSavingsEstimatorTests
{
    [Fact]
    public void ReturnsCorrectSaving()
    {
        // 30 kWh × (320 - 190) g/kWh = 3900 g = 3.9 kg
        var saving = CarbonSavingsEstimator.EstimateSavingKg(30, 320, 190);
        Assert.Equal(3.9, saving, precision: 3);
    }

    [Fact]
    public void ReturnsZero_WhenWindowIsWorse()
    {
        var saving = CarbonSavingsEstimator.EstimateSavingKg(30, 190, 320);
        Assert.Equal(0, saving);
    }

    [Fact]
    public void ReturnsZero_WhenIntensitiesAreEqual()
    {
        var saving = CarbonSavingsEstimator.EstimateSavingKg(30, 250, 250);
        Assert.Equal(0, saving);
    }

    [Theory]
    [InlineData(10, 400, 200, 2.0)]
    [InlineData(7.4, 300, 150, 1.11)]
    public void Correct_ForVariousInputs(double kwh, double current, double window, double expectedKg)
    {
        var saving = CarbonSavingsEstimator.EstimateSavingKg(kwh, current, window);
        Assert.Equal(expectedKg, saving, precision: 2);
    }
}
