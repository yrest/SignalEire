using IrelandLiveSignals.Api.Worker;
using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Core.Services;
using TransitUserReport = IrelandLiveSignals.Core.Models.TransitUserReport;
using IrelandLiveSignals.Infrastructure;
using IrelandLiveSignals.Infrastructure.Persistence;
using IrelandLiveSignals.Worker;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
{
    const double R = 6_371_000;
    var dLat = (lat2 - lat1) * Math.PI / 180;
    var dLon = (lon2 - lon1) * Math.PI / 180;
    var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
          + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
          * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
    return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

// Phase 6 singletons
builder.Services.AddSingleton<LiveSignalState>();
builder.Services.AddSingleton<FeedHealthStore>();
builder.Services.AddSingleton<SignalEireMetrics>();

// OTel setup
var otlpEndpoint = builder.Configuration["Telemetry:OtlpEndpoint"];
var otelBuilder = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService(
            serviceName: builder.Configuration["Telemetry:ServiceName"] ?? "ireland-live-signals",
            serviceVersion: System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown")
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Configuration["Telemetry:Environment"] ?? "unknown"
        }))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(o =>
            {
                o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/metrics")
                               && !ctx.Request.Path.StartsWithSegments("/health");
            })
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation();
        if (!string.IsNullOrEmpty(otlpEndpoint))
            tracing.AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(otlpEndpoint);
                o.Headers = $"Authorization={builder.Configuration["Telemetry:OtlpAuthHeader"]}";
            });
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddMeter("IrelandLiveSignals.Grid")
            .AddMeter("IrelandLiveSignals.Transit")
            .AddMeter("IrelandLiveSignals.Alerts")
            .AddMeter("IrelandLiveSignals.Feeds")
            .AddPrometheusExporter();
        if (!string.IsNullOrEmpty(otlpEndpoint))
            metrics.AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(otlpEndpoint);
                o.Headers = $"Authorization={builder.Configuration["Telemetry:OtlpAuthHeader"]}";
            });
    });

builder.Services.AddHostedService<GridPollerService>();
builder.Services.AddHostedService<TransitPollerService>();
builder.Services.AddHostedService<GtfsStaticRefreshService>();
builder.Services.AddHostedService<ReliabilityAggregationService>();
builder.Services.AddHostedService<AnomalyDetectionJob>();
builder.Services.AddHostedService<DigestJob>();
builder.Services.AddRazorPages();

var app = builder.Build();

if (string.IsNullOrEmpty(otlpEndpoint))
{
    app.Logger.LogWarning("Telemetry:OtlpEndpoint not configured — OTLP export disabled.");
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GridDbContext>();
    db.Database.EnsureCreated();
}

// Force singleton initialization so OTel instruments are registered at startup
_ = app.Services.GetService<SignalEireMetrics>();

app.UseStaticFiles();
app.UseRouting();

if (builder.Configuration.GetValue("Telemetry:EnablePrometheusEndpoint", true))
{
    app.MapPrometheusScrapingEndpoint("/metrics");
}

app.MapRazorPages();

// ── Grid readings ─────────────────────────────────────────────────────────

app.MapGet("/api/grid/current", async (IGridReadingRepository repo) =>
{
    var reading = await repo.GetLatestAsync();
    if (reading is null)
        return Results.Json(new { error = "No grid data available yet." }, statusCode: 503);

    return Results.Ok(new
    {
        region = reading.Region,
        timestampUtc = reading.TimestampUtc,
        dataFreshnessSeconds = reading.DataFreshnessSeconds,
        systemDemandMw = reading.SystemDemandMw,
        windGenerationMw = reading.WindGenerationMw,
        solarGenerationMw = reading.SolarGenerationMw,
        renewablesPercent = reading.RenewablesPercent,
        co2IntensityGPerKwh = reading.Co2IntensityGPerKwh,
        greenScore = reading.GreenScore,
        status = reading.GreenStatus,
        recommendation = reading.Recommendation
    });
});

