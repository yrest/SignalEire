using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IrelandLiveSignals.MauiClient.Models;
using IrelandLiveSignals.MauiClient.Services;

namespace IrelandLiveSignals.MauiClient.ViewModels;

public partial class GridViewModel : ObservableObject, IDisposable
{
    private const string CacheKey = "grid_current";

    private readonly ISignalEireApiClient _apiClient;
    private readonly ILocalCacheService _cache;
    private readonly IDispatcher _dispatcher;
    private readonly Timer _refreshTimer;

    [ObservableProperty]
    private GridReadingResponse? _currentReading;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isOffline;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _lastUpdatedText = string.Empty;

    public GridViewModel(ISignalEireApiClient apiClient, ILocalCacheService cache, IDispatcher dispatcher)
    {
        _apiClient = apiClient;
        _cache = cache;
        _dispatcher = dispatcher;

        // Auto-refresh every 5 minutes
        _refreshTimer = new Timer(
            callback: _ => _dispatcher.Dispatch(() => _ = RefreshAsync()),
            state: null,
            dueTime: TimeSpan.FromMinutes(5),
            period: TimeSpan.FromMinutes(5));
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            // Try cache first
            var cached = await _cache.GetAsync<GridReadingResponse>(CacheKey);
            if (cached is not null)
            {
                CurrentReading = cached;
                IsOffline = false;
                UpdateLastUpdatedText();
            }

            // Fetch fresh data
            var fresh = await _apiClient.GetCurrentGridAsync();
            if (fresh is not null)
            {
                CurrentReading = fresh;
                IsOffline = false;
                await _cache.SetAsync(CacheKey, fresh, TimeSpan.FromMinutes(AppConfig.GridCacheMinutes));
                UpdateLastUpdatedText();
            }
            else if (cached is null)
            {
                IsOffline = true;
                ErrorMessage = "Unable to load grid data. Check your connection.";
            }
            else
            {
                IsOffline = true;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateLastUpdatedText()
    {
        LastUpdatedText = $"Updated {DateTime.Now:HH:mm}";
    }

    public void Dispose()
    {
        _refreshTimer.Dispose();
    }
}
