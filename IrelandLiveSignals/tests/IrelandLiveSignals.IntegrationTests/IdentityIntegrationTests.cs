using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace IrelandLiveSignals.IntegrationTests;

public class IdentityIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public IdentityIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var dbDesc = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<GridDbContext>));
                if (dbDesc != null) services.Remove(dbDesc);
                services.AddDbContext<GridDbContext>(options =>
                    options.UseInMemoryDatabase("IdentityTestDb_" + Guid.NewGuid()));
            });
        });
    }

    [Fact]
    public async Task GetFavourites_Unauthenticated_RequiresAuth()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/api/me/favourites");

        // Identity redirects unauthenticated requests to login (302/Found) or returns 401
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.Found ||
            response.StatusCode == HttpStatusCode.Redirect,
            $"Expected 401 or 302 but got {response.StatusCode}");
    }

    [Fact]
    public async Task GetFavourites_AfterRegistrationAndLogin_Returns200()
    {
        // Use a shared DB so register creates user, then login works
        var dbName = "IdentityLoginTestDb_" + Guid.NewGuid();
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var dbDesc = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<GridDbContext>));
                if (dbDesc != null) services.Remove(dbDesc);
                services.AddDbContext<GridDbContext>(options =>
                    options.UseInMemoryDatabase(dbName));
                // Disable antiforgery for testing
                services.AddAntiforgery(opts => opts.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.None);
            });
        });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = true,
            HandleCookies = true
        });

        // First GET the register page to obtain antiforgery token
        var getRegisterResponse = await client.GetAsync("/Account/Register");
        Assert.Equal(HttpStatusCode.OK, getRegisterResponse.StatusCode);
        var html = await getRegisterResponse.Content.ReadAsStringAsync();

        // Extract antiforgery token from hidden input
        var tokenMatch = System.Text.RegularExpressions.Regex.Match(
            html, @"<input[^>]+name=""__RequestVerificationToken""[^>]+value=""([^""]+)""");
        var token = tokenMatch.Success ? tokenMatch.Groups[1].Value : "";

        // Register
        var registerContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Email", "test@example.com"),
            new KeyValuePair<string, string>("Password", "Password1!"),
            new KeyValuePair<string, string>("ConfirmPassword", "Password1!"),
            new KeyValuePair<string, string>("DisplayName", "Test User"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        });
        var registerResponse = await client.PostAsync("/Account/Register", registerContent);
        Assert.True(registerResponse.StatusCode == HttpStatusCode.OK ||
                    registerResponse.StatusCode == HttpStatusCode.Redirect ||
                    registerResponse.StatusCode == HttpStatusCode.Found,
                    $"Register returned {registerResponse.StatusCode}");

        // Now fetch favourites - should be 200 (authenticated via cookie)
        var favsResponse = await client.GetAsync("/api/me/favourites");
        Assert.Equal(HttpStatusCode.OK, favsResponse.StatusCode);
    }
}
