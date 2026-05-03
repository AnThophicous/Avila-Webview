namespace Avila.Workers;

public sealed class WorkerPoolOptions
{
    public int MinWorkers { get; init; } = 1;

    public int MaxWorkers { get; init; } = Math.Max(2, Environment.ProcessorCount);

    public int MaxQueueLength { get; init; } = 256;

    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan IdleShrinkDelay { get; init; } = TimeSpan.FromSeconds(8);
}
