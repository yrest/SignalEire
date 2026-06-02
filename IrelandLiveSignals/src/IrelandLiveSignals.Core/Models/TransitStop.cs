namespace IrelandLiveSignals.Core.Models;

public class TransitStop
{
    public string StopId { get; set; } = string.Empty;
    public string StopCode { get; set; } = string.Empty;
    public string StopName { get; set; } = string.Empty;
    public double StopLat { get; set; }
    public double StopLon { get; set; }
    public string? AgencyId { get; set; }
}
