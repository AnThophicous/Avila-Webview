using System.Collections.Concurrent;
using System.Diagnostics;

namespace Avila.Diagnostics;

public sealed class DiagnosticsCollector
{
    private readonly Stopwatch _startup = Stopwatch.StartNew();
    private readonly ConcurrentDictionary<string, long> _bridgeCalls = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _errors = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _stageHits = new(StringComparer.Ordinal);

    public TimeSpan StartupElapsed => _startup.Elapsed;

    public RuntimeStage CurrentStage { get; private set; } = RuntimeStage.BackgroundPreparation;

    public TimeSpan? BackgroundPreparationAt { get; private set; }

    public TimeSpan? WebViewWorkingAt { get; private set; }

    public TimeSpan? OpenAt { get; private set; }

    public TimeSpan? FirstPaintAt { get; private set; }

    public TimeSpan? BridgeReadyAt { get; private set; }

    public long InitialWorkingSetBytes { get; private set; }

    public long? PostShrinkWorkingSetBytes { get; private set; }

    public void MarkInitialMemory()
    {
        InitialWorkingSetBytes = Environment.WorkingSet;
    }

    public void MarkStage(RuntimeStage stage)
    {
        CurrentStage = stage;
        _stageHits.AddOrUpdate(stage.ToString(), 1, static (_, value) => value + 1);
        var elapsed = _startup.Elapsed;
        switch (stage)
        {
            case RuntimeStage.BackgroundPreparation:
                BackgroundPreparationAt ??= elapsed;
                break;
            case RuntimeStage.WebViewWorking:
                WebViewWorkingAt ??= elapsed;
                break;
            case RuntimeStage.Open:
                OpenAt ??= elapsed;
                break;
        }
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
            CurrentStage,
            BackgroundPreparationAt,
            WebViewWorkingAt,
            OpenAt,
            FirstPaintAt,
            BridgeReadyAt,
            InitialWorkingSetBytes,
            PostShrinkWorkingSetBytes,
            workerQueueDepth,
            activeWorkers,
            _bridgeCalls.ToDictionary(),
            _errors.ToDictionary(),
            _stageHits.ToDictionary());
    }
}

public enum RuntimeStage
{
    BackgroundPreparation,
    WebViewWorking,
    Open
}

public sealed record DiagnosticsSnapshot(
    TimeSpan StartupElapsed,
    RuntimeStage CurrentStage,
    TimeSpan? BackgroundPreparationAt,
    TimeSpan? WebViewWorkingAt,
    TimeSpan? OpenAt,
    TimeSpan? FirstPaintAt,
    TimeSpan? BridgeReadyAt,
    long InitialWorkingSetBytes,
    long? PostShrinkWorkingSetBytes,
    int WorkerQueueDepth,
    int ActiveWorkers,
    IReadOnlyDictionary<string, long> BridgeCalls,
    IReadOnlyDictionary<string, long> Errors,
    IReadOnlyDictionary<string, long> StageHits);
