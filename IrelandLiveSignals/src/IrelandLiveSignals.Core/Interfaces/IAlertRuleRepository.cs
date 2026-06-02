using IrelandLiveSignals.Core.Models;

namespace IrelandLiveSignals.Core.Interfaces;

public interface IAlertRuleRepository
{
    Task<IReadOnlyList<AlertRule>> GetActiveAsync(CancellationToken ct = default);
    Task<AlertRule?> GetByIdAsync(string id, CancellationToken ct = default);
    Task SaveAsync(AlertRule rule, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);

    Task SaveDeliveryAsync(AlertDelivery delivery, CancellationToken ct = default);
    Task<int> CountDeliveriesForRuleTodayAsync(string ruleId, CancellationToken ct = default);
    Task<IReadOnlyList<AlertDelivery>> GetRecentDeliveriesAsync(int limit = 50, CancellationToken ct = default);
}
