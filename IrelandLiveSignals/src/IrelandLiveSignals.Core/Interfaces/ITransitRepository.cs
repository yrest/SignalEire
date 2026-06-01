using IrelandLiveSignals.Core.Models;

namespace IrelandLiveSignals.Core.Interfaces;

public interface ITransitRepository
{
    // Stops
    Task<IReadOnlyList<TransitStop>> SearchStopsAsync(string query, int limit = 20, CancellationToken ct = default);
    Task<IReadOnlyList<TransitStop>> GetNearbyStopsAsync(double lat, double lon, int radiusMeters, int limit = 20, CancellationToken ct = default);
    Task<TransitStop?> GetStopAsync(string stopId, CancellationToken ct = default);

    // Schedule
    Task<IReadOnlyList<(StopTime St, TransitTrip Trip, TransitRoute Route)>> GetScheduledArrivalsAsync(
        string stopId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    // Vehicle positions
    Task UpsertVehicleObservationAsync(VehicleObservation obs, CancellationToken ct = default);
    Task<VehicleObservation?> GetVehicleForTripAsync(string tripId, CancellationToken ct = default);
    Task<IReadOnlyList<VehicleObservation>> GetVehiclesForRouteAsync(string routeId, CancellationToken ct = default);

    // Trip updates (delay seconds per stop)
    Task SaveTripDelaysAsync(string tripId, IReadOnlyList<(string StopId, int DelaySeconds)> delays, CancellationToken ct = default);
    Task<int?> GetDelaySecondsAsync(string tripId, string stopId, CancellationToken ct = default);

    // Service alerts
    Task SaveServiceAlertsAsync(IReadOnlyList<ServiceAlertRecord> alerts, CancellationToken ct = default);
    Task<IReadOnlyList<ServiceAlertRecord>> GetActiveAlertsAsync(CancellationToken ct = default);
    Task<ServiceAlertRecord?> GetAlertForRouteAsync(string routeId, CancellationToken ct = default);

    // GTFS static state
    Task<DateTimeOffset?> GetLastStaticImportAsync(CancellationToken ct = default);
    Task SetLastStaticImportAsync(DateTimeOffset importedAt, CancellationToken ct = default);
    Task<bool> HasStaticDataAsync(CancellationToken ct = default);
}
