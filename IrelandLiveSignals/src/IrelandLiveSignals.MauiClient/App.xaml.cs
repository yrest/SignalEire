using IrelandLiveSignals.MauiClient.Services;

namespace IrelandLiveSignals.MauiClient;

public partial class App : Application
{
    private readonly IAuthService _authService;
    private readonly ISignalEireApiClient _apiClient;

    public App(IAuthService authService, ISignalEireApiClient apiClient, AppShell shell)
    {
        InitializeComponent();

        _authService = authService;
        _apiClient = apiClient;

        // Listen for forced logout (e.g. refresh token expired)
        _authService.AuthStateChanged += OnAuthStateChanged;

        MainPage = shell;
    }

    protected override async void OnStart()
    {
        base.OnStart();

        // Attempt silent refresh if token is expired
        if (!_authService.IsAuthenticated)
            await _authService.RefreshAsync();

        // Initialise push notifications (non-blocking)
        _ = PushNotificationService.InitialiseAsync(_apiClient);
    }

    private void OnAuthStateChanged(object? sender, EventArgs e)
    {
        if (!_authService.IsAuthenticated)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Shell.Current.GoToAsync("//Grid");
            });
        }
    }
}
