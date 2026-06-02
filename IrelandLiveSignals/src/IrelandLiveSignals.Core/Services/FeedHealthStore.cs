using System.Collections.Concurrent;

namespace IrelandLiveSignals.Core.Services;

public class FeedHealthStore
{
    private readonly ConcurrentDictionary<string, FeedHealthEntry> _entries = new();

    public void RecordSuccess(string source, TimeSpan latency)
    {
        var entry = _entries.GetOrAdd(source, _ => new FeedHealthEntry { Source = source });
        lock (entry)
        {
            var now = DateTimeOffset.UtcNow;
            entry.LastSuccessUtc = now;
            entry.LastAttemptUtc = now;
            entry.LastAttemptSucceeded = true;
            entry.LastLatency = latency;
            entry._recentAttempts.Add((now, true));
            PruneAttempts(entry);
            entry.FailureCount24h = entry._recentAttempts.Count(a => !a.success);
            // Rolling average latency (last 20 successes)
            var successLatencies = entry._recentAttempts
                .Where(a => a.success)
                .TakeLast(20)
                .ToList();
            entry.AverageLatencyMs = successLatencies.Count > 0
                ? latency.TotalMilliseconds // simplified: update with latest for now
                : latency.TotalMilliseconds;
        }
    }

    public void RecordFailure(string source, string error)
    {
        var entry = _entries.GetOrAdd(source, _ => new FeedHealthEntry { Source = source });
        lock (entry)
        {
            var now = DateTimeOffset.UtcNow;
            entry.LastAttemptUtc = now;
            entry.LastAttemptSucceeded = false;
            entry._recentAttempts.Add((now, false));
            PruneAttempts(entry);
            entry.FailureCount24h = entry._recentAttempts.Count(a => !a.success);
        }
    }

    public IReadOnlyList<FeedHealthEntry> GetAll() => _entries.Values.ToList();

    private static void PruneAttempts(FeedHealthEntry entry)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
        entry._recentAttempts.RemoveAll(a => a.time < cutoff);
    }
}

public class FeedHealthEntry
{
    public string Source { get; set; } = "";
    public DateTimeOffset LastSuccessUtc { get; set; }
    public DateTimeOffset LastAttemptUtc { get; set; }
    public bool LastAttemptSucceeded { get; set; }
    public TimeSpan LastLatency { get; set; }
    public int FailureCount24h { get; set; }
    public double AverageLatencyMs { get; set; }
    internal readonly List<(DateTimeOffset time, bool success)> _recentAttempts = new();
}
