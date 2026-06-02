using IrelandLiveSignals.MauiClient.Models;

namespace IrelandLiveSignals.MauiClient.Services;

public interface ISignalEireApiClient
{
    Task<GridReadingResponse?> GetCurrentGridAsync();
    Task<List<TransitStopDto>?> SearchStopsAsync(string q);
    Task<List<TransitStopDto>?> GetNearbyStopsAsync(double lat, double lon);
    Task<StopBoardResponse?> GetStopArrivalsAsync(string stopId);
    Task<List<VehicleDto>?> GetVehiclesForRouteAsync(string routeId);
    Task<List<FavouriteStopDto>?> GetFavouritesAsync();
    Task<FavouriteStopDto?> AddFavouriteAsync(string stopId, string? label);
    Task<bool> RemoveFavouriteAsync(string stopId);
    Task<List<AlertRuleDto>?> GetAlertRulesAsync();
    Task<bool> DeleteAlertRuleAsync(string id);
    Task<bool> RegisterDeviceTokenAsync(string token, string platform);
    Task<LoginResponse?> LoginAsync(string email, string password, string? deviceLabel);
    Task<LoginResponse?> RefreshTokenAsync(string refreshToken);
    Task LogoutAsync(string? refreshToken, string? platform);
}
