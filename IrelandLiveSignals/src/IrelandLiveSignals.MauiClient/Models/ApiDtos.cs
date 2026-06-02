namespace IrelandLiveSignals.MauiClient.Models;

public record GridReadingResponse(
    string Region,
    DateTime TimestampUtc,
    double Co2IntensityGPerKwh,
    double RenewablesPercent,
    double GreenScore,
    double SystemDemandMw,
    double WindGenerationMw,
    double SolarGenerationMw,
    string Status,
    string Recommendation,
    int DataFreshnessSeconds
);

public record TransitStopDto(
    string StopId,
    string StopName,
    string StopCode,
    double StopLat,
    double StopLon,
    double DistanceMeters
);

public record ArrivalDto(
    string TripId,
    string RouteId,
    string RouteShortName,
    string Headsign,
    DateTime ScheduledArrivalUtc,
    DateTime? EstimatedArrivalUtc,
    int DelaySeconds,
    double Confidence,
    double GhostRisk,
    string StatusLabel,
    string? VehicleId,
    double? VehicleLat,
    double? VehicleLon
);

public record StopBoardResponse(
    string StopId,
    string StopName,
    List<ArrivalDto> Arrivals
);

public record FavouriteStopDto(
    string Id,
    string StopId,
    string DisplayLabel,
    int SortOrder,
    DateTime CreatedAtUtc
);

public record AlertRuleDto(
    string Id,
    string RuleName,
    double? Co2BelowGPerKwh,
    double? RenewablesAbovePercent,
    double? GreenScoreAbove,
    bool IsActive,
    string DeliveryMode
);

public record VehicleDto(
    string VehicleId,
    string RouteId,
    double Lat,
    double Lon,
    double Bearing,
    double SpeedKph,
    int GpsAgeSeconds
);

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    string UserId,
    string DisplayName
);

public record TariffPlanSummary(string Id, string Name, string Provider, string PlanType);
