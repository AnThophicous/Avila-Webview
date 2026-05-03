using System.Diagnostics;
using Avila.Diagnostics;

namespace Avila.Packager;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

public sealed class ProcessRunner
{
    private readonly SafeLogger _logger;

    public ProcessRunner(SafeLogger logger)
    {
        _logger = logger;
    }

    public async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.Trace($"{fileName} {arguments}");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new ProcessResult(
            process.ExitCode,
            _logger.Sanitize(await outputTask.ConfigureAwait(false)),
            _logger.Sanitize(await errorTask.ConfigureAwait(false)));
    }
}
