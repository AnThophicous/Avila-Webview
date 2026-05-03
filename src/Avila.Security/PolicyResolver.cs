namespace Avila.Security;

public enum PolicyIssueSeverity
{
    Allow,
    Deny,
    Warning,
    Error,
    Suggestion
}

public sealed record PolicyIssue(string Code, PolicyIssueSeverity Severity, string Message, string Scope);

public sealed record EffectiveRuntimePolicy(
    string Mode,
    bool RemoteNavigationAllowed,
    bool NativeFetchAllowed,
    bool NodeHostAllowed,
    bool DevToolsAllowed,
    IReadOnlyList<string> AllowedNetworkOrigins,
    IReadOnlyList<string> AllowedNodeScripts);

public sealed record PolicyResolution(
    EffectiveRuntimePolicy Effective,
    IReadOnlyList<PolicyIssue> Issues)
{
    public bool HasErrors => Issues.Any(issue => issue.Severity == PolicyIssueSeverity.Error || issue.Severity == PolicyIssueSeverity.Deny);
}

public static class PolicyResolver
{
    public static PolicyResolution Resolve(AvilaProject project, string mode)
    {
        var manifest = project.Manifest;
        var issues = new List<PolicyIssue>();
        var permissionPolicy = new PermissionPolicy(manifest);
        var browserMode = manifest.Mode.Equals("browser-app", StringComparison.OrdinalIgnoreCase);
        var remoteNavigationAllowed = browserMode ? manifest.Browser.Navigation : manifest.Security.AllowRemoteContent;
        var nativeFetchAllowed = permissionPolicy.IsAllowed("network.fetch");
        var nodeHostAllowed = manifest.Node.Enabled && NodeModeAllowsRuntime(manifest.Node.Mode, mode);
        var devToolsAllowed = manifest.Security.DevTools && !mode.Equals("production", StringComparison.OrdinalIgnoreCase);

        if (permissionPolicy.IsAllowed("browser.navigate") && !remoteNavigationAllowed)
        {
            issues.Add(new PolicyIssue(
                "AVILA-POLICY-001",
                PolicyIssueSeverity.Error,
                "browser.navigate is enabled, but security.allowRemoteContent is false. Remote navigation is blocked.",
                "remote-navigation"));
        }

        if (browserMode && string.IsNullOrWhiteSpace(manifest.Browser.Url))
        {
            issues.Add(new PolicyIssue(
                "AVILA-POLICY-002",
                PolicyIssueSeverity.Error,
                "mode browser-app requires browser.url.",
                "browser"));
        }

        if (browserMode && !manifest.Browser.Navigation)
        {
            issues.Add(new PolicyIssue(
                "AVILA-POLICY-003",
                PolicyIssueSeverity.Warning,
                "browser-app mode has browser.navigation disabled, so in-app navigation is off.",
                "browser"));
        }

        if (nativeFetchAllowed && manifest.Network.AllowedOrigins.Length == 0)
        {
            issues.Add(new PolicyIssue(
                "AVILA-POLICY-010",
                PolicyIssueSeverity.Warning,
                "network.fetch is enabled, but network.allowedOrigins is empty. Native fetch will be allowed only after you define destinations.",
                "network"));
        }

        if (manifest.Security.DevTools && mode.Equals("production", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new PolicyIssue(
                "AVILA-POLICY-020",
                PolicyIssueSeverity.Warning,
                "security.devtools is enabled in production mode.",
                "devtools"));
        }

        if (manifest.Node.Enabled)
        {
            if (manifest.Node.AllowedScripts.Length == 0)
            {
                issues.Add(new PolicyIssue(
                    "AVILA-POLICY-030",
                    PolicyIssueSeverity.Error,
                    "node.enabled is true, but node.allowedScripts is empty.",
                    "node"));
            }

            if (!NodeModeAllowsRuntime(manifest.Node.Mode, mode))
            {
                issues.Add(new PolicyIssue(
                    "AVILA-POLICY-031",
                    PolicyIssueSeverity.Warning,
                    $"node.mode is '{manifest.Node.Mode}', so the NodeHost is disabled in {mode} mode.",
                    "node"));
            }
        }

        if (manifest.FileSystem.AllowedRoots.Length == 0 && manifest.Permissions.Any(pair => pair.Key.StartsWith("fs.", StringComparison.Ordinal) && pair.Value))
        {
            issues.Add(new PolicyIssue(
                "AVILA-POLICY-040",
                PolicyIssueSeverity.Error,
                "fs permissions are enabled, but fs.allowedRoots is empty.",
                "fs"));
        }

        if (manifest.Process.AllowedCommands.Length == 0 && permissionPolicy.IsAllowed("process.spawn"))
        {
            issues.Add(new PolicyIssue(
                "AVILA-POLICY-050",
                PolicyIssueSeverity.Error,
                "process.spawn is enabled, but process.allowedCommands is empty.",
                "process"));
        }

        return new PolicyResolution(
            new EffectiveRuntimePolicy(
                mode,
                remoteNavigationAllowed,
                nativeFetchAllowed,
                nodeHostAllowed,
                devToolsAllowed,
                manifest.Network.AllowedOrigins.ToArray(),
                manifest.Node.AllowedScripts.ToArray()),
            issues);
    }

    private static bool NodeModeAllowsRuntime(string nodeMode, string runtimeMode)
    {
        if (nodeMode.Equals("always", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (nodeMode.Equals("dev-build", StringComparison.OrdinalIgnoreCase))
        {
            return runtimeMode.Equals("dev", StringComparison.OrdinalIgnoreCase)
                || runtimeMode.Equals("build", StringComparison.OrdinalIgnoreCase);
        }

        if (nodeMode.Equals("build-only", StringComparison.OrdinalIgnoreCase))
        {
            return runtimeMode.Equals("build", StringComparison.OrdinalIgnoreCase)
                || runtimeMode.Equals("package", StringComparison.OrdinalIgnoreCase);
        }

        return nodeMode.Equals("dev-only", StringComparison.OrdinalIgnoreCase)
            && runtimeMode.Equals("dev", StringComparison.OrdinalIgnoreCase);
    }
}
