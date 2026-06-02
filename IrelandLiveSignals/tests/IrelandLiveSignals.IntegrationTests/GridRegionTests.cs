using IrelandLiveSignals.Core.Models;
using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace IrelandLiveSignals.IntegrationTests;

/// <summary>
/// Integration tests for the /api/grid/current and /api/grid/compare endpoints,
/// verifying region-based filtering and multi-region comparison.
/// </summary>
public class GridRegionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string JwtSecret = "super-secret-key-for-testing-at-least-32-chars!!";

    public GridRegionTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Creates a factory backed by a uniquely-named in-memory database,
    /// with JWT settings provided so the app starts cleanly.
    /// </summary>
    private WebApplicationFactory<Program> CreateFactory(string dbName)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var dbDesc = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<GridDbContext>));
                if (dbDesc != null) services.Remove(dbDesc);
                services.AddDbContext<GridDbContext>(options =>
                    options.UseInMemoryDatabase(dbName));

                // Replace the factory used by ApiKeyService / push service
                var factoryDesc = services.SingleOrDefault(d =>
                    d.ServiceType == typeof(IDbContextFactory<GridDbContext>));
                if (factoryDesc != null) services.Remove(factoryDesc);
                services.AddDbContextFactory<GridDbContext>(options =>
                    options.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped);
            });
            builder.UseSetting("Jwt:Secret", JwtSecret);
            builder.UseSetting("Jwt:Issuer", "ireland-live-signals");
            builder.UseSetting("Jwt:Audience", "ireland-live-signals-clients");
            builder.UseSetting("Jwt:AccessTokenExpiryMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenExpiryDays", "30");
        });
    }

    private static GridReading BuildReading(string region, string id) => new()
    {
        Id = id,
        Region = region,
        TimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
        SystemDemandMw = region == "ROI" ? 4000 : 1500,
        WindGenerationMw = region == "ROI" ? 2000 : 600,
        RenewablesPercent = region == "ROI" ? 55.0 : 42.0,
        Co2IntensityGPerKwh = region == "ROI" ? 200.0 : 280.0,
        DataFreshnessSeconds = 120,
        GreenScore = region == "ROI" ? 0.72 : 0.54,
        GreenStatus = region == "ROI" ? "good" : "moderate",
        Recommendation = $"Grid OK for {region}",
        QualityStatus = "ok"
    };

    private async Task SeedReadingsAsync(WebApplicationFactory<Program> factory,
        params GridReading[] readings)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GridDbContext>();
        db.GridReadings.AddRange(readings);
        await db.SaveChangesAsync();
    }

    // ── /api/grid/current (default ROI) ──────────────────────────────────────

    [Fact]
    public async Task GetCurrent_NoRegionParam_ReturnsRoiReading()
    {
        var dbName = "GridRegion_Default_" + Guid.NewGuid();
        var factory = CreateFactory(dbName);
        await SeedReadingsAsync(factory,
            BuildReading("ROI", "roi_1"),
            BuildReading("NI", "ni_1"));

        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/grid/current");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ROI", body.GetProperty("region").GetString());
    }

    // ── /api/grid/current?region=NI ──────────────────────────────────────────

    [Fact]
    public async Task GetCurrent_RegionNI_ReturnsNiReading()
    {
        var dbName = "GridRegion_NI_" + Guid.NewGuid();
        var factory = CreateFactory(dbName);
        await SeedReadingsAsync(factory,
            BuildReading("ROI", "roi_2"),
            BuildReading("NI", "ni_2"));

        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/grid/current?region=NI");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NI", body.GetProperty("region").GetString());
    }

    // ── /api/grid/current?region=ROI ─────────────────────────────────────────

    [Fact]
    public async Task GetCurrent_RegionROI_ReturnsRoiReading()
    {
        var dbName = "GridRegion_ROI_" + Guid.NewGuid();
        var factory = CreateFactory(dbName);
        await SeedReadingsAsync(factory,
            BuildReading("ROI", "roi_3"),
            BuildReading("NI", "ni_3"));

        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/grid/current?region=ROI");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ROI", body.GetProperty("region").GetString());
    }

    // ── /api/grid/compare?regions=ROI,NI ─────────────────────────────────────

    [Fact]
    public async Task GetCompare_RegionsROI_And_NI_ReturnsBothSnapshots()
    {
        var dbName = "GridRegion_Compare_" + Guid.NewGuid();
        var factory = CreateFactory(dbName);
        await SeedReadingsAsync(factory,
            BuildReading("ROI", "roi_4"),
            BuildReading("NI", "ni_4"));

        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/grid/compare?regions=ROI,NI");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.TryGetProperty("snapshots", out var snapshots));
        Assert.Equal(2, snapshots.GetArrayLength());

        var regionValues = snapshots.EnumerateArray()
            .Select(s => s.GetProperty("region").GetString())
            .ToHashSet();

        Assert.Contains("ROI", regionValues);
        Assert.Contains("NI", regionValues);
    }
}
