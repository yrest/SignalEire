using IrelandLiveSignals.Core.Models;

namespace IrelandLiveSignals.Core.Services;

public record ReliabilityReport
{
    public string RouteId { get; init; } = string.Empty;
    public string StopId { get; init; } = string.Empty;
    public int TotalObservations { get; init; }
    public double VehicleConfirmedPercent { get; init; }
    public double GhostPercent { get; init; }
    public double AverageDelaySeconds { get; init; }
    public double ReliabilityScore { get; init; }
    public string ReliabilityLabel { get; init; } = string.Empty;
    public DateTimeOffset? LastUpdatedUtc { get; init; }
}

public static class TransitReliabilityService
{
    /// <summary>
    /// Computes a reliability score 0–1 from raw observation counters and user reports.
    /// </summary>
    public static double ComputeScore(
        int vehicleConfirmed,
        int timetableOnly,
        int ghostCount,
        IReadOnlyList<TransitUserReport> recentReports)
    {
        var total = vehicleConfirmed + timetableOnly + ghostCount;
        if (total == 0) return 0.5; // no data → neutral

        // Base score: fraction vehicle-confirmed, ghosts penalise heavily
        var baseScore = (vehicleConfirmed - ghostCount * 0.5) / (double)total;
        baseScore = Math.Clamp(baseScore, 0.0, 1.0);

        // User report adjustment — weighted
        var positiveWeight = recentReports
            .Where(r => r.ReportType is "bus_seen")
            .Sum(r => r.TrustWeight);
        var negativeWeight = recentReports
            .Where(r => r.ReportType is "bus_not_appeared" or "bus_passed_full")
            .Sum(r => r.TrustWeight);

        var reportAdjustment = (positiveWeight - negativeWeight) * 0.05;
        return Math.Clamp(baseScore + reportAdjustment, 0.0, 1.0);
    }

    public static string ReliabilityLabel(double score) => score switch
    {
        >= 0.75 => "reliable",
        >= 0.50 => "moderate",
        >= 0.25 => "poor",
        _       => "unreliable"
    };

    public static ReliabilityReport BuildReport(
        TransitReliabilityAggregate agg,
        IReadOnlyList<TransitUserReport> recentReports)
    {
        var total = agg.TotalObservations;
        var score = ComputeScore(agg.VehicleConfirmedCount, agg.TimetableOnlyCount, agg.GhostCount, recentReports);

        return new ReliabilityReport
        {
            RouteId = agg.RouteId,
            StopId = agg.StopId,
            TotalObservations = total,
            VehicleConfirmedPercent = total > 0 ? (agg.VehicleConfirmedCount / (double)total) * 100.0 : 0,
            GhostPercent = total > 0 ? (agg.GhostCount / (double)total) * 100.0 : 0,
            AverageDelaySeconds = agg.AverageDelaySeconds,
            ReliabilityScore = Math.Round(score, 3),
            ReliabilityLabel = ReliabilityLabel(score),
            LastUpdatedUtc = agg.LastUpdatedUtc
        };
    }
}
