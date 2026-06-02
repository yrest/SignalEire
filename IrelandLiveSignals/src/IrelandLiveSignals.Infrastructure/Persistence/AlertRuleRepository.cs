using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace IrelandLiveSignals.Infrastructure.Persistence;

public class AlertRuleRepository : IAlertRuleRepository
{
    private readonly GridDbContext _db;

    public AlertRuleRepository(GridDbContext db) => _db = db;

    public async Task<IReadOnlyList<AlertRule>> GetActiveAsync(CancellationToken ct = default) =>
        await _db.AlertRules.Where(r => r.IsActive).ToListAsync(ct);

    public async Task<AlertRule?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await _db.AlertRules.FindAsync(new object[] { id }, ct);

    public async Task SaveAsync(AlertRule rule, CancellationToken ct = default)
    {
        var existing = await _db.AlertRules.FindAsync(new object[] { rule.Id }, ct);
        if (existing is null)
            _db.AlertRules.Add(rule);
        else
        {
            _db.Entry(existing).CurrentValues.SetValues(rule);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var rule = await _db.AlertRules.FindAsync(new object[] { id }, ct);
        if (rule is not null)
        {
            _db.AlertRules.Remove(rule);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task SaveDeliveryAsync(AlertDelivery delivery, CancellationToken ct = default)
    {
        _db.AlertDeliveries.Add(delivery);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> CountDeliveriesForRuleTodayAsync(string ruleId, CancellationToken ct = default)
    {
        var startOfDay = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        return await _db.AlertDeliveries
            .Where(d => d.AlertRuleId == ruleId && d.FiredAtUtc >= startOfDay)
            .CountAsync(ct);
    }

    public async Task<IReadOnlyList<AlertDelivery>> GetRecentDeliveriesAsync(int limit = 50, CancellationToken ct = default) =>
        await _db.AlertDeliveries
            .OrderByDescending(d => d.FiredAtUtc)
            .Take(limit)
            .ToListAsync(ct);
}
