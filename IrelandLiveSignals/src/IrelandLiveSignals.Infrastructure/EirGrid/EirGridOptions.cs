namespace IrelandLiveSignals.Infrastructure.EirGrid;

public class EirGridRegionConfig
{
    public string Name { get; set; } = "ROI";
    public string EirGridUrl { get; set; } = "https://www.smartgriddashboard.com/";
}

public class EirGridOptions
{
    public string BaseUrl { get; set; } = "https://www.smartgriddashboard.com/";
    public string Region { get; set; } = "ROI";
    public string RawSnapshotPath { get; set; } = "data/raw";
    public List<EirGridRegionConfig> Regions { get; set; } = [];
}
