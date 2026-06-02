using System.Globalization;
using System.IO.Compression;
using CsvHelper;
using CsvHelper.Configuration;
using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IrelandLiveSignals.Infrastructure.Transit;

public class GtfsStaticImporter
{
    private readonly HttpClient _http;
    private readonly NtaTransitOptions _opts;
    private readonly IDbContextFactory<GridDbContext> _dbFactory;
    private readonly ILogger<GtfsStaticImporter> _logger;

    public GtfsStaticImporter(
        HttpClient http,
        IOptions<NtaTransitOptions> opts,
        IDbContextFactory<GridDbContext> dbFactory,
        ILogger<GtfsStaticImporter> logger)
    {
        _http = http;
        _opts = opts.Value;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task ImportAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Downloading GTFS static from {Url}", _opts.GtfsStaticUrl);
        var zipBytes = await _http.GetByteArrayAsync(_opts.GtfsStaticUrl, ct);
        _logger.LogInformation("Downloaded {Size:N0} bytes. Parsing...", zipBytes.Length);

        using var zipStream = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);

        // Clear existing static data
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await TruncateStaticTablesAsync(db, ct);

        await ImportStopsAsync(zip, db, ct);
        await ImportRoutesAsync(zip, db, ct);
        await ImportTripsAsync(zip, db, ct);
        await ImportCalendarAsync(zip, db, ct);
        await ImportCalendarDatesAsync(zip, db, ct);
        await ImportStopTimesAsync(zip, db, ct);

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE [__GtfsImportMeta] SET [LastImportUtc] = GETUTCDATE() WHERE [Id] = 1", ct);
        _logger.LogInformation("GTFS static import complete.");
    }

    private static async Task TruncateStaticTablesAsync(GridDbContext db, CancellationToken ct)
    {
        // Order matters due to potential FK constraints — truncate leaves in dependency order
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [StopTimes]", ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [ServiceCalendarDates]", ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [ServiceCalendars]", ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [TransitTrips]", ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [TransitRoutes]", ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [TransitStops]", ct);
    }

    private async Task ImportStopsAsync(ZipArchive zip, GridDbContext db, CancellationToken ct)
    {
        var entry = FindEntry(zip, "stops.txt");
        if (entry is null) { _logger.LogWarning("stops.txt not found in zip"); return; }

        using var reader = new StreamReader(entry.Open());
        using var csv = new CsvReader(reader, CsvConfig());

        var batch = new List<TransitStop>(500);
        await foreach (var row in csv.GetRecordsAsync<StopCsvRow>(ct))
        {
            batch.Add(new TransitStop
            {
                StopId = row.stop_id,
                StopCode = row.stop_code ?? string.Empty,
                StopName = row.stop_name,
                StopLat = ParseDouble(row.stop_lat),
                StopLon = ParseDouble(row.stop_lon)
            });
            if (batch.Count >= 500)
            {
                db.TransitStops.AddRange(batch);
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
                batch.Clear();
            }
        }
        if (batch.Count > 0) { db.TransitStops.AddRange(batch); await db.SaveChangesAsync(ct); db.ChangeTracker.Clear(); }
        _logger.LogInformation("Stops imported.");
    }

    private async Task ImportRoutesAsync(ZipArchive zip, GridDbContext db, CancellationToken ct)
    {
        var entry = FindEntry(zip, "routes.txt");
        if (entry is null) { _logger.LogWarning("routes.txt not found"); return; }

        using var reader = new StreamReader(entry.Open());
        using var csv = new CsvReader(reader, CsvConfig());

        var routes = new List<TransitRoute>();
        await foreach (var row in csv.GetRecordsAsync<RouteCsvRow>(ct))
        {
            routes.Add(new TransitRoute
            {
                RouteId = row.route_id,
                AgencyId = row.agency_id ?? string.Empty,
                RouteShortName = row.route_short_name ?? string.Empty,
                RouteLongName = row.route_long_name ?? string.Empty,
                RouteType = int.TryParse(row.route_type, out var rt) ? rt : 3
            });
        }
        db.TransitRoutes.AddRange(routes);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        _logger.LogInformation("Routes imported: {Count}", routes.Count);
    }

    private async Task ImportTripsAsync(ZipArchive zip, GridDbContext db, CancellationToken ct)
    {
        var entry = FindEntry(zip, "trips.txt");
        if (entry is null) { _logger.LogWarning("trips.txt not found"); return; }

        using var reader = new StreamReader(entry.Open());
        using var csv = new CsvReader(reader, CsvConfig());

        var batch = new List<TransitTrip>(2000);
        await foreach (var row in csv.GetRecordsAsync<TripCsvRow>(ct))
        {
            batch.Add(new TransitTrip
            {
                TripId = row.trip_id,
                RouteId = row.route_id,
                ServiceId = row.service_id,
                TripHeadsign = row.trip_headsign,
                DirectionId = int.TryParse(row.direction_id, out var d) ? d : 0
            });
            if (batch.Count >= 2000)
            {
                db.TransitTrips.AddRange(batch);
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
                batch.Clear();
            }
        }
        if (batch.Count > 0) { db.TransitTrips.AddRange(batch); await db.SaveChangesAsync(ct); db.ChangeTracker.Clear(); }
        _logger.LogInformation("Trips imported.");
    }

    private async Task ImportCalendarAsync(ZipArchive zip, GridDbContext db, CancellationToken ct)
    {
        var entry = FindEntry(zip, "calendar.txt");
        if (entry is null) return; // optional

        using var reader = new StreamReader(entry.Open());
        using var csv = new CsvReader(reader, CsvConfig());

        var records = new List<ServiceCalendar>();
        await foreach (var row in csv.GetRecordsAsync<CalendarCsvRow>(ct))
        {
            records.Add(new ServiceCalendar
            {
                ServiceId = row.service_id,
                Monday = row.monday == "1",
                Tuesday = row.tuesday == "1",
                Wednesday = row.wednesday == "1",
                Thursday = row.thursday == "1",
                Friday = row.friday == "1",
                Saturday = row.saturday == "1",
                Sunday = row.sunday == "1",
                StartDate = ParseGtfsDate(row.start_date),
                EndDate = ParseGtfsDate(row.end_date)
            });
        }
        db.ServiceCalendars.AddRange(records);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private async Task ImportCalendarDatesAsync(ZipArchive zip, GridDbContext db, CancellationToken ct)
    {
        var entry = FindEntry(zip, "calendar_dates.txt");
        if (entry is null) return;

        using var reader = new StreamReader(entry.Open());
        using var csv = new CsvReader(reader, CsvConfig());

        var batch = new List<ServiceCalendarDate>(500);
        await foreach (var row in csv.GetRecordsAsync<CalendarDateCsvRow>(ct))
        {
            batch.Add(new ServiceCalendarDate
            {
                ServiceId = row.service_id,
                Date = ParseGtfsDate(row.date),
                ExceptionType = int.TryParse(row.exception_type, out var et) ? et : 1
            });
            if (batch.Count >= 500) { db.ServiceCalendarDates.AddRange(batch); await db.SaveChangesAsync(ct); db.ChangeTracker.Clear(); batch.Clear(); }
        }
        if (batch.Count > 0) { db.ServiceCalendarDates.AddRange(batch); await db.SaveChangesAsync(ct); db.ChangeTracker.Clear(); }
    }

    private async Task ImportStopTimesAsync(ZipArchive zip, GridDbContext db, CancellationToken ct)
    {
        var entry = FindEntry(zip, "stop_times.txt");
        if (entry is null) { _logger.LogWarning("stop_times.txt not found"); return; }

        _logger.LogInformation("Importing stop_times (large file — using bulk copy)...");

        var connString = db.Database.GetConnectionString()!;
        using var conn = new SqlConnection(connString);
        await conn.OpenAsync(ct);

        // SqlBulkCopy in batches of 50,000
        using var bulk = new SqlBulkCopy(conn)
        {
            DestinationTableName = "StopTimes",
            BatchSize = 50_000,
            BulkCopyTimeout = 600
        };
        bulk.ColumnMappings.Add("TripId", "TripId");
        bulk.ColumnMappings.Add("StopId", "StopId");
        bulk.ColumnMappings.Add("StopSequence", "StopSequence");
        bulk.ColumnMappings.Add("ArrivalSeconds", "ArrivalSeconds");
        bulk.ColumnMappings.Add("DepartureSeconds", "DepartureSeconds");

        using var reader = new StreamReader(entry.Open());
        using var csv = new CsvReader(reader, CsvConfig());

        var dt = new System.Data.DataTable();
        dt.Columns.Add("TripId", typeof(string));
        dt.Columns.Add("StopId", typeof(string));
        dt.Columns.Add("StopSequence", typeof(int));
        dt.Columns.Add("ArrivalSeconds", typeof(int));
        dt.Columns.Add("DepartureSeconds", typeof(int));

        long total = 0;
        await foreach (var row in csv.GetRecordsAsync<StopTimeCsvRow>(ct))
        {
            dt.Rows.Add(row.trip_id, row.stop_id,
                int.TryParse(row.stop_sequence, out var seq) ? seq : 0,
                ParseGtfsTime(row.arrival_time),
                ParseGtfsTime(row.departure_time));
            total++;
            if (dt.Rows.Count >= 50_000)
            {
                await bulk.WriteToServerAsync(dt, ct);
                dt.Clear();
                _logger.LogDebug("stop_times bulk progress: {Total:N0}", total);
            }
        }
        if (dt.Rows.Count > 0) await bulk.WriteToServerAsync(dt, ct);
        _logger.LogInformation("stop_times imported: {Total:N0} rows", total);
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive zip, string name) =>
        zip.Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static CsvConfiguration CsvConfig() => new(CultureInfo.InvariantCulture)
    {
        MissingFieldFound = null,
        HeaderValidated = null,
        BadDataFound = null
    };

    private static double ParseDouble(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static DateOnly ParseGtfsDate(string? s) =>
        s?.Length == 8 && int.TryParse(s[..4], out var y) && int.TryParse(s[4..6], out var mo) && int.TryParse(s[6..], out var d)
            ? new DateOnly(y, mo, d)
            : DateOnly.MinValue;

    // GTFS times can exceed 24:00:00 (next-day trips) — store as total seconds
    private static int ParseGtfsTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        var parts = s.Split(':');
        if (parts.Length < 3) return 0;
        int.TryParse(parts[0], out var h);
        int.TryParse(parts[1], out var m);
        int.TryParse(parts[2], out var sec);
        return h * 3600 + m * 60 + sec;
    }

    // CSV row types — CsvHelper maps columns by name
    private record StopCsvRow(string stop_id, string? stop_code, string stop_name, string? stop_lat, string? stop_lon);
    private record RouteCsvRow(string route_id, string? agency_id, string? route_short_name, string? route_long_name, string? route_type);
    private record TripCsvRow(string trip_id, string route_id, string service_id, string? trip_headsign, string? direction_id);
    private record CalendarCsvRow(string service_id, string monday, string tuesday, string wednesday, string thursday, string friday, string saturday, string sunday, string? start_date, string? end_date);
    private record CalendarDateCsvRow(string service_id, string? date, string? exception_type);
    private record StopTimeCsvRow(string trip_id, string? arrival_time, string? departure_time, string stop_id, string? stop_sequence);
}
