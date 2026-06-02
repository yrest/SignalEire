# Mobile Client Setup Guide

This document describes how to set up the `IrelandLiveSignals.MauiClient` project for local development and CI/CD builds.

## Prerequisites

- .NET 8 SDK with MAUI workload: `dotnet workload install maui`
- Android SDK (API 34) for Android builds
- Xcode 15+ for iOS builds (macOS only)
- Firebase project with Android and iOS apps registered

---

## Required Files (not committed to source control)

### 1. Firebase — Android

Place `google-services.json` at:

```
src/IrelandLiveSignals.MauiClient/Platforms/Android/google-services.json
```

Download from: Firebase Console → Project Settings → Your Android App → `google-services.json`.

### 2. Firebase — iOS

Place `GoogleService-Info.plist` at:

```
src/IrelandLiveSignals.MauiClient/Platforms/iOS/GoogleService-Info.plist
```

Download from: Firebase Console → Project Settings → Your iOS App → `GoogleService-Info.plist`.

In your Xcode / build settings ensure the file is added as a bundle resource.

---

## Google Maps API Key

The app reads the Google Maps API key from `Secrets.GoogleMapsApiKey` (see `Secrets.cs`), which in turn reads the `GOOGLE_MAPS_API_KEY` environment variable.

### Option A — Environment variable (recommended for CI/CD)

```bash
export GOOGLE_MAPS_API_KEY="AIzaSy..."
dotnet build -f net8.0-android
```

### Option B — Local Secrets file (development only, never commit)

Create `Platforms/Android/Secrets.local.cs` (gitignored):

```csharp
namespace IrelandLiveSignals.MauiClient;
// Local override — DO NOT COMMIT
internal static partial class Secrets
{
    // Matches the static property in Secrets.cs
}
```

Or simply set the environment variable in your shell profile.

### Android manifest placeholder

`Platforms/Android/AndroidManifest.xml` contains:

```xml
<meta-data android:name="com.google.android.maps.v2.API_KEY"
           android:value="YOUR_GOOGLE_MAPS_API_KEY" />
```

Replace `YOUR_GOOGLE_MAPS_API_KEY` with your key for local builds, or inject it via a CI/CD step.

### iOS Info.plist placeholder

`Platforms/iOS/Info.plist` contains a `GMSApiKey` entry — replace `YOUR_GOOGLE_MAPS_API_KEY` with your actual key.

---

## Build Commands

### Android (Debug)

```bash
dotnet build src/IrelandLiveSignals.MauiClient/IrelandLiveSignals.MauiClient.csproj \
  -f net8.0-android \
  -c Debug
```

### Android (Release APK)

```bash
dotnet publish src/IrelandLiveSignals.MauiClient/IrelandLiveSignals.MauiClient.csproj \
  -f net8.0-android \
  -c Release \
  -p:AndroidPackageFormat=apk
```

### iOS (Debug, requires macOS + Xcode)

```bash
dotnet build src/IrelandLiveSignals.MauiClient/IrelandLiveSignals.MauiClient.csproj \
  -f net8.0-ios \
  -c Debug
```

---

## .gitignore

The following files are already excluded in the root `.gitignore`:

```
**/Platforms/Android/google-services.json
**/Platforms/iOS/GoogleService-Info.plist
**/Platforms/*/Secrets.local.cs
```

Never commit these files. If they appear in `git status`, verify your `.gitignore` is applied correctly.

---

## Firebase Service Account (backend push notifications)

The backend (`IrelandLiveSignals.Api` / `IrelandLiveSignals.Worker`) sends FCM pushes using a Firebase service account JSON file.

Place it at a secure path and set the environment variable:

```bash
FIREBASE_SERVICE_ACCOUNT_PATH=/run/secrets/firebase-service-account.json
```

Or inject it via Kubernetes Secret / Docker secret at runtime. **Never commit this file.**

---

## Architecture Notes

- `TokenRefreshHandler` automatically attaches the Bearer token and retries on 401.
- `LocalCacheService` uses SQLite (sqlite-net-pcl) at `FileSystem.AppDataDirectory/signals_cache.db`.
- `PushNotificationService` registers the FCM token with the backend on first launch and whenever the token rotates.
- All ViewModels use `CommunityToolkit.Mvvm` source generators (`[ObservableProperty]`, `[RelayCommand]`).
- Map uses `Maui.GoogleMaps` — the namespace in XAML is `clr-namespace:Maui.GoogleMaps;assembly=Maui.GoogleMaps`.
