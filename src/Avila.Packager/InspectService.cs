using Avila.Bridge;
using Avila.Security;

namespace Avila.Packager;

public sealed class InspectService
{
    public async Task<string> InspectApisAsync(string? projectPath, CancellationToken cancellationToken = default)
    {
        var project = await ProjectLocator.LoadProjectAsync(projectPath, cancellationToken).ConfigureAwait(false);
        var permissions = new PermissionPolicy(project.Manifest);
        var commands = new BridgeCommandRegistry().Commands
            .OrderBy(command => command.Name, StringComparer.Ordinal)
            .Select(command =>
            {
                var allowed = !command.RequiresPermission || permissions.IsAllowed(command.Name);
                var risk = PermissionPolicy.IsDangerous(command.Name) ? "dangerous" : "normal";
                var scope = command.AllowRemote ? "remote" : "local";
                return $"{command.Name} [{(allowed ? "allowed" : "denied")}, {risk}, {scope}]";
            });

        return string.Join(Environment.NewLine, commands);
    }

    public async Task<string> InspectPermissionsAsync(string? projectPath, CancellationToken cancellationToken = default)
    {
        var project = await ProjectLocator.LoadProjectAsync(projectPath, cancellationToken).ConfigureAwait(false);
        var validation = ManifestLoader.Validate(project);
        var permissions = new PermissionPolicy(project.Manifest);
        var commands = new BridgeCommandRegistry().Commands
            .Where(command => command.RequiresPermission)
            .OrderBy(command => command.Name, StringComparer.Ordinal)
            .Select(command =>
            {
                var explicitValue = project.Manifest.Permissions.TryGetValue(command.Name, out var value)
                    ? value ? "explicit allow" : "explicit deny"
                    : "default";
                var allowed = permissions.IsAllowed(command.Name) ? "allowed" : "denied";
                var risk = PermissionPolicy.IsDangerous(command.Name) ? "dangerous" : "normal";
                return $"{command.Name}: {allowed} ({explicitValue}, {risk})";
            });

        var issues = validation.Issues.Select(issue => $"{(issue.IsError ? "error" : "warning")}: {issue.Code} - {issue.Message}");
        return string.Join(Environment.NewLine, commands.Concat(["", "Manifest issues:"]).Concat(issues));
    }

    public async Task<string> InspectPackageAsync(string? projectPath, CancellationToken cancellationToken = default)
    {
        var project = await ProjectLocator.LoadProjectAsync(projectPath, cancellationToken).ConfigureAwait(false);
        var dist = Path.Combine(project.RootPath, "dist");
        if (!Directory.Exists(dist))
        {
            return "Package dist folder was not found. Run avila package first.";
        }

        var files = Directory.EnumerateFiles(dist, "*", SearchOption.AllDirectories).ToArray();
        var totalBytes = files.Sum(file => new FileInfo(file).Length);
        var suspicious = PackageService.FindDisallowedPackageFiles(dist).ToArray();
        var lines = new List<string>
        {
            $"dist: {dist}",
            $"files: {files.Length}",
            $"sizeBytes: {totalBytes}",
            $"suspicious: {suspicious.Length}"
        };
        lines.AddRange(suspicious.Select(file => $"blocked: {Path.GetRelativePath(dist, file)}"));
        return string.Join(Environment.NewLine, lines);
    }
}
