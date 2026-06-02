namespace IrelandLiveSignals.Core.Services;

public static class CarbonSavingsEstimator
{
    /// <summary>
    /// Returns estimated CO₂ saved in kg by deferring consumption from current intensity
    /// to the recommended window intensity.
    /// </summary>
    public static double EstimateSavingKg(double kWh, double currentIntensityGPerKwh, double windowIntensityGPerKwh)
    {
        var savingGrams = kWh * (currentIntensityGPerKwh - windowIntensityGPerKwh);
        return Math.Max(0, savingGrams / 1000.0);
    }
}
