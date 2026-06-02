# SignalEire — Phase 7 Build Brief
**For:** Claude Code  
**Prerequisite:** Phase 6 complete; Firebase project created; Apple Developer account active  
**Scope:** .NET MAUI native client (Android + iOS) + JWT auth on API + FCM/APNs push  
**Character:** New MAUI project in the existing solution. API changes are additive only — no existing behaviour modified.

---

## Pre-conditions — do not start without all of these

| Item | Where to get it | Used for |
|---|---|---|
| `google-services.json` | Firebase Console → Project Settings → Android app | FCM on Android |
| `GoogleService-Info.plist` | Firebase Console → Project Settings → iOS app | FCM-mediated APNs on iOS |
| Firebase service account JSON | Firebase Console → Project Settings → Service accounts | Server-side push sending |
| APNs auth key (`.p8`) uploaded to Firebase | Firebase Console → Project Settings → Cloud Messaging → iOS config | iOS push delivery |
| Google Maps mobile API key | Google Cloud Console → Credentials (restrict to Android SHA-1 + iOS bundle ID) | Maps SDK on device |
| Apple Developer account with push capability | developer.apple.com | APNs entitlement |

If any of these are missing, stop and tell the user before writing a line of code.

---

## What Phase 7 adds

### API changes (backend, additive only)
1. JWT Bearer authentication alongside existing cookie auth
2. `POST /api/auth/login` — returns access token + refresh token
3. `POST /api/auth/refresh` — rotates refresh token
4. `POST /api/auth/logout` — revokes refresh token
5. `POST /api/push/device-token` — registers FCM/APNs device token
6. `DELETE /api/push/device-token` — unregisters on logout
7. Firebase Admin SDK wired into alert delivery

### MAUI client (new project)
1. Project: `IrelandLiveSignals.MauiClient` in existing solution
2. Targets: `net8.0-android`, `net8.0-ios`
3. Shell navigation with five tabs
4. Full MVVM via CommunityToolkit.Mvvm
5. JWT auth with automatic token refresh
6. All core screens: Grid, Transit search, Stop board, Favourites, Map, Account, Alerts
7. Google Maps with vehicle and stop markers
8. FCM (Android) + APNs via Firebase (iOS) push notifications
9. Offline cache for last-fetched data

---

## Part 1 — API changes

### New NuGet packages (Infrastructure project)

```
Microsoft.AspNetCore.Authentication.JwtBearer
FirebaseAdmin
System.IdentityModel.Tokens.Jwt
```

### JWT configuration

```json
// appsettings.json additions
"Jwt": {
  "Secret": "",
  "Issuer": "ireland-live-signals",
  "Audience": "ireland-live-signals-clients",
  "AccessTokenExpiryMinutes": 15,
  "RefreshTokenExpiryDays": 30
}
```

`Jwt:Secret` is a secret — add to `/etc/ireland-live-signals/environment`, minimum 64 characters.

### Dual auth scheme setup (Program.cs)

Keep cookie auth for Razor Pages unchanged. Add JWT Bearer as a second scheme for API routes only.

```csharp
builder.Services.AddAuthentication()
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = config["Jwt:Issuer"],
            ValidAudience            = config["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(config["Jwt:Secret"]!)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
```

Apply JWT scheme to all `/api/*` route groups:

```csharp
var api = app.MapGroup("/api")
    .RequireAuthorization(new AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build());
```

Unauthenticated endpoints (`/api/grid/current`, `/api/grid/health`, transit search, stop arrivals) must be explicitly exempted with `.AllowAnonymous()`.

Razor Pages continue to use the existing cookie scheme — no changes to any `.cshtml` files.

### New domain models

```csharp
public record UserRefreshToken
{
    public string Id { get; init; }
    public string UserId { get; init; }
    public string TokenHash { get; init; }       // SHA-256 of plaintext token
    public string DeviceLabel { get; init; }     // "Android" | "iOS" | "Web"
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? UsedAtUtc { get; set; }
    public bool IsRevoked { get; set; }
}

public record DeviceToken
{
    public string Id { get; init; }
    public string UserId { get; init; }
    public string Token { get; init; }           // FCM registration token
    public string Platform { get; init; }        // "android" | "ios"
    public DateTimeOffset RegisteredAtUtc { get; init; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
}
```

