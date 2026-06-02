using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using IrelandLiveSignals.Api.Middleware;
using IrelandLiveSignals.Api.Worker;
using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Core.Services;
using TransitUserReport = IrelandLiveSignals.Core.Models.TransitUserReport;
using IrelandLiveSignals.Infrastructure;
using IrelandLiveSignals.Infrastructure.Identity;
using IrelandLiveSignals.Infrastructure.Persistence;
using IrelandLiveSignals.Infrastructure.Services;
using IrelandLiveSignals.Worker;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using System.Threading.RateLimiting;

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

static string HashToken(string token)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
    return Convert.ToBase64String(bytes);
}

static string GenerateRefreshToken() =>
    Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

static string GenerateJwt(ApplicationUser user, IConfiguration config)
{
    var secret = config["Jwt:Secret"]!;
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var expiry = int.Parse(config["Jwt:AccessTokenExpiryMinutes"] ?? "15");

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id),
        new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
        new Claim("display_name", user.DisplayName ?? user.UserName ?? ""),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
    };

    var token = new JwtSecurityToken(
        issuer: config["Jwt:Issuer"],
        audience: config["Jwt:Audience"],
        claims: claims,
        expires: DateTime.UtcNow.AddMinutes(expiry),
        signingCredentials: creds);

    return new JwtSecurityTokenHandler().WriteToken(token);
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJwtAuth(builder.Configuration);

// Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Ireland Live Signals API",
        Version = "v1",
        Description = "Live Irish electricity grid and public transit intelligence. " +
                      "Free for non-commercial use. Attribution required.",
        Contact = new OpenApiContact { Email = "api@yourdomain.ie" }
    });
    o.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-Api-Key",
        Description = "Optional. Provides higher rate limits (200 req/min vs 60 req/min anonymous)."
    });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Required for /api/me/* endpoints. Obtain from POST /api/auth/login."
    });
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath)) o.IncludeXmlComments(xmlPath);
    o.DocInclusionPredicate((_, api) =>
        !(api.RelativePath?.StartsWith("admin/") == true) &&
        !(api.RelativePath?.StartsWith("api/admin/") == true));
});

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
builder.Services.AddHostedService<RagSummaryJob>();
builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("public-api", o =>
    {
        o.PermitLimit = 60;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("push-subscribe", o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
    });
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        await context.HttpContext.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded. Please slow down." }, ct);
    };
});

var app = builder.Build();

var firebasePath = builder.Configuration["Firebase:ServiceAccountPath"];
if (!string.IsNullOrEmpty(firebasePath) && File.Exists(firebasePath))
{
    FirebaseApp.Create(new AppOptions
    {
        Credential = GoogleCredential.FromFile(firebasePath)
    });
}

app.UseExceptionHandler("/Error");
app.UseStatusCodePagesWithReExecute("/{0}");

if (string.IsNullOrEmpty(otlpEndpoint))
{
    app.Logger.LogWarning("Telemetry:OtlpEndpoint not configured — OTLP export disabled.");
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GridDbContext>();
    db.Database.EnsureCreated();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GridDbContext>();
    await TariffPlanSeeder.SeedAsync(db);
}

// Force singleton initialization so OTel instruments are registered at startup
_ = app.Services.GetService<SignalEireMetrics>();

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

if (builder.Configuration.GetValue("Telemetry:EnablePrometheusEndpoint", true))
{
    app.MapPrometheusScrapingEndpoint("/metrics");
}

app.UseSwagger();
app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/swagger/v1/swagger.json", "SignalEire API v1");
    o.RoutePrefix = "api-docs";
    o.DocumentTitle = "Ireland Live Signals API";
});

app.MapRazorPages();

// ── Grid readings ─────────────────────────────────────────────────────────

app.MapGet("/api/grid/current", async (IGridReadingRepository repo, string? region) =>
{
    var reading = await repo.GetLatestAsync(region ?? "ROI");
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
}).RequireRateLimiting("public-api").AllowAnonymous();

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

app.MapGet("/api/grid/health", async (IGridReadingRepository repo, string? region) =>
{
    var reading = await repo.GetLatestAsync(region ?? "ROI");
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

app.MapPost("/api/grid/recommendations/ev-charge", async (HttpContext ctx,
    IGridReadingRepository repo, EvChargeRequest request,
    UserManager<ApplicationUser> userManager, GridDbContext db,
    ITariffRateService tariffRateService) =>
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

    // Load user's tariff plan if authenticated
    TariffPlanEntity? userTariffPlan = null;
    var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (userId is not null)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user?.PreferredTariffPlanId is not null)
        {
            userTariffPlan = await db.TariffPlans
                .Include(p => p.Periods)
                .FirstOrDefaultAsync(p => p.Id == user.PreferredTariffPlanId);
        }
    }

    // Add cost estimates when a tariff plan is available
    decimal? estimatedCostEuro = null;
    decimal? estimatedSavingEuro = null;
    string? tariffPlanName = null;

    if (userTariffPlan is not null)
    {
        var avgRate = tariffRateService.GetAverageRateForWindow(userTariffPlan, best.Start, best.End);
        estimatedCostEuro = (decimal)request.RequiredKwh * avgRate;
        var irelandTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, irelandTz);
        var currentRate = tariffRateService.GetRateAt(userTariffPlan,
            TimeOnly.FromDateTime(localNow.DateTime),
            localNow.DayOfWeek);
        var currentCostTotal = (decimal)request.RequiredKwh * currentRate;
        estimatedSavingEuro = Math.Max(0, currentCostTotal - estimatedCostEuro.Value);
        tariffPlanName = userTariffPlan.Name;
    }

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
        Explanation = explanation.ToArray(),
        EstimatedCostEuro = estimatedCostEuro,
        EstimatedSavingEuro = estimatedSavingEuro,
        TariffPlanName = tariffPlanName
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
        explanation = recommendation.Explanation,
        estimatedCostEuro = recommendation.EstimatedCostEuro,
        estimatedSavingEuro = recommendation.EstimatedSavingEuro,
        tariffPlanName = recommendation.TariffPlanName
    });
}).AllowAnonymous();

// ── Grid compare ─────────────────────────────────────────────────────────

app.MapGet("/api/grid/compare", async (IGridReadingRepository repo, string? regions) =>
{
    var regionList = (regions ?? "ROI,NI").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var snapshots = new List<object>();

    foreach (var regionName in regionList)
    {
        var reading = await repo.GetLatestAsync(regionName);
        if (reading is null)
            return Results.Json(new { error = $"No reading available for region {regionName}." }, statusCode: 503);
        snapshots.Add(new
        {
            region = reading.Region,
            timestampUtc = reading.TimestampUtc,
            co2IntensityGPerKwh = reading.Co2IntensityGPerKwh,
            renewablesPercent = reading.RenewablesPercent,
            greenScore = reading.GreenScore,
            status = reading.GreenStatus
        });
    }

    return Results.Ok(new { snapshots });
}).RequireRateLimiting("public-api").AllowAnonymous();

// ── Tariff plans ──────────────────────────────────────────────────────────

app.MapGet("/api/tariff/plans", async (GridDbContext db) =>
{
    var plans = await db.TariffPlans
        .Where(p => p.IsActive)
        .Select(p => new { p.Id, p.Name, p.Provider, p.PlanType, p.IsDefault })
        .ToListAsync();
    return Results.Ok(plans);
}).AllowAnonymous().RequireRateLimiting("public-api");

app.MapPost("/api/admin/tariff/plans", async (HttpContext ctx, GridDbContext db,
    TariffPlanCreateRequest request) =>
{
    if (!ctx.User.IsInRole("Admin")) return Results.Forbid();
    var plan = new TariffPlanEntity
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = request.Name,
        Provider = request.Provider ?? "Generic",
        PlanType = request.PlanType ?? "custom",
        IsActive = true,
        IsDefault = false,
        Description = request.Description,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };
    db.TariffPlans.Add(plan);
    await db.SaveChangesAsync();
    return Results.Created($"/api/admin/tariff/plans/{plan.Id}", new { plan.Id });
}).RequireAuthorization();

app.MapDelete("/api/admin/tariff/plans/{id}", async (HttpContext ctx, GridDbContext db, string id) =>
{
    if (!ctx.User.IsInRole("Admin")) return Results.Forbid();
    var plan = await db.TariffPlans.Include(p => p.Periods).FirstOrDefaultAsync(p => p.Id == id);
    if (plan is null) return Results.NotFound();
    db.TariffPlans.Remove(plan);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

// ── Developer API keys ────────────────────────────────────────────────────

app.MapPost("/api/admin/developer-keys", async (HttpContext ctx,
    IApiKeyService apiKeyService, DevKeyCreateRequest request) =>
{
    if (!ctx.User.IsInRole("Admin")) return Results.Forbid();
    var (record, plaintext) = await apiKeyService.CreateAsync(
        request.Name, request.OwnerEmail, request.RateLimitPerMinute ?? 200);
    return Results.Ok(new { record.Id, record.Name, plaintextKey = plaintext,
        warning = "This key will not be shown again. Copy it now." });
}).RequireAuthorization();

app.MapGet("/api/admin/developer-keys", async (HttpContext ctx, IApiKeyService apiKeyService) =>
{
    if (!ctx.User.IsInRole("Admin")) return Results.Forbid();
    var keys = await apiKeyService.GetAllAsync();
    return Results.Ok(keys.Select(k => new
    {
        k.Id, k.Name, k.OwnerEmail, k.RateLimitPerMinute,
        k.IsActive, k.CreatedAtUtc, k.LastUsedAtUtc
    }));
}).RequireAuthorization();

app.MapPut("/api/admin/developer-keys/{id}/active", async (HttpContext ctx,
    IApiKeyService apiKeyService, string id, bool active) =>
{
    if (!ctx.User.IsInRole("Admin")) return Results.Forbid();
    await apiKeyService.SetActiveAsync(id, active);
    return Results.Ok();
}).RequireAuthorization();

app.MapDelete("/api/admin/developer-keys/{id}", async (HttpContext ctx,
    IApiKeyService apiKeyService, string id) =>
{
    if (!ctx.User.IsInRole("Admin")) return Results.Forbid();
    await apiKeyService.DeleteAsync(id);
    return Results.NoContent();
}).RequireAuthorization();

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

// ── Auth (JWT) endpoints ──────────────────────────────────────────────────

app.MapPost("/api/auth/login", async (
    HttpContext ctx,
    UserManager<ApplicationUser> userManager,
    GridDbContext db,
    IConfiguration config,
    LoginRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest(new { error = "Email and password are required." });

    var user = await userManager.FindByEmailAsync(request.Email);
    if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
        return Results.Json(new { error = "Invalid credentials" }, statusCode: 401);

    var accessToken = GenerateJwt(user, config);
    var plainRefreshToken = GenerateRefreshToken();
    var expiryDays = int.Parse(config["Jwt:RefreshTokenExpiryDays"] ?? "30");

    var refreshToken = new UserRefreshToken
    {
        Id = Guid.NewGuid().ToString("N"),
        UserId = user.Id,
        TokenHash = HashToken(plainRefreshToken),
        DeviceLabel = request.DeviceLabel ?? "unknown",
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(expiryDays),
        CreatedAtUtc = DateTimeOffset.UtcNow
    };
    db.UserRefreshTokens.Add(refreshToken);
    await db.SaveChangesAsync();

    var expiry = int.Parse(config["Jwt:AccessTokenExpiryMinutes"] ?? "15");
    return Results.Ok(new
    {
        accessToken,
        refreshToken = plainRefreshToken,
        expiresAt = DateTimeOffset.UtcNow.AddMinutes(expiry),
        userId = user.Id,
        displayName = user.DisplayName ?? user.UserName ?? string.Empty
    });
}).AllowAnonymous();

app.MapPost("/api/auth/refresh", async (
    GridDbContext db,
    UserManager<ApplicationUser> userManager,
    IConfiguration config,
    RefreshTokenRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.RefreshToken))
        return Results.Json(new { error = "Invalid or expired refresh token" }, statusCode: 401);

    var hash = HashToken(request.RefreshToken);
    var stored = await db.UserRefreshTokens
        .FirstOrDefaultAsync(t => t.TokenHash == hash);

    if (stored is null || stored.IsRevoked || stored.ExpiresAtUtc < DateTimeOffset.UtcNow)
        return Results.Json(new { error = "Invalid or expired refresh token" }, statusCode: 401);

    // Replay attack detection: token already used
    if (stored.UsedAtUtc.HasValue)
    {
        // Revoke ALL tokens for this user
        var allTokens = db.UserRefreshTokens.Where(t => t.UserId == stored.UserId);
        await allTokens.ForEachAsync(t => t.IsRevoked = true);
        await db.SaveChangesAsync();
        return Results.Json(new { error = "Invalid or expired refresh token" }, statusCode: 401);
    }

    stored.UsedAtUtc = DateTimeOffset.UtcNow;
    stored.IsRevoked = true;

    var user = await userManager.FindByIdAsync(stored.UserId);
    if (user is null)
        return Results.Json(new { error = "Invalid or expired refresh token" }, statusCode: 401);

    var newAccessToken = GenerateJwt(user, config);
    var newPlainRefresh = GenerateRefreshToken();
    var expiryDays = int.Parse(config["Jwt:RefreshTokenExpiryDays"] ?? "30");

    var newRefreshToken = new UserRefreshToken
    {
        Id = Guid.NewGuid().ToString("N"),
        UserId = user.Id,
        TokenHash = HashToken(newPlainRefresh),
        DeviceLabel = stored.DeviceLabel,
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(expiryDays),
        CreatedAtUtc = DateTimeOffset.UtcNow
    };
    db.UserRefreshTokens.Add(newRefreshToken);
    await db.SaveChangesAsync();

    var expiry = int.Parse(config["Jwt:AccessTokenExpiryMinutes"] ?? "15");
    return Results.Ok(new
    {
        accessToken = newAccessToken,
        refreshToken = newPlainRefresh,
        expiresAt = DateTimeOffset.UtcNow.AddMinutes(expiry),
        userId = user.Id,
        displayName = user.DisplayName ?? user.UserName ?? string.Empty
    });
}).AllowAnonymous();

app.MapPost("/api/auth/logout", async (
    HttpContext ctx,
    GridDbContext db,
    LogoutRequest request) =>
{
    var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (userId is null) return Results.Unauthorized();

    if (!string.IsNullOrWhiteSpace(request.RefreshToken))
    {
        var hash = HashToken(request.RefreshToken);
        var token = await db.UserRefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.UserId == userId);
        if (token is not null)
            token.IsRevoked = true;
    }

    // Remove device token for the platform
    if (!string.IsNullOrWhiteSpace(request.Platform))
    {
        var deviceToken = await db.DeviceTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Platform == request.Platform);
        if (deviceToken is not null)
            db.DeviceTokens.Remove(deviceToken);
    }

    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization(new AuthorizationPolicyBuilder()
    .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
    .RequireAuthenticatedUser()
    .Build());

// ── Device token (mobile push) ─────────────────────────────────────────────

app.MapPost("/api/push/device-token", async (
    HttpContext ctx,
    GridDbContext db,
    DeviceTokenRequest request) =>
{
    var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (userId is null) return Results.Unauthorized();

    // Remove any previous association for this FCM token
    var existing = await db.DeviceTokens.FirstOrDefaultAsync(t => t.Token == request.Token);
    if (existing is not null && existing.UserId != userId)
        db.DeviceTokens.Remove(existing);

    var mine = await db.DeviceTokens
        .FirstOrDefaultAsync(t => t.UserId == userId && t.Platform == request.Platform);

    if (mine is not null)
    {
        mine.LastSeenAtUtc = DateTimeOffset.UtcNow;
        // Update token if it changed
        if (mine.Token != request.Token)
        {
            db.DeviceTokens.Remove(mine);
            db.DeviceTokens.Add(new DeviceToken
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                Token = request.Token,
                Platform = request.Platform,
                RegisteredAtUtc = DateTimeOffset.UtcNow,
                LastSeenAtUtc = DateTimeOffset.UtcNow
            });
        }
    }
    else
    {
        db.DeviceTokens.Add(new DeviceToken
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            Token = request.Token,
            Platform = request.Platform,
            RegisteredAtUtc = DateTimeOffset.UtcNow,
            LastSeenAtUtc = DateTimeOffset.UtcNow
        });
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { registered = true });
}).RequireAuthorization(new AuthorizationPolicyBuilder()
    .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
    .RequireAuthenticatedUser()
    .Build());

app.MapDelete("/api/push/device-token", async (
    HttpContext ctx,
    GridDbContext db,
    string platform) =>
{
    var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (userId is null) return Results.Unauthorized();

    var token = await db.DeviceTokens
        .FirstOrDefaultAsync(t => t.UserId == userId && t.Platform == platform);
    if (token is not null)
        db.DeviceTokens.Remove(token);

    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization(new AuthorizationPolicyBuilder()
    .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
    .RequireAuthenticatedUser()
    .Build());

// ── /api/me endpoints ─────────────────────────────────────────────────────

// Accept both cookie (Identity) and JWT Bearer. Only add the Bearer scheme
// to the policy when JWT is actually configured; otherwise the auth middleware
// throws because no handler is registered for that scheme.
var jwtConfigured = !string.IsNullOrEmpty(app.Configuration["Jwt:Secret"]);
var dualSchemePolicyBuilder = new AuthorizationPolicyBuilder()
    .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
    .RequireAuthenticatedUser();
if (jwtConfigured)
    dualSchemePolicyBuilder.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
var dualSchemePolicy = dualSchemePolicyBuilder.Build();

app.MapGet("/api/me/favourites", async (HttpContext ctx, GridDbContext db) =>
{
    var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (userId is null) return Results.Unauthorized();
    var favs = await db.FavouriteStops.Where(f => f.UserId == userId)
        .OrderBy(f => f.SortOrder).ToListAsync();
    return Results.Ok(favs);
}).RequireAuthorization(dualSchemePolicy);

app.MapPost("/api/me/favourites", async (HttpContext ctx, GridDbContext db, FavouriteStopRequest request) =>
{
    var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (userId is null) return Results.Unauthorized();
    var fav = new FavouriteStop
    {
        Id = Guid.NewGuid().ToString("N"),
        UserId = userId,
        StopId = request.StopId,
        DisplayLabel = request.DisplayLabel,
        SortOrder = request.SortOrder ?? 0,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };
    db.FavouriteStops.Add(fav);
    await db.SaveChangesAsync();
    return Results.Created($"/api/me/favourites/{fav.Id}", fav);
}).RequireAuthorization(dualSchemePolicy);

app.MapDelete("/api/me/favourites/{stopId}", async (HttpContext ctx, GridDbContext db, string stopId) =>
{
    var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (userId is null) return Results.Unauthorized();
    var fav = await db.FavouriteStops.FirstOrDefaultAsync(f => f.UserId == userId && f.StopId == stopId);
    if (fav is null) return Results.NotFound();
    db.FavouriteStops.Remove(fav);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization(dualSchemePolicy);

app.MapGet("/api/me/alerts", async (HttpContext ctx, GridDbContext db) =>
{
    var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (userId is null) return Results.Unauthorized();
    var rules = await db.AlertRules.Where(r => r.UserId == userId).ToListAsync();
    return Results.Ok(rules);
}).RequireAuthorization(dualSchemePolicy);

app.MapPost("/api/me/alerts", async (HttpContext ctx, GridDbContext db, AlertRuleRequest request) =>
{
    var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (userId is null) return Results.Unauthorized();
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
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UserId = userId
    };
    db.AlertRules.Add(rule);
    await db.SaveChangesAsync();
    return Results.Created($"/api/me/alerts/{rule.Id}", new { rule.Id, rule.RuleName });
}).RequireAuthorization(dualSchemePolicy);

app.MapDelete("/api/me/alerts/{id}", async (HttpContext ctx, GridDbContext db, string id) =>
{
    var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (userId is null) return Results.Unauthorized();
    var rule = await db.AlertRules.FindAsync(id);
    if (rule is null || rule.UserId != userId) return Results.NotFound();
    db.AlertRules.Remove(rule);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization(dualSchemePolicy);

app.MapGet("/api/me/devices", (HttpContext ctx) =>
{
    var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (userId is null) return Results.Unauthorized();
    return Results.Ok(Array.Empty<object>());
}).RequireAuthorization(dualSchemePolicy);

app.MapGet("/api/me/tariff-plan", async (HttpContext ctx, GridDbContext db,
    UserManager<ApplicationUser> userManager) =>
{
    var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (userId is null) return Results.Unauthorized();
    var user = await userManager.FindByIdAsync(userId);
    if (user is null) return Results.Unauthorized();
    var plan = user.PreferredTariffPlanId is not null
        ? await db.TariffPlans.FindAsync(user.PreferredTariffPlanId)
        : null;
    return Results.Ok(new { tariffPlanId = user.PreferredTariffPlanId, planName = plan?.Name });
}).RequireAuthorization(dualSchemePolicy);

app.MapPut("/api/me/tariff-plan", async (HttpContext ctx, GridDbContext db,
    UserManager<ApplicationUser> userManager,
    TariffPlanSelectRequest request) =>
{
    var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (userId is null) return Results.Unauthorized();
    var user = await userManager.FindByIdAsync(userId);
    if (user is null) return Results.Unauthorized();
    user.PreferredTariffPlanId = request.TariffPlanId;
    await userManager.UpdateAsync(user);
    return Results.Ok(new { saved = true });
}).RequireAuthorization(dualSchemePolicy);

// ── Push API ──────────────────────────────────────────────────────────────

app.MapGet("/api/push/vapid-public-key", (IConfiguration config) =>
{
    var publicKey = config["WebPush:VapidPublicKey"] ?? "";
    return Results.Ok(new { publicKey });
});

app.MapPost("/api/push/subscribe", async (HttpContext ctx, GridDbContext db, PushSubscribeRequest request) =>
{
    var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    var existing = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == request.Endpoint);
    if (existing is not null)
    {
        existing.LastSeenAtUtc = DateTimeOffset.UtcNow;
        if (userId is not null) existing.UserId = userId;
        await db.SaveChangesAsync();
        return Results.Ok(new { existing.Id });
    }
    var sub = new PushSubscription
    {
        Id = Guid.NewGuid().ToString("N"),
        UserId = userId,
        Endpoint = request.Endpoint,
        P256Dh = request.Keys?.P256dh ?? "",
        Auth = request.Keys?.Auth ?? "",
        SubscribedAtUtc = DateTimeOffset.UtcNow,
        LastSeenAtUtc = DateTimeOffset.UtcNow
    };
    db.PushSubscriptions.Add(sub);
    await db.SaveChangesAsync();
    return Results.Created($"/api/push/subscribe/{sub.Id}", new { sub.Id });
}).RequireRateLimiting("push-subscribe");

app.MapDelete("/api/push/subscribe", async (HttpContext ctx, GridDbContext db) =>
{
    var request = await ctx.Request.ReadFromJsonAsync<PushUnsubscribeRequest>();
    if (request?.Endpoint is null) return Results.BadRequest();
    var sub = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == request.Endpoint);
    if (sub is not null)
    {
        db.PushSubscriptions.Remove(sub);
        await db.SaveChangesAsync();
    }
    return Results.NoContent();
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

record TariffPlanSelectRequest(string? TariffPlanId);
record TariffPlanCreateRequest(string Name, string? Provider, string? PlanType, string? Description);
record DevKeyCreateRequest(string Name, string OwnerEmail, int? RateLimitPerMinute);

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

record FavouriteStopRequest(string StopId, string? DisplayLabel, int? SortOrder);
record PushSubscribeRequest(string Endpoint, PushSubscribeKeys? Keys);
record PushSubscribeKeys(string P256dh, string Auth);
record PushUnsubscribeRequest(string Endpoint);
record LoginRequest(string Email, string Password, string? DeviceLabel);
record RefreshTokenRequest(string RefreshToken);
record LogoutRequest(string? RefreshToken, string? Platform);
record DeviceTokenRequest(string Token, string Platform);

// Required for WebApplicationFactory in integration tests
public partial class Program { }
