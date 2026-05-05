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
        var devHooks = mode.Equals("dev", StringComparison.OrdinalIgnoreCase) ? BuildDevErrorHooksScript() : "";
        var bootstrap = new
        {
            capability,
            mode,
            maxPayloadBytes = project.Manifest.Security.MaxPayloadBytes,
            bridgeTimeoutMs = project.Manifest.Security.BridgeTimeoutMs,
            sandbox = project.Manifest.Security.Sandbox,
            contextIsolation = project.Manifest.Security.ContextIsolation,
            preserveStateOnReload = project.Manifest.Performance.PreserveStateOnReload,
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
{{devHooks}}
{{sdk}}
})();
""";
    }

    private static string BuildDevErrorHooksScript() => """
  const avilaDevPost = (kind, error) => {
    const payload = {
      type: "avila.dev.error",
      kind,
      message: error && error.message ? String(error.message) : "Unknown error",
      filename: error && error.filename ? String(error.filename) : "",
      lineno: error && Number.isFinite(error.lineno) ? Number(error.lineno) : 0,
      colno: error && Number.isFinite(error.colno) ? Number(error.colno) : 0,
      stack: error && error.stack ? String(error.stack) : "",
      url: globalThis.location ? String(globalThis.location.href) : ""
    };

    try {
      globalThis.chrome?.webview?.postMessage(payload);
    } catch {
    }
  };

  globalThis.addEventListener("error", event => {
    avilaDevPost("error", {
      message: event.message,
      filename: event.filename,
      lineno: event.lineno,
      colno: event.colno,
      stack: event.error && event.error.stack ? event.error.stack : ""
    });
  });

  globalThis.addEventListener("unhandledrejection", event => {
    const reason = event.reason instanceof Error ? event.reason : new Error(String(event.reason ?? "Unhandled rejection"));
    avilaDevPost("unhandledrejection", {
      message: reason.message,
      stack: reason.stack || ""
    });
  });

  let lastActivityPost = 0;
  const postActivity = () => {
    const now = Date.now();
    if (now - lastActivityPost < 1500) {
      return;
    }

    lastActivityPost = now;
    try {
      globalThis.chrome?.webview?.postMessage({
        type: "avila.activity",
        timestamp: now,
        url: globalThis.location ? String(globalThis.location.href) : ""
      });
    } catch {
    }
  };

  for (const name of ["pointerdown", "pointermove", "keydown", "wheel", "touchstart"]) {
    globalThis.addEventListener(name, postActivity, { passive: true, capture: true });
  }

  for (const level of ["error", "warn"]) {
    const original = global.console && typeof global.console[level] === "function"
      ? global.console[level].bind(global.console)
      : null;

    if (!original) {
      continue;
    }

    global.console[level] = (...args) => {
      try {
        avilaDevPost(`console.${level}`, {
          message: args.map(value => {
            try {
              return typeof value === "string" ? value : JSON.stringify(value);
            } catch (_) {
              return String(value);
            }
          }).join(" "),
          stack: "",
          filename: global.location ? String(global.location.pathname || "") : "",
          lineno: 0,
          colno: 0
        });
      } catch {
      }

      return original(...args);
    };
  }
""";
}
