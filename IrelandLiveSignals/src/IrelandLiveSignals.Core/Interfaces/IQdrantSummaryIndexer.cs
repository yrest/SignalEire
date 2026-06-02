using IrelandLiveSignals.Core.Models;

namespace IrelandLiveSignals.Core.Interfaces;

public interface IQdrantSummaryIndexer
{
    Task IndexAsync(SignalSummary summary, CancellationToken ct);
    Task<IReadOnlyList<SignalSummary>> SearchAsync(string query, int topK, CancellationToken ct);
}
