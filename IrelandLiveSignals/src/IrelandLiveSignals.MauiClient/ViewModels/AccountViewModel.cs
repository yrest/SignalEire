using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IrelandLiveSignals.MauiClient.Services;

namespace IrelandLiveSignals.MauiClient.ViewModels;

public partial class AccountViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly ISignalEireApiClient _apiClient;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private bool _pushNotificationsEnabled;

    public AccountViewModel(IAuthService authService, ISignalEireApiClient apiClient)
    {
        _authService = authService;
        _apiClient = apiClient;

        _authService.AuthStateChanged += OnAuthStateChanged;
        Refresh();
    }

    private void OnAuthStateChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    private void Refresh()
    {
        IsAuthenticated = _authService.IsAuthenticated;
        DisplayName = _authService.DisplayName ?? string.Empty;
    }

    [RelayCommand]
    public async Task LogoutAsync()
    {
        await _authService.LogoutAsync();
        await Shell.Current.GoToAsync("//Grid");
    }

    [RelayCommand]
    public async Task GoToAlertsAsync()
    {
        await Shell.Current.GoToAsync("Account/Alerts");
    }

    [RelayCommand]
    public async Task GoToRegisterAsync()
    {
        await Shell.Current.GoToAsync("//Register");
    }

    [RelayCommand]
    public async Task GoToLoginAsync()
    {
        await Shell.Current.GoToAsync("//Login");
    }
}