app.MapGet("/api/grid/history", async (IGridReadingRepository repo,
    string? region,
    DateTimeOffset? from,
    DateTimeOffset? to) =>
{
    var f = from ?? DateTimeOffset.UtcNow.AddDays(-1);
    var t = to   ?? DateTimeOffset.UtcNow;
    var readings = await repo.GetRangeAsync(f, t);
    return Results.Ok(readings.Select(r => new
    {
        r.Id, r.Region, r.TimestampUtc, r.SystemDemandMw, r.WindGenerationMw,
        r.RenewablesPercent, r.Co2IntensityGPerKwh, r.GreenScore, r.GreenStatus,
        r.DataFreshnessSeconds, r.QualityStatus
    }));
});

app.MapGet("/api/grid/health", async (IGridReadingRepository repo) =>
{
    var reading = await repo.GetLatestAsync();
    var secondsSince = reading is null
        ? (int?)null
        : (int)(DateTimeOffset.UtcNow - reading.TimestampUtc).TotalSeconds;

    return Results.Ok(new
    {
        status = "ok",
        lastReadingUtc = reading?.TimestampUtc,
        secondsSinceLastReading = secondsSince,
        workerRunning = GridPollerService.IsRunning
    });
});

// ── Best window (simple GET without EV parameters) ────────────────────────

app.MapGet("/api/grid/best-window", async (IGridReadingRepository repo,
    int durationMinutes = 60,
    string mode = "balanced",
    string tariff = "standard",
    DateTimeOffset? deadline = null) =>
{
    var history = await repo.GetRecentAsync(7 * 48); // 7 days of 30-min slots
    if (history.Count == 0)
        return Results.Json(new { error = "Insufficient grid history for recommendation." }, statusCode: 503);

    var now = DateTimeOffset.UtcNow;
    var dl = deadline ?? now.AddHours(12);
    var forecast = GridForecastService.Forecast(history, now, dl);
    var tariffPlan = TariffCatalogue.GetById(tariff);
    var (best, currentCo2) = WindowRecommendationService.FindBestWindow(forecast, durationMinutes, mode, tariffPlan);

    if (best is null)
        return Results.Json(new { error = "No suitable window found within the deadline." }, statusCode: 422);

    return Results.Ok(new
    {
        recommendedStartUtc = best.Start,
        recommendedEndUtc = best.End,
        durationMinutes,
        averageCo2GPerKwh = best.AverageCo2,
        averageRenewablesPercent = best.AverageRenewables,
        averageGreenScore = best.AverageGreenScore,
        confidence = best.Confidence,
        mode,
        tariff
    });
});

// ── EV charge recommendation ──────────────────────────────────────────────

