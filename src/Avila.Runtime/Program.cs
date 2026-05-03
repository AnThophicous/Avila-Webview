using Avila.Diagnostics;
using Avila.Security;
using Avila.Workers;

namespace Avila.Runtime;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var options = RuntimeOptions.Parse(args);
        try
        {
            var project = ManifestLoader.LoadProjectAsync(options.ProjectPath).GetAwaiter().GetResult();
            var validation = ManifestLoader.Validate(project);
            if (!validation.IsValid)
            {
                var message = string.Join(Environment.NewLine, validation.Errors.Select(error => $"{error.Code}: {error.Message}"));
                MessageBox.Show(message, "Invalid avila.json", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }

            var logDirectory = options.Mode.Equals("dev", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(project.RootPath, "logs")
                : Path.Combine(AppContext.BaseDirectory, "logs");

            var logger = new SafeLogger(
                logDirectory,
                sanitize: project.Manifest.Security.SanitizeLogs,
                verbose: options.Mode.Equals("dev", StringComparison.OrdinalIgnoreCase));

            foreach (var warning in validation.Warnings)
            {
                logger.Warning($"{warning.Code}: {warning.Message}");
            }

            var policy = PolicyResolver.Resolve(project, options.Mode);
            var policyErrors = policy.Issues.Where(issue => issue.Severity is PolicyIssueSeverity.Error or PolicyIssueSeverity.Deny).ToArray();
            foreach (var issue in policy.Issues)
            {
                var prefix = issue.Severity switch
                {
                    PolicyIssueSeverity.Error => "error",
                    PolicyIssueSeverity.Deny => "deny",
                    PolicyIssueSeverity.Warning => "warning",
                    PolicyIssueSeverity.Suggestion => "suggestion",
                    _ => "allow"
                };

                logger.Warning($"{issue.Code} [{prefix}] {issue.Message}");
            }

            if (policyErrors.Length > 0)
            {
                var message = string.Join(Environment.NewLine, policyErrors.Select(issue => $"{issue.Code}: {issue.Message}"));
                MessageBox.Show(message, "Avila policy resolver", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 3;
            }

            var diagnostics = new DiagnosticsCollector();
            var workers = new AvilaWorkerPool(new WorkerPoolOptions
            {
                MinWorkers = project.Manifest.Performance.WarmWorkerPool ? project.Manifest.Performance.WorkerPoolMin : 0,
                MaxWorkers = project.Manifest.Performance.WorkerPoolMax,
                IdleShrinkDelay = TimeSpan.FromMilliseconds(project.Manifest.Performance.ShrinkDelayMs),
                DefaultTimeout = TimeSpan.FromMilliseconds(project.Manifest.Security.BridgeTimeoutMs)
            }, logger);

            var capabilities = new CapabilityManager();
            Application.Run(new AvilaApplicationForm(project, options, capabilities, logger, diagnostics, workers, logDirectory));
            return 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Avila", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
