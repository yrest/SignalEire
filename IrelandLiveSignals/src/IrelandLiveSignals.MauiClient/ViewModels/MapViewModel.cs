using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IrelandLiveSignals.MauiClient.Services;
using Maui.GoogleMaps;

namespace IrelandLiveSignals.MauiClient.ViewModels;

public partial class MapViewModel : ObservableObject, IDisposable
{
    private readonly ISignalEireApiClient _apiClient;
    private readonly IDispatcher _dispatcher;
    private readonly Timer _vehicleTimer;
    private string? _currentRouteId;

    [ObservableProperty]
    private ObservableCollection<Pin> _stopPins = [];

    [ObservableProperty]
    private ObservableCollection<Pin> _vehiclePins = [];

    public MapViewModel(ISignalEireApiClient apiClient, IDispatcher dispatcher)
    {
        _apiClient = apiClient;
        _dispatcher = dispatcher;

        _vehicleTimer = new Timer(
            callback: _ => _dispatcher.Dispatch(() => _ = RefreshVehiclesAsync()),
            state: null,
            dueTime: TimeSpan.FromSeconds(AppConfig.VehicleRefreshSeconds),
            period: TimeSpan.FromSeconds(AppConfig.VehicleRefreshSeconds));
    }

    [RelayCommand]
    public async Task LoadStopsAsync()
    {
        try
        {
            var location = await Geolocation.Default.GetLastKnownLocationAsync()
                ?? await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium));

            if (location is null)
                return;

            var stops = await _apiClient.GetNearbyStopsAsync(location.Latitude, location.Longitude);
            if (stops is null)
                return;

            StopPins.Clear();
            foreach (var stop in stops)
            {
                var pin = new Pin
                {
                    Label = stop.StopName,
                    Position = new Position(stop.StopLat, stop.StopLon),
                    Type = PinType.Place
                };
                StopPins.Add(pin);
            }
        }
        catch
        {
            // Location unavailable — ignore
        }
    }

    [RelayCommand]
    public async Task RefreshVehiclesAsync()
    {
        if (_currentRouteId is null)
            return;

        var vehicles = await _apiClient.GetVehiclesForRouteAsync(_currentRouteId);
        if (vehicles is null)
            return;

        VehiclePins.Clear();
        foreach (var vehicle in vehicles)
        {
            var pin = new Pin
            {
                Label = $"Route {vehicle.RouteId}",
                Position = new Position(vehicle.Lat, vehicle.Lon),
                Type = PinType.Generic
            };
            VehiclePins.Add(pin);
        }
    }

    public void SetRouteFilter(string routeId)
    {
        _currentRouteId = routeId;
        _ = RefreshVehiclesAsync();
    }

    public void Dispose()
    {
        _vehicleTimer.Dispose();
    }
}