app.MapPost("/api/grid/recommendations/ev-charge", async (IGridReadingRepository repo, EvChargeRequest request) =>
{
    if (request.RequiredKwh <= 0 || request.ChargerKw <= 0)
        return Results.BadRequest(new { error = "requiredKwh and chargerKw must be positive." });

    var durationMinutes = (int)Math.Ceiling((request.RequiredKwh / request.ChargerKw) * 60);
    var now = DateTimeOffset.UtcNow;

    if (request.DeadlineUtc <= now.AddMinutes(durationMinutes))
        return Results.BadRequest(new { error = "Deadline does not allow enough time to complete charging." });

    var history = await repo.GetRecentAsync(7 * 48);
    if (history.Count == 0)
        return Results.Json(new { error = "Insufficient grid history for recommendation." }, statusCode: 503);

    var current = history.Last();
    var forecast = GridForecastService.Forecast(history, now, request.DeadlineUtc);
    var tariffPlan = TariffCatalogue.GetById(request.TariffPlan ?? "standard");

    TimeOnly? quietStart = request.QuietHoursStart.HasValue ? TimeOnly.FromTimeSpan(request.QuietHoursStart.Value) : null;
    TimeOnly? quietEnd   = request.QuietHoursEnd.HasValue   ? TimeOnly.FromTimeSpan(request.QuietHoursEnd.Value)   : null;

    var (best, currentCo2) = WindowRecommendationService.FindBestWindow(
        forecast, durationMinutes, request.Mode ?? "balanced", tariffPlan, quietStart, quietEnd);

    if (best is null)
        return Results.Json(new { error = "No suitable charging window found within the deadline." }, statusCode: 422);

    var saving = CarbonSavingsEstimator.EstimateSavingKg(request.RequiredKwh, currentCo2, best.AverageCo2);
    var decision = best.Start <= now.AddMinutes(30) ? "start_now" : "wait";

    var explanation = new List<string>();
    if (decision == "wait")
        explanation.Add($"Current CO₂ intensity is {currentCo2:F0} g/kWh. A cleaner window starts around {best.Start:HH:mm} UTC.");
    else
        explanation.Add("Current grid conditions are already near-optimal. Starting now is recommended.");
    if (saving > 0.01)
        explanation.Add($"Estimated CO₂ saving vs charging now: {saving:F2} kg.");
    explanation.Add($"Recommended window satisfies the deadline of {request.DeadlineUtc:HH:mm} UTC.");

    var recommendation = new GridRecommendation
    {
        Id = $"rec_{Guid.NewGuid():N}",
        Region = "ROI",
        DeviceType = "ev",
        CreatedAtUtc = now,
        RequiredKwh = request.RequiredKwh,
        ChargerKw = request.ChargerKw,
        DeadlineUtc = request.DeadlineUtc,
        Mode = request.Mode ?? "balanced",
        TariffPlan = request.TariffPlan ?? "standard",
        Decision = decision,
        RecommendedStartUtc = best.Start,
        RecommendedEndUtc = best.End,
        RequiredDurationMinutes = durationMinutes,
        EstimatedAverageCo2GPerKwh = best.AverageCo2,
        EstimatedSavingKgCo2 = saving,
        Confidence = best.Confidence,
        Explanation = explanation.ToArray()
    };

    return Results.Ok(new
    {
        recommendation = recommendation.Decision,
        recommendedStartUtc = recommendation.RecommendedStartUtc,
        recommendedEndUtc = recommendation.RecommendedEndUtc,
        requiredDurationMinutes = recommendation.RequiredDurationMinutes,
        estimatedAverageCo2GPerKwh = recommendation.EstimatedAverageCo2GPerKwh,
        estimatedSavingKgCo2 = recommendation.EstimatedSavingKgCo2,
        confidence = recommendation.Confidence,
        explanation = recommendation.Explanation
    });
});

// ── Alert rules ───────────────────────────────────────────────────────────

app.MapPost("/api/grid/alerts", async (IAlertRuleRepository repo, AlertRuleRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.RuleName))
        return Results.BadRequest(new { error = "ruleName is required." });

    var rule = new AlertRule
    {
        Id = $"rule_{Guid.NewGuid():N}",
        RuleName = request.RuleName,
        Region = "ROI",
        Co2BelowGPerKwh = request.Co2BelowGPerKwh,
        RenewablesAbovePercent = request.RenewablesAbovePercent,
        GreenScoreAbove = request.GreenScoreAbove,
        QuietHoursStart = request.QuietHoursStart.HasValue ? TimeOnly.FromTimeSpan(request.QuietHoursStart.Value) : null,
        QuietHoursEnd = request.QuietHoursEnd.HasValue ? TimeOnly.FromTimeSpan(request.QuietHoursEnd.Value) : null,
        MaxAlertsPerDay = request.MaxAlertsPerDay ?? 2,
        IsActive = true,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    await repo.SaveAsync(rule);
    return Results.Created($"/api/grid/alerts/{rule.Id}", new { rule.Id, rule.RuleName });
});

app.MapGet("/api/grid/alerts", async (IAlertRuleRepository repo) =>
{
    var rules = await repo.GetActiveAsync();
    return Results.Ok(rules);
});

app.MapDelete("/api/grid/alerts/{id}", async (IAlertRuleRepository repo, string id) =>
{
    var rule = await repo.GetByIdAsync(id);
    if (rule is null) return Results.NotFound();
    await repo.DeleteAsync(id);
    return Results.NoContent();
});

app.MapGet("/api/grid/alerts/deliveries", async (IAlertRuleRepository repo, int limit = 50) =>
{
    var deliveries = await repo.GetRecentDeliveriesAsync(limit);
    return Results.Ok(deliveries);
});

// ── Transit stops ─────────────────────────────────────────────────────────

app.MapGet("/api/transit/stops/search", async (ITransitRepository repo, string q, int limit = 20) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest(new { error = "q is required." });
    var stops = await repo.SearchStopsAsync(q, limit);
    return Results.Ok(stops);
});

