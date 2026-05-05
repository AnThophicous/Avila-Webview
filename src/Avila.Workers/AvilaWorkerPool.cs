using System.Collections.Concurrent;
using System.Threading.Channels;
using Avila.Diagnostics;

namespace Avila.Workers;

public sealed class AvilaWorkerPool : IAsyncDisposable
{
    private readonly WorkerPoolOptions _options;
    private readonly SafeLogger _logger;
    private readonly Channel<WorkerItem>[] _queues;
    private readonly SemaphoreSlim _backpressure;
    private readonly ConcurrentDictionary<int, Task> _workers = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _targetSync = new();
    private int _nextWorkerId;
    private int _queueDepth;
    private int _minimumWorkers;
    private int _maximumWorkers;

    public AvilaWorkerPool(WorkerPoolOptions options, SafeLogger logger)
    {
        _options = options;
        _logger = logger;
        _backpressure = new SemaphoreSlim(options.MaxQueueLength, options.MaxQueueLength);
        _minimumWorkers = Math.Clamp(options.MinWorkers, 0, options.MaxWorkers);
        _maximumWorkers = Math.Clamp(options.MaxWorkers, 0, Math.Max(options.MaxWorkers, _minimumWorkers));
        _queues =
        [
            Channel.CreateUnbounded<WorkerItem>(),
            Channel.CreateUnbounded<WorkerItem>(),
            Channel.CreateUnbounded<WorkerItem>()
        ];

        var initialWorkers = _minimumWorkers;
        for (var i = 0; i < initialWorkers; i++)
        {
            StartWorker();
        }
    }

    public int QueueDepth => Volatile.Read(ref _queueDepth);

    public int ActiveWorkers => _workers.Count;

    public int MinimumWorkers => Volatile.Read(ref _minimumWorkers);

    public int MaximumWorkers => Volatile.Read(ref _maximumWorkers);

    public async Task<T> EnqueueAsync<T>(
        Func<CancellationToken, Task<T>> work,
        WorkerPriority priority = WorkerPriority.Normal,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_shutdown.IsCancellationRequested, this);

        await _backpressure.WaitAsync(cancellationToken).ConfigureAwait(false);

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new WorkerItem(
            async token => await work(token).ConfigureAwait(false),
            completion,
            timeout ?? _options.DefaultTimeout);

        Interlocked.Increment(ref _queueDepth);
        await _queues[(int)priority].Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        EnsureWorkerCapacity();

        var result = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return (T)result!;
    }

    public async Task ShrinkAfterStartupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Delay(_options.IdleShrinkDelay, cancellationToken).ConfigureAwait(false);
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: true);
            _logger.Trace("Worker pool entered post-startup shrink window.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void ConfigureTargetWorkers(int minimumWorkers, int maximumWorkers)
    {
        lock (_targetSync)
        {
            _minimumWorkers = Math.Clamp(minimumWorkers, 0, maximumWorkers);
            _maximumWorkers = Math.Max(_minimumWorkers, maximumWorkers);
        }

        PrimeMinimumWorkers();
    }

    public void PrimeMinimumWorkers()
    {
        while (ActiveWorkers < MinimumWorkers && ActiveWorkers < MaximumWorkers)
        {
            StartWorker();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        foreach (var queue in _queues)
        {
            queue.Writer.TryComplete();
        }

        try
        {
            await Task.WhenAll(_workers.Values).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch
        {
        }

        _shutdown.Dispose();
        _backpressure.Dispose();
    }

    private void EnsureWorkerCapacity()
    {
        if (QueueDepth > ActiveWorkers && ActiveWorkers < MaximumWorkers)
        {
            StartWorker();
        }
    }

    private void StartWorker()
    {
        var workerId = Interlocked.Increment(ref _nextWorkerId);
        var task = Task.Run(() => WorkerLoopAsync(workerId));
        _workers[workerId] = task;
    }

    private async Task WorkerLoopAsync(int workerId)
    {
        var lastWorkAt = DateTimeOffset.UtcNow;

        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                if (TryReadNext(out var item))
                {
                    lastWorkAt = DateTimeOffset.UtcNow;
                    await ExecuteAsync(item).ConfigureAwait(false);
                    continue;
                }

                if (ActiveWorkers > MinimumWorkers
                    && DateTimeOffset.UtcNow - lastWorkAt > _options.IdleShrinkDelay)
                {
                    return;
                }

                await Task.Delay(15, _shutdown.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _workers.TryRemove(workerId, out _);
        }
    }

    private bool TryReadNext(out WorkerItem item)
    {
        foreach (var queue in _queues)
        {
            if (queue.Reader.TryRead(out item!))
            {
                Interlocked.Decrement(ref _queueDepth);
                _backpressure.Release();
                return true;
            }
        }

        item = default!;
        return false;
    }

    private static async Task ExecuteAsync(WorkerItem item)
    {
        using var timeout = new CancellationTokenSource(item.Timeout);
        try
        {
            var result = await item.Work(timeout.Token).WaitAsync(item.Timeout, timeout.Token).ConfigureAwait(false);
            item.Completion.TrySetResult(result);
        }
        catch (OperationCanceledException)
        {
            item.Completion.TrySetException(new TimeoutException("Worker task timed out."));
        }
        catch (Exception exception)
        {
            item.Completion.TrySetException(exception);
        }
    }

    private sealed record WorkerItem(
        Func<CancellationToken, Task<object?>> Work,
        TaskCompletionSource<object?> Completion,
        TimeSpan Timeout);
}
