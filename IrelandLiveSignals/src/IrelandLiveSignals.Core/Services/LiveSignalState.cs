using System.Collections.Concurrent;

namespace IrelandLiveSignals.Core.Services;

public class LiveSignalState
{
    public double LatestCo2GPerKwh { get; set; }
    public int ActiveVehicleCount { get; set; }
    // keyed by source name
    public ConcurrentDictionary<string, DateTimeOffset> LastSuccessfulFetch { get; } = new();
}
