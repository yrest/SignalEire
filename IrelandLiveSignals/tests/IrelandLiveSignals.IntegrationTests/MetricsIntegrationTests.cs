using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IrelandLiveSignals.IntegrationTests;

public class MetricsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MetricsIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace the DB with in-memory for tests
                var dbDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<GridDbContext>));
                if (dbDescriptor != null) services.Remove(dbDescriptor);

                services.AddDbContext<GridDbContext>(options =>
                    options.UseInMemoryDatabase("TestDb_" + Guid.NewGuid()));
            });
        });
    }

    [Fact]
    public async Task GetMetrics_Returns200_WithPrometheusContent()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/metrics");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(body);
    }

    [Fact]
    public async Task GetMetrics_Contains_SignalEireCustomMetrics()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/metrics");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        // Observable gauges always appear in the output once SignalEireMetrics is initialized
        Assert.Contains("signaleire_grid_co2_latest_grams_per_kwh", body);
        // Also verify the counter metadata is registered (may appear as comment even with 0 observations)
        Assert.Contains("signaleire_", body);
    }
}
