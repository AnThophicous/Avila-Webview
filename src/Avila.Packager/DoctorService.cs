using Avila.Diagnostics;
using Avila.Security;

namespace Avila.Packager;

public sealed class DoctorService
{
    private readonly SafeLogger _logger;
    private readonly ProcessRunner _processRunner;

    public DoctorService(SafeLogger logger)
    {
        _logger = logger;
        _processRunner = new ProcessRunner(logger);
    }

    public async Task<IReadOnlyList<DoctorCheck>> RunAsync(string? projectPath, CancellationToken cancellationToken = default)
    {
        var checks = new List<DoctorCheck>
        {
            CheckWindowsArchitecture(),
            CheckWebView2Runtime()
        };

        checks.Add(await CheckDotNetSdkAsync(cancellationToken).ConfigureAwait(false));

        try
        {
            var project = await ProjectLocator.LoadProjectAsync(projectPath, cancellationToken).ConfigureAwait(false);
            var validation = ManifestLoader.Validate(project);
            checks.Add(new DoctorCheck("avila.json", validation.IsValid, validation.IsValid ? "Manifest is valid." : "Manifest has errors."));
            checks.AddRange(validation.Issues.Select(issue => new DoctorCheck(issue.Code, !issue.IsError, issue.Message)));

            var policy = PolicyResolver.Resolve(project, "production");
            checks.Add(new DoctorCheck("policy", !policy.HasErrors, policy.HasErrors ? "Policy graph has errors." : "Policy graph is valid."));
            checks.AddRange(policy.Issues.Select(issue =>
                new DoctorCheck(issue.Code, issue.Severity is not PolicyIssueSeverity.Error and not PolicyIssueSeverity.Deny, $"{issue.Severity}: {issue.Message}")));
        }
        catch (Exception exception)
        {
            checks.Add(new DoctorCheck("avila.json", false, exception.Message));
        }

        return checks;
    }

    private static DoctorCheck CheckWindowsArchitecture()
    {
        var ok = OperatingSystem.IsWindows() && Environment.Is64BitOperatingSystem;
        var message = ok ? "Windows x64 detected." : "Avila requires Windows x64.";
        return new DoctorCheck("windows", ok, message);
    }

    private static DoctorCheck CheckWebView2Runtime()
    {
        var locations = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "EdgeWebView", "Application"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "EdgeWebView", "Application")
        };

        var found = locations
            .Where(Directory.Exists)
            .SelectMany(location => Directory.EnumerateFiles(location, "msedgewebview2.exe", SearchOption.AllDirectories))
            .FirstOrDefault();

        return found is null
            ? new DoctorCheck("webview2", false, "WebView2 Runtime was not found.")
            : new DoctorCheck("webview2", true, $"WebView2 Runtime found: {found}");
    }

    private async Task<DoctorCheck> CheckDotNetSdkAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _processRunner.RunAsync("dotnet", "--version", Environment.CurrentDirectory, cancellationToken).ConfigureAwait(false);
            return result.Succeeded
                ? new DoctorCheck("dotnet", true, $".NET SDK {result.StandardOutput.Trim()} detected.")
                : new DoctorCheck("dotnet", false, ".NET SDK is not available.");
        }
        catch (Exception exception)
        {
            return new DoctorCheck("dotnet", false, exception.Message);
        }
    }
}

public sealed record DoctorCheck(string Name, bool Passed, string Message);
