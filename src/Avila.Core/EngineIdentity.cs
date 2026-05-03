namespace Avila.Core;

public static class EngineIdentity
{
    public const string EngineName = "Avila";
    public const string ToolingCommand = "avila";
    public const string HostExecutable = "Avila.exe";
    public const string AppDataRoot = "Avila";
    public const string DefaultAppName = "Avila App";

    public static string ResolveProcessName(string? configuredName, string fallbackAppName)
    {
        var candidate = string.IsNullOrWhiteSpace(configuredName) ? fallbackAppName : configuredName;
        return SanitizeFileName(candidate, "AvilaApp");
    }

    public static string ResolveWindowTitle(string? configuredTitle, string fallbackAppName)
    {
        var candidate = string.IsNullOrWhiteSpace(configuredTitle) ? fallbackAppName : configuredTitle;
        return string.IsNullOrWhiteSpace(candidate) ? DefaultAppName : candidate;
    }

    public static string SanitizeFileName(string value, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }
}