New EF Core tables: `UserRefreshTokens`, `DeviceTokens`.

### Auth endpoints

**POST /api/auth/login**

```json
// Request
{ "email": "user@example.com", "password": "..." }

// Response 200
{
  "accessToken": "eyJ...",
  "refreshToken": "opaque-random-string",
  "expiresAt": "2026-06-01T14:15:00Z",
  "userId": "abc123",
  "displayName": "Yury"
}

// Response 401: { "error": "Invalid credentials" }
```

Generate access token as a signed JWT. Generate refresh token as `Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))`. Store SHA-256 hash of refresh token in `UserRefreshTokens` — never store the plaintext.

**POST /api/auth/refresh**

```json
// Request
{ "refreshToken": "opaque-random-string" }

// Response 200: same shape as login response, new tokens
// Response 401: { "error": "Invalid or expired refresh token" }
```

Refresh tokens are single-use. On use: mark old token `IsRevoked = true`, issue new access + refresh token pair. If an already-used token is presented (replay attack), revoke all tokens for that user.

**POST /api/auth/logout**

Requires Bearer token. Revokes the refresh token associated with the device label in the request body. Deletes the `DeviceToken` entry for this device.

**POST /api/push/device-token**

Requires Bearer token.

```json
{ "token": "fcm-device-token", "platform": "android" }
```

Upserts a `DeviceToken` record for this user. If the same FCM token already exists for a different user (device was re-registered), remove the old association.

**DELETE /api/push/device-token**

Requires Bearer token. Removes `DeviceToken` for the current user + platform.

### Firebase Admin SDK setup

Firebase service account JSON file path in environment:

```bash
Firebase__ServiceAccountPath=/etc/ireland-live-signals/firebase-service-account.json
```

Initialise in `Program.cs`:

```csharp
var serviceAccountPath = config["Firebase:ServiceAccountPath"];
if (!string.IsNullOrEmpty(serviceAccountPath) && File.Exists(serviceAccountPath))
{
    FirebaseApp.Create(new AppOptions
    {
        Credential = GoogleCredential.FromFile(serviceAccountPath)
    });
}
```

### IMobilePushService

```csharp
public interface IMobilePushService
{
    Task SendAsync(string userId, string title, string body, string url, CancellationToken ct);
}
```

Implementation (`FirebaseMobilePushService`):
1. Load all `DeviceToken` records for the user
2. For each token, send via `FirebaseMessaging.DefaultInstance.SendAsync(new Message { ... })`
3. On `FirebaseMessagingException` with `MessagingErrorCode.Unregistered` or `Invalid` → delete the stale token
4. Log failures, do not throw

Wire `IMobilePushService` into `IAlertDeliveryService` — called alongside email/Telegram/WebPush when the user has registered device tokens.

---

## Part 2 — MAUI project setup

### Add to solution

```
IrelandLiveSignals/
├── src/
│   ├── ... (existing projects)
│   └── IrelandLiveSignals.MauiClient/
│       ├── IrelandLiveSignals.MauiClient.csproj
│       ├── MauiProgram.cs
│       ├── AppShell.xaml
│       ├── Services/
│       ├── ViewModels/
│       ├── Views/
│       ├── Converters/
│       ├── Models/
│       └── Platforms/
│           ├── Android/
│           └── iOS/
```

### .csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0-android;net8.0-ios</TargetFrameworks>
    <UseMaui>true</UseMaui>
    <RootNamespace>IrelandLiveSignals.MauiClient</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ApplicationId>ie.irelandlivesignals.app</ApplicationId>
    <ApplicationVersion>1</ApplicationVersion>
    <ApplicationDisplayVersion>1.0</ApplicationDisplayVersion>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\IrelandLiveSignals.Core\IrelandLiveSignals.Core.csproj" />
  </ItemGroup>
