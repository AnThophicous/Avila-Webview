using System.Text.Json;
using Avila.Diagnostics;
using Avila.Security;

namespace Avila.Packager;

public sealed record BuildResult(AvilaProject Project, string BuildDirectory, SecurityBuildReport Report);

public sealed record PackageResult(AvilaProject Project, string ExePath, string DistDirectory, SecurityBuildReport Report);

public sealed class BuildService
{
    private readonly SafeLogger _logger;

    public BuildService(SafeLogger logger)
    {
        _logger = logger;
    }

    public async Task<BuildResult> BuildAsync(string? projectPath, CancellationToken cancellationToken = default)
    {
        var project = await ProjectLocator.LoadProjectAsync(projectPath, cancellationToken).ConfigureAwait(false);
        var validation = ManifestLoader.Validate(project);
        if (!validation.IsValid)
        {
            var errors = string.Join(Environment.NewLine, validation.Errors.Select(error => $"{error.Code}: {error.Message}"));
            throw new InvalidOperationException(errors);
        }

        foreach (var warning in validation.Warnings)
        {
            _logger.Warning($"{warning.Code}: {warning.Message}");
        }

        var policy = PolicyResolver.Resolve(project, "build");
        foreach (var issue in policy.Issues)
        {
            _logger.Warning($"{issue.Code} [{issue.Severity}] {issue.Message}");
        }

        var buildDirectory = Path.Combine(project.RootPath, "build");
        Directory.CreateDirectory(buildDirectory);

        var report = CreateReport(project, validation, policy);
        await File.WriteAllTextAsync(
            Path.Combine(buildDirectory, "avila.build.json"),
            JsonSerializer.Serialize(new
            {
                generatedAt = DateTimeOffset.UtcNow,
                project = project.Manifest.App,
                report
            }, ManifestLoader.JsonOptions),
            cancellationToken).ConfigureAwait(false);

        await File.WriteAllTextAsync(
            Path.Combine(buildDirectory, "avila.build.txt"),
            ReportFormatter.Format(report),
            cancellationToken).ConfigureAwait(false);

        return new BuildResult(project, buildDirectory, report);
    }

    public static SecurityBuildReport CreateReport(AvilaProject project, ManifestValidationResult validation, PolicyResolution? policy = null)
    {
        var dangerous = PermissionPolicy.GetDangerousEnabledCommands(project.Manifest).ToArray();
        var score = dangerous.Length == 0 && !project.Manifest.Security.AllowRemoteContent
            ? validation.Warnings.Any() ? "A-" : "A"
            : "B";

        if (!project.Manifest.Security.DefaultPolicy.Equals("deny", StringComparison.OrdinalIgnoreCase))
        {
            score = "C";
        }

        var startup = project.Manifest.Performance.StartupBoost && project.Manifest.Build.ReadyToRun ? "fast" : "standard";
        var exeMode = project.Manifest.Build.SingleFile && project.Manifest.Build.SelfContained
            ? "single-file self-contained"
            : project.Manifest.Build.SingleFile ? "single-file framework-dependent" : "folder";

        return new SecurityBuildReport(
            score,
            startup,
            dangerous,
            project.Manifest.Security.AllowRemoteContent,
            exeMode,
            validation.Warnings.Select(warning => warning.Message)
                .Concat(policy?.Issues.Select(issue => $"{issue.Code}: {issue.Message}") ?? [])
                .ToArray());
    }
}
