using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace IrelandLiveSignals.Infrastructure.Persistence;

public class TransitRepository : ITransitRepository
{
    private readonly GridDbContext _db;

    public TransitRepository(GridDbContext db) => _db = db;

    public async Task<IReadOnlyList<TransitStop>> SearchStopsAsync(string query, int limit = 20, CancellationToken ct = default) =>
        await _db.TransitStops
            .Where(s => EF.Functions.Like(s.StopName, $"%{query}%") ||
                        EF.Functions.Like(s.StopCode, $"%{query}%"))
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TransitStop>> GetNearbyStopsAsync(double lat, double lon, int radiusMeters, int limit = 20, CancellationToken ct = default)
    {
        // Bounding box approximation — 1 degree lat ≈ 111,320 m
        var latDelta = radiusMeters / 111_320.0;
        var lonDelta = radiusMeters / (111_320.0 * Math.Cos(lat * Math.PI / 180.0));

        var candidates = await _db.TransitStops
            .Where(s => s.StopLat >= lat - latDelta && s.StopLat <= lat + latDelta &&
                        s.StopLon >= lon - lonDelta && s.StopLon <= lon + lonDelta)
            .ToListAsync(ct);

        return candidates
            .Select(s => (Stop: s, Dist: HaversineMeters(lat, lon, s.StopLat, s.StopLon)))
            .Where(x => x.Dist <= radiusMeters)
            .OrderBy(x => x.Dist)
            .Take(limit)
            .Select(x => x.Stop)
            .ToList();
    }

    public async Task<TransitStop?> GetStopAsync(string stopId, CancellationToken ct = default) =>
        await _db.TransitStops.FindAsync(new object[] { stopId }, ct);

    public async Task<IReadOnlyList<(StopTime St, TransitTrip Trip, TransitRoute Route)>> GetScheduledArrivalsAsync(
        string stopId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(from.UtcDateTime);
        var todayDow = (int)today.DayOfWeek; // 0=Sun, 1=Mon...

        // Active service IDs for today
        var activeServiceIds = await GetActiveServiceIdsAsync(today, ct);

        // Window in seconds from midnight
        var fromSeconds = (int)from.TimeOfDay.TotalSeconds;
        var toSeconds = (int)to.TimeOfDay.TotalSeconds;
        // Handle window crossing midnight
        if (toSeconds < fromSeconds) toSeconds += 86400;

        var results = await (
            from st in _db.StopTimes
            join trip in _db.TransitTrips on st.TripId equals trip.TripId
            join route in _db.TransitRoutes on trip.RouteId equals route.RouteId
            where st.StopId == stopId
               && activeServiceIds.Contains(trip.ServiceId)
               && st.ArrivalSeconds >= fromSeconds
               && st.ArrivalSeconds <= toSeconds
            orderby st.ArrivalSeconds
            select new { St = st, Trip = trip, Route = route }
        ).Take(50).ToListAsync(ct);

        return results.Select(r => (r.St, r.Trip, r.Route)).ToList();
    }

    private async Task<HashSet<string>> GetActiveServiceIdsAsync(DateOnly date, CancellationToken ct)
    {
        var dow = date.DayOfWeek;

        // Services active by regular calendar
        var regular = await _db.ServiceCalendars
            .Where(c => c.StartDate <= date && c.EndDate >= date)
            .ToListAsync(ct);

        var active = regular
            .Where(c => dow switch
            {
                DayOfWeek.Monday => c.Monday,
                DayOfWeek.Tuesday => c.Tuesday,
                DayOfWeek.Wednesday => c.Wednesday,
                DayOfWeek.Thursday => c.Thursday,
                DayOfWeek.Friday => c.Friday,
                DayOfWeek.Saturday => c.Saturday,
                DayOfWeek.Sunday => c.Sunday,
                _ => false
            })
            .Select(c => c.ServiceId)
            .ToHashSet();

        // Apply calendar_dates exceptions
        var exceptions = await _db.ServiceCalendarDates
            .Where(cd => cd.Date == date)
            .ToListAsync(ct);

        foreach (var ex in exceptions)
        {
            if (ex.ExceptionType == 1) active.Add(ex.ServiceId);      // added
            else if (ex.ExceptionType == 2) active.Remove(ex.ServiceId); // removed
        }

        return active;
    }

