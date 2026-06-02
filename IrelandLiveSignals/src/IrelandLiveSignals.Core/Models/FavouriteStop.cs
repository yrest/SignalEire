namespace IrelandLiveSignals.Core.Models;

public class FavouriteStop
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string StopId { get; set; } = "";
    public string? DisplayLabel { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
