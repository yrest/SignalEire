// Secrets.cs — shared placeholder.
// Replace with your actual API key or inject via environment variable at build time.
// See docs/mobile-setup.md for instructions.
// DO NOT commit real keys to source control.

namespace IrelandLiveSignals.MauiClient;

internal static class Secrets
{
    /// <summary>
    /// Google Maps API key.
    /// Set the GOOGLE_MAPS_API_KEY environment variable at build time,
    /// or replace the fallback string with your key (local builds only — do not commit).
    /// </summary>
    public static string GoogleMapsApiKey { get; } =
        Environment.GetEnvironmentVariable("GOOGLE_MAPS_API_KEY") ?? string.Empty;
}
