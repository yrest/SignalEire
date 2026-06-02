using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IrelandLiveSignals.MauiClient.Models;
using IrelandLiveSignals.MauiClient.Services;

namespace IrelandLiveSignals.MauiClient.ViewModels;

public partial class FavouritesViewModel : ObservableObject
{
    private readonly ISignalEireApiClient _apiClient;
    private readonly ILocalCacheService _cache;
    private readonly IAuthService _authService;

    [ObservableProperty]
    private ObservableCollection<FavouriteStopDto> _favourites = [];

    [ObservableProperty]
    private bool _showLoginPrompt;

    [ObservableProperty]
    private bool _isLoading;

    public FavouritesViewModel(
        ISignalEireApiClient apiClient,
        ILocalCacheService cache,
        IAuthService authService)
    {
        _apiClient = apiClient;
        _cache = cache;
        _authService = authService;

        _authService.AuthStateChanged += OnAuthStateChanged;
        ShowLoginPrompt = !_authService.IsAuthenticated;
    }

    private void OnAuthStateChanged(object? sender, EventArgs e)
    {
        ShowLoginPrompt = !_authService.IsAuthenticated;
        if (_authService.IsAuthenticated)
            _ = LoadAsync();
        else
            Favourites.Clear();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        ShowLoginPrompt = !_authService.IsAuthenticated;

        if (!_authService.IsAuthenticated)
            return;

        const string cacheKey = "favourites";
        IsLoading = true;

        try
        {
            var cached = await _cache.GetAsync<List<FavouriteStopDto>>(cacheKey);
            if (cached is not null)
                UpdateFavourites(cached);

            var fresh = await _apiClient.GetFavouritesAsync();
            if (fresh is not null)
            {
                UpdateFavourites(fresh);
                await _cache.SetAsync(cacheKey, fresh, TimeSpan.FromMinutes(AppConfig.FavouritesCacheMinutes));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RemoveFavouriteAsync(FavouriteStopDto favourite)
    {
        var removed = await _apiClient.RemoveFavouriteAsync(favourite.StopId);
        if (removed)
        {
            Favourites.Remove(favourite);
            // Invalidate cache
            await _cache.RemoveAsync("favourites");
        }
    }

    [RelayCommand]
    public async Task GoToStopAsync(FavouriteStopDto favourite)
    {
        var stop = new TransitStopDto(
            StopId: favourite.StopId,
            StopName: favourite.DisplayLabel,
            StopCode: string.Empty,
            StopLat: 0,
            StopLon: 0,
            DistanceMeters: 0);

        var parameters = new Dictionary<string, object>
        {
            { "Stop", stop }
        };
        await Shell.Current.GoToAsync("Transit/StopBoard", parameters);
    }

    private void UpdateFavourites(List<FavouriteStopDto> items)
    {
        Favourites.Clear();
        foreach (var item in items.OrderBy(f => f.SortOrder))
            Favourites.Add(item);
    }
}
