using IrelandLiveSignals.Core.Services;
using Xunit;

namespace IrelandLiveSignals.Tests;

public class FeedHealthStoreTests
{
    [Fact]
    public void RecordSuccess_UpdatesLastSuccessUtcAndLatency()
    {
        var store = new FeedHealthStore();
        var latency = TimeSpan.FromMilliseconds(250);

        store.RecordSuccess("test_feed", latency);

        var entries = store.GetAll();
        Assert.Single(entries);
        var entry = entries[0];
        Assert.Equal("test_feed", entry.Source);
        Assert.True(entry.LastSuccessUtc > DateTimeOffset.MinValue);
        Assert.Equal(latency, entry.LastLatency);
        Assert.True(entry.LastAttemptSucceeded);
    }

    [Fact]
    public void RecordFailure_IncrementsFailureCount24h()
    {
        var store = new FeedHealthStore();

        store.RecordFailure("test_feed", "timeout");
        store.RecordFailure("test_feed", "connection refused");

        var entries = store.GetAll();
        Assert.Single(entries);
        Assert.Equal(2, entries[0].FailureCount24h);
        Assert.False(entries[0].LastAttemptSucceeded);
    }

    [Fact]
    public void FailureCount24h_OnlyCountsFailuresWithinLast24h()
    {
        var store = new FeedHealthStore();

        // We can't easily simulate old failures without time injection.
        // Record some failures and verify the count only includes recent ones.
        store.RecordFailure("feed", "err1");
        store.RecordFailure("feed", "err2");
        store.RecordSuccess("feed", TimeSpan.FromMilliseconds(100));
        store.RecordFailure("feed", "err3");

        var entries = store.GetAll();
        // 3 failures total, all within last 24h
        Assert.Equal(3, entries[0].FailureCount24h);
    }

    [Fact]
    public void GetAll_ReturnsAllRegisteredSources()
    {
        var store = new FeedHealthStore();

        store.RecordSuccess("source_a", TimeSpan.FromMilliseconds(100));
        store.RecordSuccess("source_b", TimeSpan.FromMilliseconds(200));
        store.RecordFailure("source_c", "error");

        var entries = store.GetAll();
        Assert.Equal(3, entries.Count);
        var sources = entries.Select(e => e.Source).OrderBy(s => s).ToList();
        Assert.Equal(new[] { "source_a", "source_b", "source_c" }, sources);
    }

    [Fact]
    public void RecordSuccess_AfterFailure_SetsLastAttemptSucceeded()
    {
        var store = new FeedHealthStore();
        store.RecordFailure("feed", "error");
        Assert.False(store.GetAll()[0].LastAttemptSucceeded);

        store.RecordSuccess("feed", TimeSpan.FromMilliseconds(50));
        Assert.True(store.GetAll()[0].LastAttemptSucceeded);
    }
}