app.MapGet("/api/transit/stops/nearby", async (ITransitRepository repo,
    double lat, double lon, int radiusMeters = 500, int limit = 20) =>
{
    var stops = await repo.GetNearbyStopsAsync(lat, lon, radiusMeters, limit);
    return Results.Ok(stops);
});

app.MapGet("/api/transit/stops/{stopId}/arrivals", async (ITransitRepository repo, string stopId,
    int windowMinutes = 90) =>
{
    var stop = await repo.GetStopAsync(stopId);
    if (stop is null)
        return Results.NotFound(new { error = $"Stop '{stopId}' not found." });

    var now = DateTimeOffset.UtcNow;
    var to = now.AddMinutes(windowMinutes);
    var scheduled = await repo.GetScheduledArrivalsAsync(stopId, now, to);

    var predictions = await Task.WhenAll(scheduled.Select(async item =>
    {
        var (st, trip, route) = item;
        var vehicle = await repo.GetVehicleForTripAsync(trip.TripId);
        var delay = await repo.GetDelaySecondsAsync(trip.TripId, st.StopId);
        var alert = await repo.GetAlertForRouteAsync(route.RouteId);

        var presence = vehicle is not null
            ? VehiclePresence.VehicleConfirmed
            : delay.HasValue
                ? VehiclePresence.TripUpdateOnly
                : VehiclePresence.TimetableOnly;

        double? distToStop = vehicle is not null
            ? HaversineMeters(vehicle.Lat, vehicle.Lon, stop.StopLat, stop.StopLon)
            : null;

        var confidence = TransitConfidenceService.Compute(new ConfidenceInput
        {
            Presence = presence,
            GpsAgeSeconds = vehicle?.GpsAgeSeconds,
            TripIdMatched = vehicle?.TripId == trip.TripId,
            RouteMatched = vehicle?.RouteId == route.RouteId,
            DistanceToStopMeters = distToStop,
            HasServiceAlert = alert is not null,
            AlertEffect = alert?.Effect ?? string.Empty
        });

        var scheduledSeconds = st.ArrivalSeconds + (delay ?? 0);
        var midnight = DateTimeOffset.UtcNow.Date;
        var eta = new DateTimeOffset(midnight, TimeSpan.Zero).AddSeconds(scheduledSeconds);

        return new
        {
            tripId = trip.TripId,
            routeId = route.RouteId,
            routeShortName = route.RouteShortName,
            headsign = trip.TripHeadsign,
            scheduledArrivalUtc = new DateTimeOffset(midnight, TimeSpan.Zero).AddSeconds(st.ArrivalSeconds),
            estimatedArrivalUtc = eta,
            delaySeconds = delay ?? 0,
            confidence = Math.Round(confidence.Score, 3),
            ghostRisk = confidence.GhostRisk,
            statusLabel = confidence.StatusLabel,
            vehicleId = vehicle?.VehicleId,
            vehicleLat = vehicle?.Lat,
            vehicleLon = vehicle?.Lon,
            alertEffect = alert?.Effect
        };
    }));

    return Results.Ok(new { stopId, stopName = stop.StopName, arrivals = predictions });
});

app.MapGet("/api/transit/routes/{routeId}/vehicles", async (ITransitRepository repo, string routeId) =>
{
    var vehicles = await repo.GetVehiclesForRouteAsync(routeId);
    return Results.Ok(vehicles);
});

app.MapGet("/api/transit/alerts", async (ITransitRepository repo) =>
{
    var alerts = await repo.GetActiveAlertsAsync();
    return Results.Ok(alerts);
});

// ── Transit reliability (Phase 4) ─────────────────────────────────────────

