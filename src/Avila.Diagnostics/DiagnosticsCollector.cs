using System.Collections.Concurrent;
using System.Diagnostics;

namespace Avila.Diagnostics;

public sealed class DiagnosticsCollector
{
    private readonly Stopwatch _startup = Stopwatch.StartNew();
    private readonly ConcurrentDictionary<string, long> _bridgeCalls = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _errors = new(StringComparer.Ordinal);

    public TimeSpan StartupElapsed => _startup.Elapsed;

    public TimeSpan? FirstPaintAt { get; private set; }

    public TimeSpan? BridgeReadyAt { get; private set; }

    public long InitialWorkingSetBytes { get; private set; }

    public long? PostShrinkWorkingSetBytes { get; private set; }

    public void MarkInitialMemory()
    {
        InitialWorkingSetBytes = Environment.WorkingSet;
    }

    public void MarkPostShrinkMemory()
    {
        PostShrinkWorkingSetBytes = Environment.WorkingSet;
    }

    public void MarkFirstPaint()
    {
        FirstPaintAt ??= _startup.Elapsed;
    }

    public void MarkBridgeReady()
    {
        BridgeReadyAt ??= _startup.Elapsed;
    }

    public void CountBridgeCall(string command)
    {
        _bridgeCalls.AddOrUpdate(command, 1, static (_, value) => value + 1);
    }

    public void CountError(string category)
    {
        _errors.AddOrUpdate(category, 1, static (_, value) => value + 1);
    }

    public DiagnosticsSnapshot Snapshot(int workerQueueDepth = 0, int activeWorkers = 0)
    {
        return new DiagnosticsSnapshot(
            StartupElapsed,
            FirstPaintAt,
            BridgeReadyAt,
            InitialWorkingSetBytes,
            PostShrinkWorkingSetBytes,
            workerQueueDepth,
            activeWorkers,
            _bridgeCalls.ToDictionary(),
            _errors.ToDictionary());
    }
}

public sealed record DiagnosticsSnapshot(
    TimeSpan StartupElapsed,
    TimeSpan? FirstPaintAt,
    TimeSpan? BridgeReadyAt,
    long InitialWorkingSetBytes,
    long? PostShrinkWorkingSetBytes,
    int WorkerQueueDepth,
    int ActiveWorkers,
    IReadOnlyDictionary<string, long> BridgeCalls,
    IReadOnlyDictionary<string, long> Errors);
