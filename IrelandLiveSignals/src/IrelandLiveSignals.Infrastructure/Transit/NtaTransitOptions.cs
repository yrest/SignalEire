namespace IrelandLiveSignals.Infrastructure.Transit;

public class NtaTransitOptions
{
    public string BaseUrl { get; set; } = "https://api.nationaltransport.ie/gtfsr/v2";
    public string ApiKey { get; set; } = string.Empty;
    public string ApiKeyHeader { get; set; } = "Ocp-Apim-Subscription-Key";
    public string TripUpdatesPath { get; set; } = "TripUpdates";
    public string VehiclesPath { get; set; } = "Vehicles";
    public string AlertsPath { get; set; } = "Alerts";
    public string GtfsStaticUrl { get; set; } = "https://www.transportforireland.ie/transitData/Data/GTFS_Realtime.zip";
    public int VehiclesPollIntervalSeconds { get; set; } = 60;
    public int TripUpdatesPollIntervalSeconds { get; set; } = 60;
    public int AlertsPollIntervalSeconds { get; set; } = 300;
    public int GtfsStaticRefreshDays { get; set; } = 14;
    // Seconds to stagger feed polls to stay within rate limits
    public int FeedStaggerSeconds { get; set; } = 25;
}
