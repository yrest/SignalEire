using IrelandLiveSignals.Core.Models;

namespace IrelandLiveSignals.Core.Interfaces;

public interface IGridReadingRepository
{
    Task SaveAsync(GridReading reading, CancellationToken cancellationToken = default);
    Task<GridReading?> GetLatestAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GridReading>> GetRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
    Task<IReadOnlyList<GridReading>> GetRecentAsync(int count, CancellationToken ct = default);
}
