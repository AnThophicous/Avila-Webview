using System.Diagnostics;
using Avila.Bridge;
using Avila.Core;
using Avila.Diagnostics;
using Avila.Security;

namespace Avila.Runtime;

public sealed class NodeHostGateway : INodeHostGateway
{
    private readonly AvilaProject _project;
    private readonly RuntimeOptions _options;
    private readonly SafeLogger _logger;

    public NodeHostGateway(AvilaProject project, RuntimeOptions options, SafeLogger logger)
    {
        _project = project;
        _options = options;
        _logger = logger;
    }

    public async Task<NodeRunResult> RunAsync(string script, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        if (!_project.Manifest.Node.Enabled)
        {
            throw new InvalidOperationException("NodeHost is disabled by node.enabled.");
        }

        if (!IsRuntimeAllowed())
        {
            throw new InvalidOperationException($"NodeHost is disabled in {_options.Mode} mode by node.mode.");
        }

        if (!_project.Manifest.Node.AllowedScripts.Contains(script, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Script is not allowed by node.allowedScripts: {script}");
        }

        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "npm.cmd" : "npm",
            WorkingDirectory = _project.RootPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--silent");
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add("--");
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        _logger.Trace($"node host run: {script}");
        if (!process.Start())
        {
            throw new InvalidOperationException("NodeHost could not be started.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new NodeRunResult(
            script,
            process.ExitCode,
            _logger.Sanitize(await stdoutTask.ConfigureAwait(false)),
            _logger.Sanitize(await stderrTask.ConfigureAwait(false)));
    }

    private bool IsRuntimeAllowed()
    {
        return _project.Manifest.Node.Mode switch
        {
            "always" => true,
            "dev-build" => _options.Mode is "dev" or "build" or "package",
            "build-only" => _options.Mode is "build" or "package",
            _ => _options.Mode.Equals("dev", StringComparison.OrdinalIgnoreCase)
        };
    }
}
