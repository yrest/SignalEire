using IrelandLiveSignals.Core.Models;

namespace IrelandLiveSignals.Core.Interfaces;

public interface IGridDataAdapter
{
    Task<(RawGridSnapshot Snapshot, GridReading Reading)> FetchLatestAsync(CancellationToken cancellationToken = default);
}
