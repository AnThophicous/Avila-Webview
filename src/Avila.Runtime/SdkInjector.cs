using System.Reflection;
using System.Text.Json;
using Avila.Security;

namespace Avila.Runtime;

public static class SdkInjector
{
    public static string LoadSdkSource()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("avila.js", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            throw new InvalidOperationException("Embedded avila.js SDK was not found.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Embedded avila.js SDK could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string BuildInjectedScript(AvilaProject project, string mode, string capability)
    {
        var sdk = LoadSdkSource();
        var bootstrap = new
        {
            capability,
            mode,
            maxPayloadBytes = project.Manifest.Security.MaxPayloadBytes,
            bridgeTimeoutMs = project.Manifest.Security.BridgeTimeoutMs,
            localHost = OriginPolicy.VirtualHost,
            dragRegions = project.Manifest.Window.DragRegions,
            noDragRegions = project.Manifest.Window.NoDragRegions
        };

        var bootstrapJson = JsonSerializer.Serialize(bootstrap);
        return $$"""
(() => {
  "use strict";
  if (globalThis.location && globalThis.location.hostname !== "{{OriginPolicy.VirtualHost}}") {
    return;
  }
  globalThis.__AVILA_BOOTSTRAP__ = Object.freeze({{bootstrapJson}});
{{sdk}}
})();
""";
    }
}
