namespace Avila.Diagnostics;

public sealed record SecurityBuildReport(
    string SecurityScore,
    string StartupEstimate,
    IReadOnlyList<string> DangerousPermissions,
    bool RemoteContentEnabled,
    string ExeMode,
    IReadOnlyList<string> Warnings);

public static class ReportFormatter
{
    public static string Format(SecurityBuildReport report)
    {
        var dangerous = report.DangerousPermissions.Count == 0
            ? "none"
            : string.Join(", ", report.DangerousPermissions);

        return string.Join(Environment.NewLine,
            $"Security score: {report.SecurityScore}",
            $"Startup estimate: {report.StartupEstimate}",
            $"Dangerous permissions: {dangerous}",
            $"Remote content: {(report.RemoteContentEnabled ? "enabled" : "disabled")}",
            $"EXE mode: {report.ExeMode}",
            report.Warnings.Count == 0 ? "Warnings: none" : $"Warnings: {string.Join("; ", report.Warnings)}");
    }
}
