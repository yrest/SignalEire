using CommunityToolkit.Maui;
using IrelandLiveSignals.MauiClient.Models;
using IrelandLiveSignals.MauiClient.Services;
using IrelandLiveSignals.MauiClient.ViewModels;
using IrelandLiveSignals.MauiClient.Views;
using Maui.GoogleMaps.Hosting;
using Microsoft.Extensions.Logging;

namespace IrelandLiveSignals.MauiClient;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseGoogleMaps(Secrets.GoogleMapsApiKey)
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var services = builder.Services;

        // ── Core services ────────────────────────────────────────────────────

        // LocalCacheService is IAsyncDisposable — register as singleton
        services.AddSingleton<ILocalCacheService, LocalCacheService>();

        // AuthService depends on ISignalEireApiClient (registered below)
        services.AddSingleton<IAuthService, AuthService>();

        // TokenRefreshHandler needs IAuthService
        services.AddTransient<TokenRefreshHandler>();

        // HttpClient for API — wired through the refresh handler
        services.AddHttpClient<ISignalEireApiClient, SignalEireApiClient>(client =>
        {
            client.BaseAddress = new Uri(AppConfig.ApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddHttpMessageHandler<TokenRefreshHandler>();

        // ── ViewModels ───────────────────────────────────────────────────────

        services.AddTransient<LoginViewModel>();
        services.AddTransient<GridViewModel>();
        services.AddTransient<TransitSearchViewModel>();
        services.AddTransient<StopBoardViewModel>();
        services.AddTransient<FavouritesViewModel>();
        services.AddTransient<MapViewModel>();
        services.AddTransient<AccountViewModel>();
        services.AddTransient<AlertRulesViewModel>();

        // ── Views ────────────────────────────────────────────────────────────

        services.AddTransient<LoginPage>();
        services.AddTransient<GridPage>();
        services.AddTransient<TransitSearchPage>();
        services.AddTransient<StopBoardPage>();
        services.AddTransient<FavouritesPage>();
        services.AddTransient<MapPage>();
        services.AddTransient<AccountPage>();
        services.AddTransient<AlertRulesPage>();

        // Shell & App
        services.AddSingleton<AppShell>();
        services.AddSingleton<App>();

        return builder.Build();
    }
}
