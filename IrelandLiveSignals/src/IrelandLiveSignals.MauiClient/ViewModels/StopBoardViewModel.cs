using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IrelandLiveSignals.MauiClient.Models;
using IrelandLiveSignals.MauiClient.Services;

namespace IrelandLiveSignals.MauiClient.ViewModels;

[QueryProperty(nameof(Stop), "Stop")]
public partial class StopBoardViewModel : ObservableObject, IDisposable
{
    private readonly ISignalEireApiClient _apiClient;
    private readonly ILocalCacheService _cache;
    private readonly IAuthService _authService;
    private readonly IDispatcher _dispatcher;
    private readonly Timer _refreshTimer;

    [ObservableProperty]
    private TransitStopDto? _stop;

    [ObservableProperty]
    private ObservableCollection<ArrivalDto> _arrivals = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isOffline;

    [ObservableProperty]
    private bool _isFavourite;

    [ObservableProperty]
    private string _stopName = string.Empty;

    public StopBoardViewModel(
        ISignalEireApiClient apiClient,
        ILocalCacheService cache,
        IAuthService authService,
        IDispatcher dispatcher)
    {
        _apiClient = apiClient;
        _cache = cache;
        _authService = authService;
        _dispatcher = dispatcher;

        // Auto-refresh every 30 seconds
        _refreshTimer = new Timer(
            callback: _ => _dispatcher.Dispatch(() => _ = RefreshAsync()),
            state: null,
            dueTime: TimeSpan.FromSeconds(30),
            period: TimeSpan.FromSeconds(30));
    }

    partial void OnStopChanged(TransitStopDto? value)
    {
        if (value is not null)
        {
            StopName = value.StopName;
            _ = RefreshAsync();
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (Stop is null)
            return;

        var cacheKey = $"stopboard_{Stop.StopId}";
        IsLoading = true;

        try
        {
            // Try cache first
            var cached = await _cache.GetAsync<StopBoardResponse>(cacheKey);
            if (cached is not null)
            {
                UpdateArrivals(cached);
                IsOffline = false;
            }

            // Fetch fresh data
            var fresh = await _apiClient.GetStopArrivalsAsync(Stop.StopId);
            if (fresh is not null)
            {
                UpdateArrivals(fresh);
                IsOffline = false;
                await _cache.SetAsync(cacheKey, fresh, TimeSpan.FromMinutes(AppConfig.StopBoardCacheMinutes));
            }
            else if (cached is null)
            {
                IsOffline = true;
            }
            else
            {
                IsOffline = true;
            }

            // Check favourite status
            if (_authService.IsAuthenticated)
            {
                var favs = await _apiClient.GetFavouritesAsync();
                IsFavourite = favs?.Any(f => f.StopId == Stop.StopId) ?? false;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ToggleFavouriteAsync()
    {
        if (Stop is null || !_authService.IsAuthenticated)
            return;

        if (IsFavourite)
        {
            var removed = await _apiClient.RemoveFavouriteAsync(Stop.StopId);
            if (removed)
                IsFavourite = false;
        }
        else
        {
            var added = await _apiClient.AddFavouriteAsync(Stop.StopId, Stop.StopName);
            if (added is not null)
                IsFavourite = true;
        }
    }

    private void UpdateArrivals(StopBoardResponse board)
    {
        Arrivals.Clear();
        foreach (var arrival in board.Arrivals)
            Arrivals.Add(arrival);
    }

    public void Dispose()
    {
        _refreshTimer.Dispose();
    }
}