</Project>
```

### NuGet packages

```
CommunityToolkit.Maui
CommunityToolkit.Mvvm
Maui.GoogleMaps
Plugin.Firebase.CloudMessaging
sqlite-net-pcl
SQLitePCLRaw.bundle_green
Microsoft.Extensions.Http
```

### MauiProgram.cs

```csharp
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseGoogleMaps(Secrets.GoogleMapsApiKey)  // platform-specific key
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        // Services
        builder.Services.AddSingleton<IAuthService, AuthService>();
        builder.Services.AddSingleton<ILocalCacheService, LocalCacheService>();
        builder.Services.AddSingleton<ISignalEireApiClient, SignalEireApiClient>();

        // ViewModels
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<GridViewModel>();
        builder.Services.AddTransient<TransitSearchViewModel>();
        builder.Services.AddTransient<StopBoardViewModel>();
        builder.Services.AddTransient<FavouritesViewModel>();
        builder.Services.AddTransient<MapViewModel>();
        builder.Services.AddTransient<AccountViewModel>();
        builder.Services.AddTransient<AlertRulesViewModel>();

        // HttpClient
        builder.Services.AddTransient<TokenRefreshHandler>();
        builder.Services.AddHttpClient<ISignalEireApiClient, SignalEireApiClient>(client =>
        {
            client.BaseAddress = new Uri(AppConfig.ApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
        })
        .AddHttpMessageHandler<TokenRefreshHandler>();

        return builder.Build();
    }
}
```

### AppConfig (non-secret configuration)

```csharp
// Models/AppConfig.cs
public static class AppConfig
{
    public const string ApiBaseUrl = "https://yourdomain.ie";
    public const int CacheExpiryMinutes = 5;
    public const int VehicleRefreshSeconds = 30;
}
```

### Secrets (not committed to repository)

Create `Platforms/Android/Secrets.cs` and `Platforms/iOS/Secrets.cs` — gitignored, set `GoogleMapsApiKey` per platform. Add `Secrets.cs` to `.gitignore`. Document the required values in `docs/mobile-setup.md`.

---

## Part 3 — Authentication

### IAuthService

```csharp
public interface IAuthService
{
    Task<bool> LoginAsync(string email, string password);
    Task<bool> RefreshAsync();
    Task LogoutAsync();
    bool IsAuthenticated { get; }
    string? UserId { get; }
    string? DisplayName { get; }
    event EventHandler AuthStateChanged;
}
```

### AuthService implementation

- Store access token in `SecureStorage.SetAsync("access_token", ...)`
- Store refresh token in `SecureStorage.SetAsync("refresh_token", ...)`
- Store expiry as `Preferences.Set("token_expiry", ...)`
- On app start: check for stored tokens; attempt silent refresh if access token is expired
- `IsAuthenticated` = valid (non-expired) access token exists in SecureStorage

### TokenRefreshHandler : DelegatingHandler

```csharp
protected override async Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken ct)
{
    // Attach current access token
    var token = await SecureStorage.GetAsync("access_token");
    if (token != null)
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

    var response = await base.SendAsync(request, ct);

    if (response.StatusCode == HttpStatusCode.Unauthorized)
    {
        // Try silent refresh
        var refreshed = await _authService.RefreshAsync();
        if (refreshed)
        {
            var newToken = await SecureStorage.GetAsync("access_token");
            var retryRequest = await CloneRequestAsync(request);
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
            return await base.SendAsync(retryRequest, ct);
        }
        // Refresh failed — signal logout
        _authService.TriggerLogout();
    }

    return response;
}
```

### LoginPage / LoginViewModel

LoginPage is shown as the root when no valid auth state exists. On successful login, navigate to AppShell.

```csharp
public partial class LoginViewModel : ObservableObject
{
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    [RelayCommand]
    private async Task LoginAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        var success = await _authService.LoginAsync(Email, Password);
        IsLoading = false;
        if (success)
            Application.Current!.MainPage = new AppShell();
        else
            ErrorMessage = "Invalid email or password.";
    }

    [RelayCommand]
    private async Task GoToRegisterAsync() => await Shell.Current.GoToAsync("//Register");
}
```

---

## Part 4 — Shell navigation

```xml
<!-- AppShell.xaml -->
<Shell>
    <TabBar>
        <ShellContent Title="Grid"    Icon="icon_grid.png"    Route="Grid"    ContentTemplate="{DataTemplate views:GridPage}" />
        <ShellContent Title="Transit" Icon="icon_transit.png" Route="Transit" ContentTemplate="{DataTemplate views:TransitSearchPage}" />
        <ShellContent Title="Saved"   Icon="icon_saved.png"   Route="Favourites" ContentTemplate="{DataTemplate views:FavouritesPage}" />
        <ShellContent Title="Account" Icon="icon_account.png" Route="Account" ContentTemplate="{DataTemplate views:AccountPage}" />
    </TabBar>
