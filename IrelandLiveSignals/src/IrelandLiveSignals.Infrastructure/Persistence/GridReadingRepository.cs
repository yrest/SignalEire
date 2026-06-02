using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace IrelandLiveSignals.Infrastructure.Persistence;

public class GridReadingRepository : IGridReadingRepository
{
    private readonly GridDbContext _db;

    public GridReadingRepository(GridDbContext db) => _db = db;

    public async Task SaveAsync(GridReading reading, CancellationToken cancellationToken = default)
    {
        _db.GridReadings.Add(reading);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<GridReading?> GetLatestAsync(string region = "ROI", CancellationToken cancellationToken = default) =>
        await _db.GridReadings
            .Where(r => r.Region == region)
            .OrderByDescending(r => r.TimestampUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<GridReading>> GetRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        return await _db.GridReadings
            .Where(r => r.TimestampUtc >= from && r.TimestampUtc <= to)
            .OrderBy(r => r.TimestampUtc)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<GridReading>> GetRecentAsync(int count, CancellationToken ct = default) =>
        await _db.GridReadings
            .OrderByDescending(r => r.TimestampUtc)
            .Take(count)
            .OrderBy(r => r.TimestampUtc)
            .ToListAsync(ct);
}
