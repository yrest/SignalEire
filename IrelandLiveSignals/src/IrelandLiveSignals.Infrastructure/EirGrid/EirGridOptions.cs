namespace IrelandLiveSignals.Infrastructure.EirGrid;

public class EirGridOptions
{
    public string BaseUrl { get; set; } = "https://www.smartgriddashboard.com/";
    public string Region { get; set; } = "ROI";
    public string RawSnapshotPath { get; set; } = "data/raw";
}