</Shell>
```

Named routes for push navigation (register in AppShell constructor):
```csharp
Routing.RegisterRoute("Transit/StopBoard", typeof(StopBoardPage));
Routing.RegisterRoute("Transit/Map",       typeof(MapPage));
Routing.RegisterRoute("Account/Alerts",    typeof(AlertRulesPage));
```

Navigation: `await Shell.Current.GoToAsync("Transit/StopBoard", new Dictionary<string, object> { ["StopId"] = stopId });`

---

## Part 5 — Screens

### GridPage / GridViewModel

Displays current grid state. Auto-refreshes every 5 minutes.

**Shows:**
- Large CO₂ intensity stat, coloured (green/amber/red matching backend thresholds)
- Renewable share % — circular progress gauge or bar
- Green score 0–100 — coloured badge
- System demand MW
- Wind / solar generation MW
- Status label and recommendation text
- Data freshness ("Updated 18s ago")
- Pull-to-refresh

```csharp
public partial class GridViewModel : ObservableObject
{
    [ObservableProperty] private GridReadingResponse? _currentReading;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isOffline;
    [ObservableProperty] private string? _errorMessage;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        var reading = await _apiClient.GetCurrentGridAsync();
        if (reading != null)
        {
            CurrentReading = reading;
            await _cache.SetAsync("grid_current", reading);
            IsOffline = false;
        }
        else
        {
            CurrentReading = await _cache.GetAsync<GridReadingResponse>("grid_current");
            IsOffline = true;
        }
        IsLoading = false;
    }
}
```

### TransitSearchPage / TransitSearchViewModel

Stop search entry point.

**Shows:**
- Search bar (search by name / stop code / route)
- "Use my location" button → calls `Geolocation.GetLastKnownLocationAsync()` → nearby stops
- Results list: stop name, stop code, distance (if nearby)
- Tap → navigate to StopBoardPage
- "View map" button → navigate to MapPage

```csharp
public partial class TransitSearchViewModel : ObservableObject
{
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private ObservableCollection<TransitStop> _results = [];
    [ObservableProperty] private bool _isSearching;

    partial void OnSearchQueryChanged(string value)
    {
        if (value.Length >= 2) SearchCommand.Execute(null);
        else Results.Clear();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        IsSearching = true;
        var stops = await _apiClient.SearchStopsAsync(SearchQuery);
        Results = new ObservableCollection<TransitStop>(stops ?? []);
        IsSearching = false;
    }