    public async Task UpsertVehicleObservationAsync(VehicleObservation obs, CancellationToken ct = default)
    {
        var existing = await _db.VehicleObservations.FindAsync(new object[] { obs.VehicleId }, ct);
        if (existing is null)
            _db.VehicleObservations.Add(obs);
        else
            _db.Entry(existing).CurrentValues.SetValues(obs);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<VehicleObservation?> GetVehicleForTripAsync(string tripId, CancellationToken ct = default) =>
        await _db.VehicleObservations.FirstOrDefaultAsync(v => v.TripId == tripId, ct);

    public async Task<IReadOnlyList<VehicleObservation>> GetVehiclesForRouteAsync(string routeId, CancellationToken ct = default) =>
        await _db.VehicleObservations.Where(v => v.RouteId == routeId).ToListAsync(ct);

    public async Task AppendTrailPointAsync(VehicleTrailPoint point, CancellationToken ct = default)
    {
        _db.VehicleTrailPoints.Add(point);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<VehicleTrailPoint>> GetVehicleTrailAsync(string vehicleId, DateTimeOffset since, CancellationToken ct = default) =>
        await _db.VehicleTrailPoints
            .Where(p => p.VehicleId == vehicleId && p.ObservedAtUtc >= since)
            .OrderBy(p => p.ObservedAtUtc)
            .ToListAsync(ct);

    public async Task PruneTrailPointsAsync(DateTimeOffset olderThan, CancellationToken ct = default)
    {
        await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM [VehicleTrailPoints] WHERE [ObservedAtUtc] < {0}", olderThan);
    }

    // Trip delays stored as a simple in-memory cache per poll cycle — not persisted
    private readonly Dictionary<(string TripId, string StopId), int> _tripDelays = new();

    public Task SaveTripDelaysAsync(string tripId, IReadOnlyList<(string StopId, int DelaySeconds)> delays, CancellationToken ct = default)
    {
        foreach (var (stopId, delay) in delays)
            _tripDelays[(tripId, stopId)] = delay;
        return Task.CompletedTask;
    }

    public Task<int?> GetDelaySecondsAsync(string tripId, string stopId, CancellationToken ct = default)
    {
        int? result = _tripDelays.TryGetValue((tripId, stopId), out var d) ? d : null;
        return Task.FromResult(result);
    }

    public async Task SaveServiceAlertsAsync(IReadOnlyList<ServiceAlertRecord> alerts, CancellationToken ct = default)
    {
        // Replace all current alerts
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM [ServiceAlerts]", ct);
        _db.ServiceAlerts.AddRange(alerts);
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyList<ServiceAlertRecord>> GetActiveAlertsAsync(CancellationToken ct = default) =>
        await _db.ServiceAlerts.ToListAsync(ct);

    public async Task<ServiceAlertRecord?> GetAlertForRouteAsync(string routeId, CancellationToken ct = default)
    {
        var alerts = await _db.ServiceAlerts.ToListAsync(ct);
        return alerts.FirstOrDefault(a => a.AffectedRouteIds.Contains(routeId));
    }

    public async Task<DateTimeOffset?> GetLastStaticImportAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _db.Database
                .SqlQueryRaw<DateTimeOffset?>("SELECT TOP 1 [LastImportUtc] FROM [__GtfsImportMeta]")
                .FirstOrDefaultAsync(ct);
            return result;
        }
        catch { return null; }
    }

    public async Task SetLastStaticImportAsync(DateTimeOffset importedAt, CancellationToken ct = default)
    {
        await _db.Database.ExecuteSqlRawAsync(
            "IF NOT EXISTS (SELECT 1 FROM [__GtfsImportMeta]) INSERT INTO [__GtfsImportMeta] ([Id],[LastImportUtc]) VALUES (1,{0}) ELSE UPDATE [__GtfsImportMeta] SET [LastImportUtc]={0} WHERE [Id]=1",
            importedAt);
    }

    public async Task<bool> HasStaticDataAsync(CancellationToken ct = default) =>
        await _db.TransitStops.AnyAsync(ct);

    public async Task SaveUserReportAsync(TransitUserReport report, CancellationToken ct = default)
    {
        _db.TransitUserReports.Add(report);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TransitUserReport>> GetUserReportsAsync(string? routeId, string? stopId, DateTimeOffset since, CancellationToken ct = default)
    {
        var query = _db.TransitUserReports.Where(r => r.ReportedAtUtc >= since);
        if (routeId is not null) query = query.Where(r => r.RouteId == routeId);
        if (stopId is not null) query = query.Where(r => r.StopId == stopId);
        return await query.OrderByDescending(r => r.ReportedAtUtc).ToListAsync(ct);
    }

    public async Task<TransitReliabilityAggregate?> GetReliabilityAsync(string routeId, string stopId, CancellationToken ct = default) =>
        await _db.TransitReliabilityAggregates.FindAsync(new object[] { routeId, stopId }, ct);

    public async Task UpsertReliabilityAggregateAsync(TransitReliabilityAggregate agg, CancellationToken ct = default)
    {
        var existing = await _db.TransitReliabilityAggregates.FindAsync(new object[] { agg.RouteId, agg.StopId }, ct);
        if (existing is null)
            _db.TransitReliabilityAggregates.Add(agg);
        else
            _db.Entry(existing).CurrentValues.SetValues(agg);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TransitReliabilityAggregate>> GetTopReliableRoutesAsync(int limit = 10, CancellationToken ct = default) =>
        await _db.TransitReliabilityAggregates
            .Where(a => a.TotalObservations >= 5)
            .OrderByDescending(a => a.ReliabilityScore)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TransitReliabilityAggregate>> GetAllAggregatesAsync(CancellationToken ct = default) =>
        await _db.TransitReliabilityAggregates.ToListAsync(ct);

    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
