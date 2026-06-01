using IrelandLiveSignals.Core.Models;

namespace IrelandLiveSignals.Core.Services;

public enum VehiclePresence { VehicleConfirmed, TripUpdateOnly, TimetableOnly, Cancelled }

public record ConfidenceInput
{
    public VehiclePresence Presence { get; init; }
    public int? GpsAgeSeconds { get; init; }
    public bool TripIdMatched { get; init; }
    public bool RouteMatched { get; init; }
    public double? DistanceToStopMeters { get; init; }
    public bool HasServiceAlert { get; init; }
    public string AlertEffect { get; init; } = string.Empty;
}

public record ConfidenceResult
{
    public double Score { get; init; }
    public string GhostRisk { get; init; } = string.Empty;
    public string StatusLabel { get; init; } = string.Empty;
}

public static class TransitConfidenceService
{
    public static ConfidenceResult Compute(ConfidenceInput input)
    {
        var vehicleScore      = VehiclePresenceScore(input.Presence);
        var freshnessScore    = GpsFreshnessScore(input.GpsAgeSeconds);
        var tripMatchScore    = TripMatchScore(input.TripIdMatched, input.RouteMatched, input.Presence);
        var plausibilityScore = PhysicalPlausibilityScore(input.DistanceToStopMeters, input.Presence);
        var alertScore        = ServiceAlertScore(input.HasServiceAlert, input.AlertEffect);
        const double historicalScore = 0.5; // MVP hardcoded

        // Multiplicative — any zero collapses the whole score
        var raw = vehicleScore * freshnessScore * tripMatchScore * plausibilityScore * alertScore * historicalScore;

        // Adjust: historical 0.5 floor is unfair for very confident readings — blend instead
        var score = Math.Clamp(
            0.5 * raw + 0.5 * (vehicleScore * freshnessScore * alertScore),
            0.0, 1.0);

        var ghostRisk = score switch
        {
            >= 0.65 => "low",
            >= 0.40 => "medium",
            _       => "high"
        };

        var label = DeriveStatusLabel(input);

        return new ConfidenceResult { Score = score, GhostRisk = ghostRisk, StatusLabel = label };
    }

    private static double VehiclePresenceScore(VehiclePresence presence) => presence switch
    {
        VehiclePresence.VehicleConfirmed => 1.0,
        VehiclePresence.TripUpdateOnly   => 0.6,
        VehiclePresence.TimetableOnly    => 0.3,
        VehiclePresence.Cancelled        => 0.0,
        _                                => 0.3
    };

    private static double GpsFreshnessScore(int? ageSeconds) => ageSeconds switch
    {
        null         => 0.3,   // no GPS
        <= 30        => 1.0,
        <= 90        => 0.8,
        <= 180       => 0.5,
        <= 300       => 0.25,
        _            => 0.1
    };

    private static double TripMatchScore(bool tripMatched, bool routeMatched, VehiclePresence presence)
    {
        if (presence == VehiclePresence.TimetableOnly) return 0.5;
        if (tripMatched) return 1.0;
        if (routeMatched) return 0.7;
        return 0.5;
    }

    private static double PhysicalPlausibilityScore(double? distanceMeters, VehiclePresence presence)
    {
        if (presence != VehiclePresence.VehicleConfirmed) return 0.7;
        if (distanceMeters is null) return 0.7;
        // Beyond 10 km — vehicle probably not heading to this stop
        if (distanceMeters > 10_000) return 0.4;
        return 0.9;
    }

    private static double ServiceAlertScore(bool hasAlert, string effect)
    {
        if (!hasAlert) return 1.0;
        return effect switch
        {
            "NO_SERVICE"          => 0.0,
            "SIGNIFICANT_DELAYS"  => 0.5,
            "REDUCED_SERVICE"     => 0.6,
            "DETOUR"              => 0.7,
            _                     => 0.8
        };
    }

    private static string DeriveStatusLabel(ConfidenceInput input)
    {
        if (input.Presence == VehiclePresence.Cancelled) return "Cancelled";
        if (input.Presence == VehiclePresence.VehicleConfirmed && input.GpsAgeSeconds is <= 300) return "Vehicle confirmed";
        if (input.Presence == VehiclePresence.VehicleConfirmed && input.GpsAgeSeconds is > 300) return "Stale GPS";
        if (input.Presence == VehiclePresence.TripUpdateOnly) return "Likely active";
        return "Timetable only";
    }
}