    [RelayCommand]
    private async Task UseLocationAsync()
    {
        var location = await Geolocation.GetLastKnownLocationAsync()
                    ?? await Geolocation.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium));
        if (location == null) return;
        IsSearching = true;
        var stops = await _apiClient.GetNearbyStopsAsync(location.Latitude, location.Longitude);
        Results = new ObservableCollection<TransitStop>(stops ?? []);
        IsSearching = false;
    }

    [RelayCommand]
    private async Task GoToStopAsync(TransitStop stop) =>
        await Shell.Current.GoToAsync("Transit/StopBoard",
            new Dictionary<string, object> { ["Stop"] = stop });
}
```

### StopBoardPage / StopBoardViewModel

Query parameter: `[QueryProperty(nameof(Stop), "Stop")]`

**Shows:**
- Stop name and code (header)
- List of next arrivals:
  - Route short name + destination
  - Predicted arrival time (or scheduled if no prediction)
  - Minutes until arrival (large, prominent)
  - Confidence % — coloured badge
  - Ghost risk label — coloured chip
  - GPS age if vehicle confirmed
- Empty state: "No arrivals in the next hour"
- "Add to favourites" toolbar button (heart icon, toggles)
- Auto-refresh every 30 seconds
- Pull-to-refresh

Arrival list item colouring:
- `vehicle_confirmed`: green left border
- `likely_active`: amber left border
- `timetable_only`, `stale_gps`: grey left border
- `implausible`, `cancelled`: red left border

### FavouritesPage / FavouritesViewModel

**Shows:**
- If not authenticated: "Sign in to save favourite stops" with login button
- If authenticated: list of saved stops, each showing stop name, user label, and a mini arrival count ("3 arrivals in next hour")
- Tap → StopBoardPage
- Swipe left to remove from favourites
- Empty state: "No saved stops. Search for a stop and tap the heart icon."
- Refresh on appear (not auto-refresh)

### MapPage / MapViewModel

**Shows:**
- Full-viewport `GoogleMap` control (`Maui.GoogleMaps`)
- Bus stop markers (blue pin cluster) — tap → bottom sheet showing stop name, "View arrivals" button
- Vehicle markers (custom bus icon, rotated to vehicle bearing) — tap → popup: route, GPS age, confidence badge
- "My location" button — centres map, shows location pin
- Vehicle markers auto-refresh every 30 seconds via timer
- Cluster stops at low zoom levels

```csharp
public partial class MapViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<Pin> _stopPins = [];
    [ObservableProperty] private ObservableCollection<Pin> _vehiclePins = [];

    // Called on page appear
    [RelayCommand]
    private async Task LoadStopsAsync()
    {
        // Load stops near map centre using GetNearbyStopsAsync
        // Convert to Pin with Label = stop name, Address = stop code
    }

    // Called every 30s
    [RelayCommand]
    private async Task RefreshVehiclesAsync()
    {
        // Fetch vehicles for visible routes
        // Update VehiclePins collection (do not clear and re-add — update in place to avoid flicker)
    }
}
```

### AccountPage / AccountViewModel

**Shows:**
- If not authenticated: login/register buttons, brief description of account benefits
- If authenticated:
  - Display name and email
  - "Notifications" row → toggle push notifications on/off
  - "Alert rules" row → navigate to AlertRulesPage
  - "My devices" row → list EV/appliance profiles
  - "Sign out" button

### AlertRulesPage / AlertRulesViewModel

Mirror of the web alert rules page.

**Shows:**
- List of user's alert rules: name, conditions summary, delivery mode badge, enabled toggle
- "Add rule" button → modal bottom sheet form
- Swipe left to delete
- Each rule card: tap → expand to show full condition detail

---

## Part 6 — Google Maps integration

### Configuration

Android (`Platforms/Android/MainApplication.cs`):
```csharp
[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership) { }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
```

`Platforms/Android/AndroidManifest.xml` — add inside `<application>`:
```xml
<meta-data android:name="com.google.android.geo.API_KEY"
           android:value="${MAPS_API_KEY}" />
```

iOS (`Platforms/iOS/AppDelegate.cs`):
```csharp
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    
    public override bool FinishedLaunching(UIApplication app, NSDictionary options)
    {
        GoogleMaps.MapServices.ProvideApiKey(Secrets.GoogleMapsApiKey);
        return base.FinishedLaunching(app, options);
    }
}
```

### Map XAML

```xml
<maps:GoogleMap x:Name="Map"
                InitialCameraUpdate="{maps:CameraUpdateFactory NewLatLngZoom={maps:LatLng 53.3, -8.0}, 7}"
                PinsSource="{Binding StopPins}"
                MyLocationEnabled="True"
                MyLocationButtonEnabled="True" />