app.MapGet("/api/transit/trips/{tripId}/trust", async (ITransitRepository repo, string tripId) =>
{
    var vehicle = await repo.GetVehicleForTripAsync(tripId);
    var trailSince = DateTimeOffset.UtcNow.AddHours(-4);
    var trail = vehicle is not null
        ? await repo.GetVehicleTrailAsync(vehicle.VehicleId, trailSince)
        : [];

    if (vehicle is null && !trail.Any())
        return Results.NotFound(new { error = $"No live data for trip '{tripId}'." });

    var presence = vehicle is not null
        ? VehiclePresence.VehicleConfirmed
        : VehiclePresence.TripUpdateOnly;

    var confidence = TransitConfidenceService.Compute(new ConfidenceInput
    {
        Presence = presence,
        GpsAgeSeconds = vehicle?.GpsAgeSeconds,
        TripIdMatched = true,
        RouteMatched = true,
        HasServiceAlert = false
    });

    return Results.Ok(new
    {
        tripId,
        vehicleId = vehicle?.VehicleId,
        routeId = vehicle?.RouteId,
        currentLat = vehicle?.Lat,
        currentLon = vehicle?.Lon,
        bearing = vehicle?.Bearing,
        speedKph = vehicle?.SpeedKph,
        gpsAgeSeconds = vehicle?.GpsAgeSeconds,
        observedAtUtc = vehicle?.ObservedAtUtc,
        confidence = Math.Round(confidence.Score, 3),
        ghostRisk = confidence.GhostRisk,
        statusLabel = confidence.StatusLabel,
        trailPoints = trail.Select(p => new { p.ObservedAtUtc, p.Lat, p.Lon, p.Bearing, p.SpeedKph })
    });
});

app.MapGet("/api/transit/vehicles/{vehicleId}/trail", async (ITransitRepository repo,
    string vehicleId, int hours = 4) =>
{
    var since = DateTimeOffset.UtcNow.AddHours(-Math.Clamp(hours, 1, 48));
    var trail = await repo.GetVehicleTrailAsync(vehicleId, since);
    return Results.Ok(new { vehicleId, since, points = trail });
});

app.MapPost("/api/transit/reports", async (ITransitRepository repo, TransitReportRequest request) =>
{
    var validTypes = new[] { "bus_seen", "bus_not_appeared", "bus_passed_full", "wrong_destination", "gps_marker_wrong" };
    if (!validTypes.Contains(request.ReportType))
        return Results.BadRequest(new { error = $"reportType must be one of: {string.Join(", ", validTypes)}" });
    if (string.IsNullOrWhiteSpace(request.StopId) || string.IsNullOrWhiteSpace(request.RouteId))
        return Results.BadRequest(new { error = "stopId and routeId are required." });

    var report = new TransitUserReport
    {
        Id = $"report_{Guid.NewGuid():N}",
        StopId = request.StopId,
        RouteId = request.RouteId,
        TripId = request.TripId,
        ReportType = request.ReportType,
        ReportedAtUtc = DateTimeOffset.UtcNow,
        ReporterLat = request.Lat,
        ReporterLon = request.Lon,
        TrustWeight = 0.4
    };

    await repo.SaveUserReportAsync(report);
    return Results.Created($"/api/transit/reports/{report.Id}", new { report.Id, report.ReportType });
});

app.MapGet("/api/transit/reliability", async (ITransitRepository repo,
    string? routeId, string? stopId) =>
{
    if (routeId is not null && stopId is not null)
    {
        var agg = await repo.GetReliabilityAsync(routeId, stopId);
        if (agg is null)
            return Results.NotFound(new { error = "No reliability data for this route/stop combination." });

        var reports = await repo.GetUserReportsAsync(routeId, stopId, DateTimeOffset.UtcNow.AddDays(-30));
        var report = TransitReliabilityService.BuildReport(agg, reports);
        return Results.Ok(report);
    }

    var top = await repo.GetTopReliableRoutesAsync(20);
    return Results.Ok(top);
});

app.Run();

// ── Request types ─────────────────────────────────────────────────────────

record EvChargeRequest(
    double RequiredKwh,
    double ChargerKw,
    DateTimeOffset DeadlineUtc,
    string? Mode,
    string? TariffPlan,
    TimeSpan? QuietHoursStart,
    TimeSpan? QuietHoursEnd
);

record AlertRuleRequest(
    string RuleName,
    double? Co2BelowGPerKwh,
    double? RenewablesAbovePercent,
    double? GreenScoreAbove,
    int? MaxAlertsPerDay,
    TimeSpan? QuietHoursStart,
    TimeSpan? QuietHoursEnd
);

record TransitReportRequest(
    string StopId,
    string RouteId,
    string? TripId,
    string ReportType,
    double? Lat,
    double? Lon
);

// Required for WebApplicationFactory in integration tests
public partial class Program { }
