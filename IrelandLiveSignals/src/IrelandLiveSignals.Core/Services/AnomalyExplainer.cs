using IrelandLiveSignals.Core.Models;

namespace IrelandLiveSignals.Core.Services;

public static class AnomalyExplainer
{
    public static string Explain(SignalAnomaly anomaly) => anomaly.AnomalyType switch
    {
        "high_co2_day" => $"Grid carbon intensity on {anomaly.Date:dd MMM} averaged " +
            $"{anomaly.ObservedValue:F0} g/kWh — {anomaly.DeviationZScore:F1} standard deviations " +
            $"above the recent baseline of {anomaly.BaselineValue:F0} g/kWh. " +
            $"This suggests heavier reliance on fossil generation than usual.",

        "low_co2_day" => $"Grid carbon intensity on {anomaly.Date:dd MMM} averaged " +
            $"{anomaly.ObservedValue:F0} g/kWh — unusually clean, " +
            $"{anomaly.DeviationZScore:F1} standard deviations below the recent baseline. " +
            $"High renewable output likely contributed.",

        "low_renewables_day" => $"Renewable share on {anomaly.Date:dd MMM} averaged " +
            $"{anomaly.ObservedValue:P0} — {anomaly.DeviationZScore:F1} standard deviations " +
            $"below the recent baseline of {anomaly.BaselineValue:P0}.",

        "route_reliability_drop" => $"Route {anomaly.RouteId} had a vehicle-confirmed arrival " +
            $"rate of {anomaly.ObservedValue:P0} on {anomaly.Date:dd MMM}, compared to a " +
            $"recent average of {anomaly.BaselineValue:P0}. " +
            $"This may indicate GPS feed gaps, operator issues, or service disruption.",

        "stop_ghost_spike" => $"Stop {anomaly.StopId} recorded {anomaly.ObservedValue:F0} " +
            $"unconfirmed arrivals on {anomaly.Date:dd MMM}, significantly above the typical " +
            $"{anomaly.BaselineValue:F1} for this day of the week.",

        "feed_gap" => $"The {anomaly.RouteId ?? anomaly.Module} feed had a data gap of " +
            $"more than 30 minutes on {anomaly.Date:dd MMM}. Predictions during this period " +
            $"may have lower confidence.",

        _ => $"An anomaly of type '{anomaly.AnomalyType}' was detected on {anomaly.Date:dd MMM}."
    };
}