```

### Custom vehicle marker

Rotate bus icon to vehicle bearing:
```csharp
var vehiclePin = new Pin
{
    Label        = $"Route {vehicle.RouteId}",
    Address      = $"GPS age: {vehicle.GpsAgeSeconds}s",
    Position     = new Position(vehicle.Lat, vehicle.Lon),
    Icon         = BitmapDescriptorFactory.FromBundle("bus_icon.png"),
    Rotation     = vehicle.Bearing ?? 0f,
    IsFlat       = true
};
```

Add `bus_icon.png` (24×24dp, top-pointing bus silhouette) to `Resources/Images`.

---

## Part 7 — Push notifications

### Plugin.Firebase setup

`Plugin.Firebase.CloudMessaging` handles FCM on Android and FCM-mediated APNs on iOS.

**Android** (`Platforms/Android/`):
- Add `google-services.json` to `Platforms/Android/` (Build Action: `GoogleServicesJson`)
- In `MainApplication.cs`: call `CrossFirebaseCloudMessaging.Current.RegisterForPushNotifications()`

**iOS** (`Platforms/iOS/`):
- Add `GoogleService-Info.plist` to `Platforms/iOS/` (Build Action: `BundleResource`)
- In `AppDelegate.cs`:
```csharp
public override bool FinishedLaunching(UIApplication app, NSDictionary options)
{
    Firebase.Core.App.Configure();
    // Request notification permission
    UNUserNotificationCenter.Current.RequestAuthorization(
        UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound | UNAuthorizationOptions.Badge,
        (granted, error) => { if (granted) InvokeOnMainThread(app.RegisterForRemoteNotifications); });
    return base.FinishedLaunching(app, options);
}
```

### PushNotificationService (MAUI)

```csharp
public class PushNotificationService
{
    public static async Task InitialiseAsync(ISignalEireApiClient apiClient)
    {
        CrossFirebaseCloudMessaging.Current.TokenChanged += async (_, token) =>
        {
            if (!string.IsNullOrEmpty(token))
                await apiClient.RegisterDeviceTokenAsync(token,
                    DeviceInfo.Platform == DevicePlatform.Android ? "android" : "ios");
        };

        await CrossFirebaseCloudMessaging.Current.CheckIfValidAsync();
        var token = await CrossFirebaseCloudMessaging.Current.GetTokenAsync();
        if (!string.IsNullOrEmpty(token))
            await apiClient.RegisterDeviceTokenAsync(token,
                DeviceInfo.Platform == DevicePlatform.Android ? "android" : "ios");
    }
}
```

Call `PushNotificationService.InitialiseAsync` after successful login.

### Notification tap handling

When user taps a push notification, navigate to the relevant page:

```csharp
CrossFirebaseCloudMessaging.Current.NotificationTapped += async (_, message) =>
{
    var url = message.Data.GetValueOrDefault("url", "/");
    if (url.Contains("/transit/stops/"))
    {
        var stopId = url.Split('/').Last();
        await Shell.Current.GoToAsync($"Transit/StopBoard?StopId={stopId}");
    }
    // etc.
};
```

The server-side `FirebaseMobilePushService` should include a `url` key in the notification data payload matching the web URL pattern.

---

## Part 8 — Offline cache

### ILocalCacheService

```csharp
public interface ILocalCacheService
{
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);
    Task<T?> GetAsync<T>(string key) where T : class;
    Task RemoveAsync(string key);
    Task ClearAsync();
}
```

Implementation: `sqlite-net-pcl` with a single `CacheEntry` table (key, json, expiresAt). Serialise/deserialise with `System.Text.Json`.

### What to cache

| Cache key | Content | Expiry |
|---|---|---|
| `grid_current` | Latest `GridReadingResponse` | 10 minutes |
| `stop_board_{stopId}` | Last arrivals for a stop | 2 minutes |
| `favourites` | User's favourite stops list | 1 hour |
| `alert_rules` | User's alert rules | 1 hour |
| `nearby_stops_{lat}_{lon}` | Nearby stops result | 30 minutes |

Show cached data with a "Last updated: X ago — offline" banner when the network call fails. Never show cached data silently without indicating staleness.

---

## Part 9 — MAUI best practices — zero warning debt

Given previous MAUI project experience, enforce these from day one:

### Nullable reference types
`<Nullable>enable</Nullable>` is already in the .csproj above. All properties, parameters, and return types must be nullable-annotated correctly. No `!` null-forgiving operators except where genuinely justified with a comment.

### Compiled bindings
Every `DataTemplate` and `BindingContext` must declare `x:DataType`:

```xml
<!-- Correct -->
<CollectionView.ItemTemplate>
    <DataTemplate x:DataType="models:StopArrivalPrediction">
        <Label Text="{Binding RouteShortName}" />
    </DataTemplate>
