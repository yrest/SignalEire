namespace IrelandLiveSignals.Core.Models;

public class ServiceAlertRecord
{
    public string AlertId { get; set; } = string.Empty;
    public DateTimeOffset FetchedAtUtc { get; set; }
    public string HeaderText { get; set; } = string.Empty;
    public string DescriptionText { get; set; } = string.Empty;
    // "NO_SERVICE" | "REDUCED_SERVICE" | "SIGNIFICANT_DELAYS" | "DETOUR" | "OTHER_EFFECT" | "UNKNOWN_EFFECT"
    public string Effect { get; set; } = "UNKNOWN_EFFECT";
    public string[] AffectedRouteIds { get; set; } = Array.Empty<string>();
    public string[] AffectedStopIds { get; set; } = Array.Empty<string>();
    public DateTimeOffset? ActiveUntilUtc { get; set; }
}
