using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IrelandLiveSignals.Tests;

public class RateLimitingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RateLimitingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var dbDesc = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<GridDbContext>));
                if (dbDesc != null) services.Remove(dbDesc);
                services.AddDbContext<GridDbContext>(options =>
                    options.UseInMemoryDatabase("RateLimitTestDb_" + Guid.NewGuid()));
            });
        });
    }

    [Fact]
    public async Task GridCurrent_Returns429_After60Requests()
    {
        var client = _factory.CreateClient();

        System.Net.HttpStatusCode? lastStatus = null;
        for (int i = 0; i < 61; i++)
        {
            var response = await client.GetAsync("/api/grid/current");
            lastStatus = response.StatusCode;
        }

        Assert.Equal((System.Net.HttpStatusCode)429, lastStatus);
    }

    [Fact]
    public async Task MeFavourites_RequiresAuth_WhenUnauthenticated()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.GetAsync("/api/me/favourites");
        // Identity redirects unauthenticated requests to login (302) or returns 401
        Assert.True(
            response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Redirect,
            $"Expected 401 or 302 but got {response.StatusCode}");
    }
}
