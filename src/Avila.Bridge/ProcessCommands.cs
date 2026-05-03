using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Avila.Bridge;

public sealed partial class BridgeCommandRegistry
{
    private static readonly ConcurrentDictionary<string, Process> BackgroundProcesses = new(StringComparer.Ordinal);

    private void RegisterProcessCommands()
    {
        Register("process.spawn", async (context, request, token) =>
            await RunProcessAsync(context, request.Payload, token).ConfigureAwait(false));

        Register("process.execFile", async (context, request, token) =>
        {
            using var document = JsonDocument.Parse(request.Payload.GetRawText());
            var root = document.RootElement.Clone();
            return await RunProcessAsync(context, root, token, forceForeground: true).ConfigureAwait(false);
        });

        Register("process.kill", async (_, request, token) =>
        {
            token.ThrowIfCancellationRequested();
            var id = PayloadReader.GetString(request.Payload, "id", 96);
            if (!BackgroundProcesses.TryRemove(id, out var process))
            {
                return await Task.FromResult<object?>(new { killed = false });
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            finally
            {
                process.Dispose();
            }

            return new { killed = true };
        });
    }

    private static async Task<object?> RunProcessAsync(
        BridgeCommandContext context,
        JsonElement payload,
        CancellationToken cancellationToken,
        bool forceForeground = false)
    {
        var command = PayloadReader.GetString(payload, "command", 512);
        EnsureCommandAllowed(context, command);
        var args = ReadStringArray(payload, "args", maxItems: 64, maxLength: 1024);
        var cwd = ResolveProcessCwd(context, payload);
        var timeoutMs = GetOptionalInt(payload, "timeoutMs", 100, context.Project.Manifest.Process.TimeoutMs, context.Project.Manifest.Process.TimeoutMs);
        var background = !forceForeground && PayloadReader.GetOptionalBoolean(payload, "background", false);
        var env = ReadAllowedEnvironment(context, payload);

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = cwd,
            RedirectStandardOutput = !background,
            RedirectStandardError = !background
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (var pair in env)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = background
        };

        if (!process.Start())
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Process could not be started.");
        }

        if (background)
        {
            var id = Guid.NewGuid().ToString("N");
            process.Exited += (_, _) =>
            {
                BackgroundProcesses.TryRemove(id, out _);
                process.Dispose();
            };
            BackgroundProcesses[id] = process;
            return new { id, pid = process.Id, background = true };
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);
        var stdoutTask = ReadLimitedAsync(process.StandardOutput, context.Project.Manifest.Process.MaxOutputBytes, timeout.Token);
        var stderrTask = ReadLimitedAsync(process.StandardError, context.Project.Manifest.Process.MaxOutputBytes, timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw new BridgeException(BridgeErrorCodes.Timeout, "Process timed out.");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        var exitCode = process.ExitCode;
        process.Dispose();
        return new
        {
            exitCode,
            stdout = stdout.Text,
            stderr = stderr.Text,
            stdoutTruncated = stdout.Truncated,
            stderrTruncated = stderr.Truncated
        };
    }

    private static void EnsureCommandAllowed(BridgeCommandContext context, string command)
    {
        if (context.Project.Manifest.Process.AllowedCommands.Length == 0)
        {
            throw new BridgeException(BridgeErrorCodes.PermissionDenied, "No commands are allowed by process.allowedCommands.");
        }

        var fileName = Path.GetFileName(command);
        var fileNameNoExtension = Path.GetFileNameWithoutExtension(command);
        var allowed = context.Project.Manifest.Process.AllowedCommands.Any(item =>
            command.Equals(item, StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(item, StringComparison.OrdinalIgnoreCase)
            || fileNameNoExtension.Equals(item, StringComparison.OrdinalIgnoreCase));

        if (!allowed)
        {
            throw new BridgeException(BridgeErrorCodes.PermissionDenied, $"Command is not allowed: {fileName}");
        }
    }

    private static string ResolveProcessCwd(BridgeCommandContext context, JsonElement payload)
    {
        var root = PayloadReader.GetString(payload, "cwdRoot", 32, required: false);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = "app";
        }

        if (!context.Project.Manifest.Process.AllowedCwdRoots.Contains(root, StringComparer.OrdinalIgnoreCase))
        {
            throw new BridgeException(BridgeErrorCodes.PermissionDenied, $"cwd root is not allowed by process.allowedCwdRoots: {root}");
        }

        var relative = PayloadReader.GetString(payload, "cwd", 512, required: false);
        var rootPath = context.Runtime.GetPath(root);
        var cwd = ResolveProcessPathInsideRoot(rootPath, relative);
        if (!Directory.Exists(cwd))
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, "cwd was not found.");
        }

        return cwd;
    }

    private static IReadOnlyDictionary<string, string> ReadAllowedEnvironment(BridgeCommandContext context, JsonElement payload)
    {
        if (!payload.TryGetProperty("env", out var envValue) || envValue.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new Dictionary<string, string>();
        }

        if (envValue.ValueKind != JsonValueKind.Object)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, "payload.env must be an object.");
        }

        var output = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in envValue.EnumerateObject())
        {
            if (!context.Project.Manifest.Process.AllowedEnv.Contains(item.Name, StringComparer.Ordinal))
            {
                throw new BridgeException(BridgeErrorCodes.PermissionDenied, $"Environment variable is not allowed: {item.Name}");
            }

            if (item.Value.ValueKind != JsonValueKind.String)
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.env.{item.Name} must be a string.");
            }

            output[item.Name] = item.Value.GetString() ?? "";
        }

        return output;
    }

    private static string[] ReadStringArray(JsonElement payload, string name, int maxItems, int maxLength)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name} must be an array.");
        }

        var items = value.EnumerateArray().Take(maxItems + 1).ToArray();
        if (items.Length > maxItems)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name} has too many items.");
        }

        return items.Select((item, index) =>
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name}[{index}] must be a string.");
            }

            var text = item.GetString() ?? "";
            if (text.Length > maxLength)
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name}[{index}] is too long.");
            }

            return text;
        }).ToArray();
    }

    private static async Task<LimitedText> ReadLimitedAsync(StreamReader reader, int maxBytes, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];
        var bytes = 0;
        var truncated = false;

        while (!reader.EndOfStream)
        {
            var read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            var chunk = new string(buffer, 0, read);
            var chunkBytes = Encoding.UTF8.GetByteCount(chunk);
            if (bytes + chunkBytes <= maxBytes)
            {
                builder.Append(chunk);
                bytes += chunkBytes;
            }
            else
            {
                truncated = true;
            }
        }

        return new LimitedText(builder.ToString(), truncated);
    }

    private static string ResolveProcessPathInsideRoot(string rootPath, string relativePath)
    {
        var normalized = (relativePath ?? "").Replace('\\', '/').TrimStart('/');
        if (normalized is "" or ".")
        {
            return Path.GetFullPath(rootPath);
        }

        if (Path.IsPathRooted(normalized))
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Path must be relative to its root.");
        }

        var segments = normalized.Split('/');
        if (segments.Any(segment => segment is "" or "." or ".."))
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Path contains an unsafe segment.");
        }

        var root = Path.GetFullPath(rootPath);
        var resolved = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!resolved.Equals(root, comparison) && !resolved.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
        {
            throw new BridgeException(BridgeErrorCodes.PermissionDenied, "Path escapes the allowed root.");
        }

        return resolved;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private sealed record LimitedText(string Text, bool Truncated);
}
