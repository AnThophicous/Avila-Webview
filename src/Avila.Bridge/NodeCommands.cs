using System.Diagnostics;
using System.Text.Json;

namespace Avila.Bridge;

public sealed partial class BridgeCommandRegistry
{
    private void RegisterNodeCommands()
    {
        Register("node.run", async (context, request, token) =>
        {
            if (!context.Project.Manifest.Node.Enabled)
            {
                throw new BridgeException(BridgeErrorCodes.PermissionDenied, "NodeHost is disabled by node.enabled.");
            }

            var script = PayloadReader.GetString(request.Payload, "script", 64);
            if (!context.Project.Manifest.Node.AllowedScripts.Contains(script, StringComparer.Ordinal))
            {
                throw new BridgeException(BridgeErrorCodes.PermissionDenied, $"Script is not allowed by node.allowedScripts: {script}");
            }

            var args = ReadNodeArgs(request.Payload);
            var result = await context.NodeHost.RunAsync(script, args, token).ConfigureAwait(false);
            return new
            {
                script = result.Script,
                exitCode = result.ExitCode,
                stdout = result.StandardOutput,
                stderr = result.StandardError,
                succeeded = result.Succeeded
            };
        });
    }

    private static IReadOnlyList<string> ReadNodeArgs(JsonElement payload)
    {
        if (!payload.TryGetProperty("args", out var args) || args.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (args.ValueKind != JsonValueKind.Array)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, "payload.args must be an array.");
        }

        return args.EnumerateArray()
            .Select((item, index) =>
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.args[{index}] must be a string.");
                }

                var text = item.GetString() ?? "";
                if (text.Length > 1024)
                {
                    throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.args[{index}] is too long.");
                }

                return text;
            })
            .ToArray();
    }
}
