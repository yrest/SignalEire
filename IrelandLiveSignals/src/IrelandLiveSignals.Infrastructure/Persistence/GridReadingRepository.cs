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

    public async Task<GridReading?> GetLatestAsync(CancellationToken cancellationToken = default) =>
        await _db.GridReadings
            .OrderByDescending(r => r.TimestampUtc)
            .FirstOrDefaultAsync(cancellationToken);
}
