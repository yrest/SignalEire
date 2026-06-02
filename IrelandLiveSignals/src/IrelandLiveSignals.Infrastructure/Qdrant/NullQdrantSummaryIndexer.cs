using IrelandLiveSignals.Core.Interfaces;
using IrelandLiveSignals.Core.Models;
using Microsoft.Extensions.Logging;

namespace IrelandLiveSignals.Infrastructure.Qdrant;

public class NullQdrantSummaryIndexer : IQdrantSummaryIndexer
{
    private readonly ILogger<NullQdrantSummaryIndexer> _logger;

    public NullQdrantSummaryIndexer(ILogger<NullQdrantSummaryIndexer> logger)
    {
        _logger = logger;
    }

    public Task IndexAsync(SignalSummary summary, CancellationToken ct)
    {
        _logger.LogWarning("Qdrant is not configured. Skipping index of summary {Id} ({Module}/{Date}).",
            summary.Id, summary.Module, summary.Date);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SignalSummary>> SearchAsync(string query, int topK, CancellationToken ct)
    {
        _logger.LogWarning("Qdrant is not configured. Returning empty search results for query '{Query}'.", query);
        return Task.FromResult<IReadOnlyList<SignalSummary>>(Array.Empty<SignalSummary>());
    }
}