</CollectionView.ItemTemplate>

<!-- Forbidden — runtime binding, generates warnings and is slower -->
<DataTemplate>
    <Label Text="{Binding RouteShortName}" />
</DataTemplate>
```

### No code-behind logic
`*.xaml.cs` files contain only:
- Constructor calling `InitializeComponent()` and setting `BindingContext`
- `[QueryProperty]` attributes
- Event handlers that immediately delegate to the ViewModel (one line)

No business logic, no API calls, no navigation logic in code-behind.

### CommunityToolkit.Mvvm patterns
Always use source generators — never manually implement `INotifyPropertyChanged`:

```csharp
// Correct
public partial class GridViewModel : ObservableObject
{
    [ObservableProperty] private bool _isLoading;

    [RelayCommand]
    private async Task RefreshAsync() { ... }
}

// Forbidden — manual implementation
public class GridViewModel : INotifyPropertyChanged
{
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; PropertyChanged?.Invoke(...); }
    }
}
```

### Platform-specific code location
All platform-specific code lives in `Platforms/Android/` or `Platforms/iOS/`. No `#if ANDROID` / `#if IOS` in shared code — use dependency injection with platform-specific registrations in `MauiProgram.cs` instead.

### Image assets
Provide all images as SVG in `Resources/Images/` where possible — MAUI auto-generates density variants. For platform-specific icons (notification badge, app icon), follow the MAUI image guidelines for each density.

---

## Part 10 — Platform setup checklist

### Android
- `Platforms/Android/AndroidManifest.xml`:
  - Permissions: `INTERNET`, `ACCESS_FINE_LOCATION`, `ACCESS_COARSE_LOCATION`, `RECEIVE_BOOT_COMPLETED`, `VIBRATE`
  - Google Maps meta-data (see Part 6)
  - FCM service declaration (Plugin.Firebase handles this via manifest merge)
- Minimum SDK: `android:minSdkVersion="26"` (Android 8.0)
- Target SDK: 34

### iOS
- `Platforms/iOS/Info.plist`:
  - `NSLocationWhenInUseUsageDescription` — "Ireland Live Signals uses your location to find nearby stops."
  - `UIBackgroundModes` — `remote-notification` (for background push)
- Entitlements (`Platforms/iOS/Entitlements.plist`):
  - `aps-environment` = `production` (or `development` for debug builds)
- Minimum deployment target: iOS 16.4 (required for Web Push parity; good baseline for FCM too)

---

## Tests

### Unit tests (Tests project)

**TokenRefreshHandlerTests**
- First request: token attached correctly
- 401 response → refresh called → retry with new token
- Refresh fails → logout triggered, 401 returned to caller

**AuthServiceTests** — mock `ISignalEireApiClient`
- Login success → tokens stored in SecureStorage
- Login failure → tokens not stored, returns false
- Refresh success → new tokens overwrite old
- Refresh failure → tokens cleared

**LocalCacheServiceTests**
- Set and get round-trip
- Expired entry returns null
- Remove clears entry
- Clear empties all entries

