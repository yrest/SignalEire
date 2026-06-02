using System.Diagnostics.Metrics;

namespace IrelandLiveSignals.Core.Services;

public sealed class SignalEireMetrics : IDisposable
{
    private readonly Meter _gridMeter    = new("IrelandLiveSignals.Grid", "1.0");
    private readonly Meter _transitMeter = new("IrelandLiveSignals.Transit", "1.0");
    private readonly Meter _alertsMeter  = new("IrelandLiveSignals.Alerts", "1.0");
    private readonly Meter _feedsMeter   = new("IrelandLiveSignals.Feeds", "1.0");

    public Counter<long>     GridReadingsIngested { get; }
    public Histogram<double> GridCo2Intensity     { get; }
    public Histogram<double> GridGreenScore       { get; }
    public ObservableGauge<double> GridCo2Latest  { get; }

    public Counter<long>     VehicleObservationsIngested { get; }
    public Counter<long>     TripMatchesTotal             { get; }
    public Histogram<double> ArrivalConfidence            { get; }
    public ObservableGauge<double> ActiveVehicleCount     { get; }

    public Counter<long>     AlertsFired      { get; }
    public Counter<long>     AlertsDelivered  { get; }
    public Counter<long>     AlertsSuppressed { get; }

    public Histogram<double>       FeedFetchDurationMs  { get; }
    public Counter<long>           FeedFetchFailures    { get; }
    public ObservableGauge<double> FeedStalenessSeconds { get; }

    public SignalEireMetrics(LiveSignalState state, FeedHealthStore feedHealth)
    {
        // Grid
        GridReadingsIngested = _gridMeter.CreateCounter<long>(
            "signaleire_grid_readings_ingested_total",
            description: "Total grid readings ingested");
        GridCo2Intensity = _gridMeter.CreateHistogram<double>(
            "signaleire_grid_co2_intensity_grams_per_kwh",
            unit: "g/kWh",
            description: "Grid CO₂ intensity");
        GridGreenScore = _gridMeter.CreateHistogram<double>(
            "signaleire_grid_green_score",
            description: "Grid green score 0-1");
        GridCo2Latest = _gridMeter.CreateObservableGauge<double>(
            "signaleire_grid_co2_latest_grams_per_kwh",
            () => state.LatestCo2GPerKwh,
            unit: "g/kWh",
            description: "Latest CO₂ intensity from LiveSignalState");

        // Transit
        VehicleObservationsIngested = _transitMeter.CreateCounter<long>(
            "signaleire_transit_vehicle_observations_ingested_total",
            description: "Total vehicle observations ingested");
        TripMatchesTotal = _transitMeter.CreateCounter<long>(
            "signaleire_transit_trip_matches_total",
            description: "Total trip matches");
        ArrivalConfidence = _transitMeter.CreateHistogram<double>(
            "signaleire_transit_arrival_confidence",
            description: "Arrival confidence score 0-1");
        ActiveVehicleCount = _transitMeter.CreateObservableGauge<double>(
            "signaleire_transit_active_vehicle_count",
            () => (double)state.ActiveVehicleCount,
            description: "Number of active vehicles from LiveSignalState");

        // Alerts
        AlertsFired = _alertsMeter.CreateCounter<long>(
            "signaleire_alerts_fired_total",
            description: "Total alerts fired");
        AlertsDelivered = _alertsMeter.CreateCounter<long>(
            "signaleire_alerts_delivered_total",
            description: "Total alerts delivered");
        AlertsSuppressed = _alertsMeter.CreateCounter<long>(
            "signaleire_alerts_suppressed_total",
            description: "Total alerts suppressed");

        // Feeds
        FeedFetchDurationMs = _feedsMeter.CreateHistogram<double>(
            "signaleire_feed_fetch_duration_ms",
            unit: "ms",
            description: "Feed fetch duration in milliseconds");
        FeedFetchFailures = _feedsMeter.CreateCounter<long>(
            "signaleire_feed_fetch_failures_total",
            description: "Total feed fetch failures");
        FeedStalenessSeconds = _feedsMeter.CreateObservableGauge<double>(
            "signaleire_feed_staleness_seconds",
            () => GetMaxStalenessSeconds(feedHealth),
            unit: "s",
            description: "Maximum staleness across all feeds in seconds");
    }

    private static double GetMaxStalenessSeconds(FeedHealthStore feedHealth)
    {
        var entries = feedHealth.GetAll();
        if (entries.Count == 0) return 0;
        var now = DateTimeOffset.UtcNow;
        return entries
            .Where(e => e.LastSuccessUtc != default)
            .Select(e => (now - e.LastSuccessUtc).TotalSeconds)
            .DefaultIfEmpty(0)
            .Max();
    }

    public void Dispose()
    {
        _gridMeter.Dispose();
        _transitMeter.Dispose();
        _alertsMeter.Dispose();
        _feedsMeter.Dispose();
    }
}
