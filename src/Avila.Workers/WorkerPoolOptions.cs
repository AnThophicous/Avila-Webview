namespace Avila.Workers;

public sealed class WorkerPoolOptions
{
    public int MinWorkers { get; init; } = 1;

    public int MaxWorkers { get; init; } = Math.Max(2, Environment.ProcessorCount);

    public int StartupWorkers { get; init; } = Math.Max(1, Environment.ProcessorCount);

    public int OpenWorkers { get; init; } = Math.Max(1, Environment.ProcessorCount / 3);

    public int IdleWorkers { get; init; } = 1;

    public int MaxQueueLength { get; init; } = 256;

    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan IdleShrinkDelay { get; init; } = TimeSpan.FromSeconds(8);
}