**ViewModelTests** — mock `ISignalEireApiClient` and `ILocalCacheService`
- `GridViewModel.RefreshCommand`: successful API response → `CurrentReading` set, `IsOffline` false
- `GridViewModel.RefreshCommand`: API failure, cache hit → `CurrentReading` set, `IsOffline` true
- `GridViewModel.RefreshCommand`: API failure, no cache → `ErrorMessage` set
- `TransitSearchViewModel`: query length < 2 → no search triggered
- `TransitSearchViewModel`: query length >= 2 → `SearchCommand` executed
- `FavouritesViewModel`: not authenticated → `ShowLoginPrompt` true

### API tests (IntegrationTests project)

**JwtAuthTests** — using `WebApplicationFactory`:
- `POST /api/auth/login` with valid credentials → 200, access token returned
- `POST /api/auth/login` with invalid credentials → 401
- `GET /api/me/favourites` with valid Bearer token → 200
- `GET /api/me/favourites` without token → 401
- `GET /api/me/favourites` with expired token → 401
- `POST /api/auth/refresh` with valid refresh token → 200, new tokens
- `POST /api/auth/refresh` with used refresh token → 401

**DeviceTokenTests**:
- Register device token → stored for user
- Register same FCM token for different user → old association removed
- Logout → device token removed

---

## Constraints

1. **No new API behaviour broken.** JWT is additive. All existing Razor Pages cookie auth continues to work unchanged.
2. **Secrets never committed.** `Secrets.cs`, `google-services.json`, `GoogleService-Info.plist` all in `.gitignore`. Document required values in `docs/mobile-setup.md`.
3. **Zero warning debt from day one.** Nullable enabled, compiled bindings everywhere, no deferred fixes.
4. **No code-behind logic.** All logic in ViewModels.
5. **Offline data shown with staleness indicator.** Never silently show stale data.
6. **Stale FCM tokens cleaned up.** On 410/Invalid response from Firebase, delete the token.
7. **All Phase 1–6 tests remain green.** MAUI project does not affect existing test suite.

---

## Definition of done for Phase 7

**API (backend):**
- [ ] `POST /api/auth/login` returns JWT + refresh token
- [ ] `POST /api/auth/refresh` rotates tokens correctly
- [ ] `GET /api/me/favourites` returns 401 without Bearer token
- [ ] `GET /api/me/favourites` returns 200 with valid Bearer token
- [ ] Razor Pages login still works (cookie auth unchanged)
- [ ] `POST /api/push/device-token` stores FCM token
- [ ] Firebase Admin sends a test notification to a registered device token

**MAUI app:**
- [ ] `dotnet build -f net8.0-android` succeeds with zero errors and zero warnings
- [ ] `dotnet build -f net8.0-ios` succeeds with zero errors and zero warnings
- [ ] App installs and launches on Android emulator
- [ ] App installs and launches on iOS simulator
- [ ] Login flow completes and navigates to Shell
- [ ] Grid tab shows current grid reading
- [ ] Stop search returns results
- [ ] Stop board shows confidence-scored arrivals with ghost risk labels
- [ ] Favourite stop saved and visible in Favourites tab after restart
- [ ] Google Maps renders with stop markers on Map page
- [ ] Vehicle markers appear and refresh without full map reload
- [ ] Push notification received on Android device/emulator
- [ ] Push notification received on iOS device
- [ ] Tapping push notification navigates to correct page
- [ ] Offline mode: last grid reading shown with "offline" indicator when network unavailable
- [ ] All Phase 1–7 unit and integration tests pass

---

## What comes in Phase 8

- Tariff-aware grid scoring (Electric Ireland / Bord Gáis Night Rate config, triggered when tariff data is ready)
- Postgres + TimescaleDB migration (if load justifies it)
- Multi-region support (ROI + Northern Ireland all-island view)
- Public developer API keys and external API documentation page
- App Store / Play Store submission (signing, review preparation)
- iOS TestFlight beta distribution
