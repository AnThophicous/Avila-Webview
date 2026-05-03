using System.Text.RegularExpressions;

namespace Avila.Diagnostics;

public sealed class SafeLogger
{
    private static readonly Regex[] RedactionPatterns =
    [
        new("(authorization\\s*[:=]\\s*)(bearer\\s+)?[^\\s,;]+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new("(cookie\\s*[:=]\\s*)[^\\r\\n;]+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new("((api[_-]?key|token|secret|password|passwd|pwd)\\s*[:=]\\s*)[^\\s,;\"']+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new("(\"(apiKey|token|secret|password|cookie|authorization)\"\\s*:\\s*\")[^\"]+(\")", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    ];

    private readonly object _sync = new();
    private readonly string? _logDirectory;
    private readonly bool _sanitize;
    private readonly bool _verbose;

    public SafeLogger(string? logDirectory = null, bool sanitize = true, bool verbose = false)
    {
        _logDirectory = logDirectory;
        _sanitize = sanitize;
        _verbose = verbose;

        if (!string.IsNullOrWhiteSpace(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }
    }

    public void Trace(string message) => Write(SafeLogLevel.Trace, message);

    public void Info(string message) => Write(SafeLogLevel.Info, message);

    public void Warning(string message) => Write(SafeLogLevel.Warning, message);

    public void Error(string message) => Write(SafeLogLevel.Error, message);

    public void Error(Exception exception, string message)
    {
        var text = _verbose ? $"{message}: {exception}" : $"{message}: {exception.Message}";
        Write(SafeLogLevel.Error, text);
    }

    public string Sanitize(string value)
    {
        if (!_sanitize || string.IsNullOrEmpty(value))
        {
            return value;
        }

        var sanitized = value;
        foreach (var pattern in RedactionPatterns)
        {
            sanitized = pattern.Replace(sanitized, match =>
            {
                if (match.Groups.Count >= 4 && match.Groups[3].Success)
                {
                    return $"{match.Groups[1].Value}[redacted]{match.Groups[3].Value}";
                }

                return $"{match.Groups[1].Value}[redacted]";
            });
        }

        return sanitized;
    }

    private void Write(SafeLogLevel level, string message)
    {
        if (level == SafeLogLevel.Trace && !_verbose)
        {
            return;
        }

        var line = $"{DateTimeOffset.UtcNow:O} [{level}] {Sanitize(message)}";

        lock (_sync)
        {
            Console.WriteLine(line);
            if (!string.IsNullOrWhiteSpace(_logDirectory))
            {
                File.AppendAllText(Path.Combine(_logDirectory, "avila.log"), line + Environment.NewLine);
            }
        }
    }
}
