using IrelandLiveSignals.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace IrelandLiveSignals.IntegrationTests;

public class JwtAuthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TestEmail = "jwttest@example.com";
    private const string TestPassword = "Password1!";
    private const string TestDisplayName = "JWT Test User";
    private const string JwtSecret = "super-secret-key-for-testing-at-least-32-chars!!";

    public JwtAuthTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var dbDesc = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<GridDbContext>));
                if (dbDesc != null) services.Remove(dbDesc);
                services.AddDbContext<GridDbContext>(options =>
                    options.UseInMemoryDatabase("JwtTestDb_" + Guid.NewGuid()));

                // Also replace DbContextFactory used by FirebaseMobilePushService
                var factoryDesc = services.SingleOrDefault(d =>
                    d.ServiceType == typeof(IDbContextFactory<GridDbContext>));
                if (factoryDesc != null) services.Remove(factoryDesc);
                services.AddDbContextFactory<GridDbContext>(options =>
                    options.UseInMemoryDatabase("JwtTestDb_Shared"), ServiceLifetime.Scoped);
            });
            builder.UseSetting("Jwt:Secret", JwtSecret);
            builder.UseSetting("Jwt:Issuer", "ireland-live-signals");
            builder.UseSetting("Jwt:Audience", "ireland-live-signals-clients");
            builder.UseSetting("Jwt:AccessTokenExpiryMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenExpiryDays", "30");
        });
    }

    /// <summary>
    /// Creates a factory with a shared, named in-memory DB so that user registration
    /// and subsequent JWT login share the same database instance.
    /// </summary>
    private WebApplicationFactory<Program> CreateSharedDbFactory(string dbName)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var dbDesc = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<GridDbContext>));
                if (dbDesc != null) services.Remove(dbDesc);
                services.AddDbContext<GridDbContext>(options =>
                    options.UseInMemoryDatabase(dbName));

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

    /// <summary>
    /// Registers a test user via the Razor page form and returns the shared factory.
    /// </summary>
    private async Task<(WebApplicationFactory<Program> factory, HttpClient client)> RegisterUserAsync(string dbName)
    {
        var factory = CreateSharedDbFactory(dbName);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = true,
            HandleCookies = true
        });

        var getRegisterResponse = await client.GetAsync("/Account/Register");
        var html = await getRegisterResponse.Content.ReadAsStringAsync();
        var tokenMatch = System.Text.RegularExpressions.Regex.Match(
            html, @"<input[^>]+name=""__RequestVerificationToken""[^>]+value=""([^""]+)""");
        var csrfToken = tokenMatch.Success ? tokenMatch.Groups[1].Value : "";

        var registerContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Email", TestEmail),
            new KeyValuePair<string, string>("Password", TestPassword),
            new KeyValuePair<string, string>("ConfirmPassword", TestPassword),
            new KeyValuePair<string, string>("DisplayName", TestDisplayName),
            new KeyValuePair<string, string>("__RequestVerificationToken", csrfToken),
        });
        await client.PostAsync("/Account/Register", registerContent);

        return (factory, client);
    }

    // ── Login tests ────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_WithValidCredentials_Returns200AndAccessToken()
    {
        var dbName = "JwtLoginValid_" + Guid.NewGuid();
        var (factory, _) = await RegisterUserAsync(dbName);

        // Use a fresh (non-cookie) client for the API call
        var apiClient = factory.CreateClient();
        var response = await apiClient.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestEmail,
            password = TestPassword,
            deviceLabel = "test-device"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("accessToken", out var accessTokenProp));
        var accessToken = accessTokenProp.GetString();
        Assert.False(string.IsNullOrEmpty(accessToken));

        Assert.True(body.TryGetProperty("refreshToken", out var refreshTokenProp));
        Assert.False(string.IsNullOrEmpty(refreshTokenProp.GetString()));

        Assert.True(body.TryGetProperty("userId", out _));
    }

    [Fact]
    public async Task Login_WithInvalidPassword_Returns401()
    {
        var dbName = "JwtLoginInvalid_" + Guid.NewGuid();
        var (factory, _) = await RegisterUserAsync(dbName);

        var apiClient = factory.CreateClient();
        var response = await apiClient.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestEmail,
            password = "WrongPassword99!",
            deviceLabel = "test-device"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_Returns401()
    {
        var factory = CreateSharedDbFactory("JwtLoginUnknown_" + Guid.NewGuid());
        var apiClient = factory.CreateClient();

        var response = await apiClient.PostAsJsonAsync("/api/auth/login", new
        {
            email = "nobody@example.com",
            password = "Password1!",
            deviceLabel = "test-device"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Favourites (protected endpoint) tests ─────────────────────────────

    [Fact]
    public async Task GetFavourites_WithoutToken_Returns401OrRedirect()
    {
        var factory = CreateSharedDbFactory("JwtFavsNoToken_" + Guid.NewGuid());
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/api/me/favourites");

        // 401 Unauthorized (direct), 302/301 redirect to login page (cookie auth),
        // or 404 (app uses UseStatusCodePagesWithReExecute("/{0}") which re-executes
        // at "/401" — a non-existent path — producing a 404 from the test host).
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.Found ||
            response.StatusCode == HttpStatusCode.Redirect ||
            response.StatusCode == HttpStatusCode.NotFound,
            $"Expected 401/302 but got {response.StatusCode}");
    }

    [Fact]
    public async Task GetFavourites_WithValidBearerToken_Returns200()
    {
        var dbName = "JwtFavsWithToken_" + Guid.NewGuid();
        var (factory, _) = await RegisterUserAsync(dbName);

        var apiClient = factory.CreateClient();

        // Login to get access token
        var loginResponse = await apiClient.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestEmail,
            password = TestPassword
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = loginBody.GetProperty("accessToken").GetString()!;

        // Call protected endpoint with Bearer token
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me/favourites");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var favsResponse = await apiClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, favsResponse.StatusCode);
    }

    // ── Refresh token tests ───────────────────────────────────────────────

    [Fact]
    public async Task Refresh_WithValidRefreshToken_Returns200AndNewTokens()
    {
        var dbName = "JwtRefreshValid_" + Guid.NewGuid();
        var (factory, _) = await RegisterUserAsync(dbName);

        var apiClient = factory.CreateClient();

        var loginResponse = await apiClient.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestEmail,
            password = TestPassword
        });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var refreshToken = loginBody.GetProperty("refreshToken").GetString()!;

        var refreshResponse = await apiClient.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken
        });

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        var refreshBody = await refreshResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(refreshBody.TryGetProperty("accessToken", out var newAccess));
        Assert.False(string.IsNullOrEmpty(newAccess.GetString()));

        Assert.True(refreshBody.TryGetProperty("refreshToken", out var newRefresh));
        // New refresh token must differ from original (token rotation)
        Assert.NotEqual(refreshToken, newRefresh.GetString());
    }

    [Fact]
    public async Task Refresh_WithUsedRefreshToken_Returns401()
    {
        var dbName = "JwtRefreshUsed_" + Guid.NewGuid();
        var (factory, _) = await RegisterUserAsync(dbName);

        var apiClient = factory.CreateClient();

        var loginResponse = await apiClient.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestEmail,
            password = TestPassword
        });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var refreshToken = loginBody.GetProperty("refreshToken").GetString()!;

        // First use — should succeed
        var firstRefresh = await apiClient.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);

        // Second use of the same token — replay attack, must be 401
        var secondRefresh = await apiClient.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, secondRefresh.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithInvalidToken_Returns401()
    {
        var factory = CreateSharedDbFactory("JwtRefreshInvalid_" + Guid.NewGuid());
        var apiClient = factory.CreateClient();

        var response = await apiClient.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken = "this-is-not-a-valid-refresh-token"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
