using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IrelandLiveSignals.MauiClient.Models;
using IrelandLiveSignals.MauiClient.Services;

namespace IrelandLiveSignals.MauiClient.ViewModels;

public partial class TransitSearchViewModel : ObservableObject
{
    private readonly ISignalEireApiClient _apiClient;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private ObservableCollection<TransitStopDto> _results = [];

    [ObservableProperty]
    private bool _isSearching;

    public TransitSearchViewModel(ISignalEireApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (value.Length >= 2)
        {
            _ = SearchAsync();
        }
        else
        {
            Results.Clear();
        }
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        if (SearchQuery.Length < 2)
            return;

        // Cancel any pending search
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        // Small debounce
        try
        {
            await Task.Delay(300, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
            return;

        IsSearching = true;

        try
        {
            var stops = await _apiClient.SearchStopsAsync(SearchQuery);
            if (token.IsCancellationRequested)
                return;

            Results.Clear();
            if (stops is not null)
            {
                foreach (var stop in stops)
                    Results.Add(stop);
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsSearching = false;
        }
    }

    [RelayCommand]
    public async Task UseLocationAsync()
    {
        try
        {
            var location = await Geolocation.Default.GetLastKnownLocationAsync()
                ?? await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium));

            if (location is null)
                return;

            IsSearching = true;

            try
            {
                var stops = await _apiClient.GetNearbyStopsAsync(location.Latitude, location.Longitude);
                Results.Clear();
                if (stops is not null)
                {
                    foreach (var stop in stops)
                        Results.Add(stop);
                }
            }
            finally
            {
                IsSearching = false;
            }
        }
        catch
        {
            // Location permission denied or unavailable — ignore
        }
    }

    [RelayCommand]
    public async Task GoToStopAsync(TransitStopDto stop)
    {
        var parameters = new Dictionary<string, object>
        {
            { "Stop", stop }
        };
        await Shell.Current.GoToAsync("Transit/StopBoard", parameters);
    }
}
