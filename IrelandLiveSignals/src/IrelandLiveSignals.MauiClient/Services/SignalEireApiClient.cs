using System.Net.Http.Json;
using System.Text.Json;
using IrelandLiveSignals.MauiClient.Models;

namespace IrelandLiveSignals.MauiClient.Services;

public sealed class SignalEireApiClient : ISignalEireApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;

    public SignalEireApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<GridReadingResponse?> GetCurrentGridAsync()
        => await GetAsync<GridReadingResponse>("api/grid/current");

    public async Task<List<TransitStopDto>?> SearchStopsAsync(string q)
        => await GetAsync<List<TransitStopDto>>($"api/transit/stops/search?q={Uri.EscapeDataString(q)}");

    public async Task<List<TransitStopDto>?> GetNearbyStopsAsync(double lat, double lon)
        => await GetAsync<List<TransitStopDto>>($"api/transit/stops/nearby?lat={lat}&lon={lon}");

    public async Task<StopBoardResponse?> GetStopArrivalsAsync(string stopId)
        => await GetAsync<StopBoardResponse>($"api/transit/stops/{Uri.EscapeDataString(stopId)}/arrivals");

    public async Task<List<VehicleDto>?> GetVehiclesForRouteAsync(string routeId)
        => await GetAsync<List<VehicleDto>>($"api/transit/routes/{Uri.EscapeDataString(routeId)}/vehicles");

    public async Task<List<FavouriteStopDto>?> GetFavouritesAsync()
        => await GetAsync<List<FavouriteStopDto>>("api/me/favourites");

    public async Task<FavouriteStopDto?> AddFavouriteAsync(string stopId, string? label)
    {
        try
        {
            var payload = new { StopId = stopId, DisplayLabel = label };
            var response = await _http.PostAsJsonAsync("api/me/favourites", payload, JsonOptions);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadFromJsonAsync<FavouriteStopDto>(JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> RemoveFavouriteAsync(string stopId)
    {
        try
        {
            var response = await _http.DeleteAsync($"api/me/favourites/{Uri.EscapeDataString(stopId)}");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<AlertRuleDto>?> GetAlertRulesAsync()
        => await GetAsync<List<AlertRuleDto>>("api/me/alerts");

    public async Task<bool> DeleteAlertRuleAsync(string id)
    {
        try
        {
            var response = await _http.DeleteAsync($"api/me/alerts/{Uri.EscapeDataString(id)}");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RegisterDeviceTokenAsync(string token, string platform)
    {
        try
        {
            var payload = new { Token = token, Platform = platform };
            var response = await _http.PostAsJsonAsync("api/push/device-token", payload, JsonOptions);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<LoginResponse?> LoginAsync(string email, string password, string? deviceLabel)
    {
        try
        {
            var payload = new { Email = email, Password = password, DeviceLabel = deviceLabel };
            var response = await _http.PostAsJsonAsync("api/auth/login", payload, JsonOptions);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<LoginResponse?> RefreshTokenAsync(string refreshToken)
    {
        try
        {
            var payload = new { RefreshToken = refreshToken };
            var response = await _http.PostAsJsonAsync("api/auth/refresh", payload, JsonOptions);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task LogoutAsync(string? refreshToken, string? platform)
    {
        try
        {
            var payload = new { RefreshToken = refreshToken, Platform = platform };
            await _http.PostAsJsonAsync("api/auth/logout", payload, JsonOptions);
        }
        catch
        {
            // swallow — logout is best-effort
        }
    }

    private async Task<T?> GetAsync<T>(string relativeUrl) where T : class
    {
        try
        {
            var response = await _http.GetAsync(relativeUrl);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
