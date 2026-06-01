using IrelandLiveSignals.Core.Models;

namespace IrelandLiveSignals.Core.Interfaces;

public interface IGridReadingRepository
{
    Task SaveAsync(GridReading reading, CancellationToken cancellationToken = default);
    Task<GridReading?> GetLatestAsync(CancellationToken cancellationToken = default);
}
