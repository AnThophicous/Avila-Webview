using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avila.Security;

public sealed class AvilaManifest
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "appview";

    [JsonPropertyName("app")]
    public AppManifest App { get; set; } = new();

    [JsonPropertyName("window")]
    public WindowManifest Window { get; set; } = new();

    [JsonPropertyName("security")]
    public SecurityManifest Security { get; set; } = new();

    [JsonPropertyName("permissions")]
    public Dictionary<string, bool> Permissions { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("fs")]
    public FileSystemManifest FileSystem { get; set; } = new();

    [JsonPropertyName("native")]
    public NativeManifest Native { get; set; } = new();

    [JsonPropertyName("storage")]
    public StorageManifest Storage { get; set; } = new();

    [JsonPropertyName("process")]
    public ProcessManifest Process { get; set; } = new();

    [JsonPropertyName("network")]
    public NetworkManifest Network { get; set; } = new();

    [JsonPropertyName("performance")]
    public PerformanceManifest Performance { get; set; } = new();

    [JsonPropertyName("build")]
    public BuildManifest Build { get; set; } = new();

    [JsonPropertyName("package")]
    public PackageManifest Package { get; set; } = new();

    [JsonPropertyName("frontend")]
    public FrontendManifest Frontend { get; set; } = new();

    [JsonPropertyName("node")]
    public NodeManifest Node { get; set; } = new();

    [JsonPropertyName("browser")]
    public BrowserManifest Browser { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

public sealed class AppManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Avila App";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("entry")]
    public string Entry { get; set; } = "src/index.html";

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }
}

public sealed class WindowManifest
{
    [JsonPropertyName("width")]
    public int Width { get; set; } = 1100;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 720;

    [JsonPropertyName("minWidth")]
    public int MinWidth { get; set; } = 720;

    [JsonPropertyName("minHeight")]
    public int MinHeight { get; set; } = 480;

    [JsonPropertyName("center")]
    public bool Center { get; set; } = true;

    [JsonPropertyName("resizable")]
    public bool Resizable { get; set; } = true;

    [JsonPropertyName("borderless")]
    public bool Borderless { get; set; }

    [JsonPropertyName("roundedCorners")]
    public bool RoundedCorners { get; set; } = true;

    [JsonPropertyName("cornerPreference")]
    public string CornerPreference { get; set; } = "round";

    [JsonPropertyName("mica")]
    public bool Mica { get; set; } = true;

    [JsonPropertyName("micaAlt")]
    public bool MicaAlt { get; set; }

    [JsonPropertyName("acrylic")]
    public bool Acrylic { get; set; }

    [JsonPropertyName("blur")]
    public bool Blur { get; set; }

    [JsonPropertyName("transparent")]
    public bool Transparent { get; set; }

    [JsonPropertyName("customTitleBar")]
    public bool CustomTitleBar { get; set; } = true;

    [JsonPropertyName("draggable")]
    public bool Draggable { get; set; } = true;

    [JsonPropertyName("dragRegions")]
    public string[] DragRegions { get; set; } = ["[data-avila-drag]"];

    [JsonPropertyName("noDragRegions")]
    public string[] NoDragRegions { get; set; } = ["button", "input", "textarea", "select", "a", "[data-avila-no-drag]"];
}

public sealed class SecurityManifest
{
    [JsonPropertyName("defaultPolicy")]
    public string DefaultPolicy { get; set; } = "deny";

    [JsonPropertyName("allowRemoteContent")]
    public bool AllowRemoteContent { get; set; }

    [JsonPropertyName("allowedOrigins")]
    public string[] AllowedOrigins { get; set; } = ["avila://local"];

    [JsonPropertyName("devtools")]
    public bool DevTools { get; set; }

    [JsonPropertyName("sanitizeLogs")]
    public bool SanitizeLogs { get; set; } = true;

    [JsonPropertyName("maxPayloadBytes")]
    public int MaxPayloadBytes { get; set; } = 1_048_576;

    [JsonPropertyName("bridgeTimeoutMs")]
    public int BridgeTimeoutMs { get; set; } = 5_000;

    [JsonPropertyName("tokenSecurity")]
    public string TokenSecurity { get; set; } = "session-capability";
}

public sealed class FileSystemManifest
{
    [JsonPropertyName("allowedRoots")]
    public string[] AllowedRoots { get; set; } = ["app"];

    [JsonPropertyName("maxReadBytes")]
    public long MaxReadBytes { get; set; } = 5_242_880;

    [JsonPropertyName("maxWriteBytes")]
    public long MaxWriteBytes { get; set; } = 5_242_880;

    [JsonPropertyName("atomicWrites")]
    public bool AtomicWrites { get; set; } = true;
}

public sealed class NativeManifest
{
    [JsonPropertyName("tray")]
    public bool Tray { get; set; }

    [JsonPropertyName("notifications")]
    public bool Notifications { get; set; }

    [JsonPropertyName("shortcuts")]
    public bool Shortcuts { get; set; }

    [JsonPropertyName("globalShortcuts")]
    public bool GlobalShortcuts { get; set; }

    [JsonPropertyName("taskbar")]
    public bool Taskbar { get; set; } = true;
}

public sealed class StorageManifest
{
    [JsonPropertyName("allowedStores")]
    public string[] AllowedStores { get; set; } = ["data", "config", "cache"];

    [JsonPropertyName("maxStoreBytes")]
    public long MaxStoreBytes { get; set; } = 5_242_880;

    [JsonPropertyName("secrets")]
    public bool Secrets { get; set; }
}

public sealed class ProcessManifest
{
    [JsonPropertyName("allowedCommands")]
    public string[] AllowedCommands { get; set; } = [];

    [JsonPropertyName("allowedCwdRoots")]
    public string[] AllowedCwdRoots { get; set; } = ["app", "data", "temp"];

    [JsonPropertyName("allowedEnv")]
    public string[] AllowedEnv { get; set; } = [];

    [JsonPropertyName("timeoutMs")]
    public int TimeoutMs { get; set; } = 10_000;

    [JsonPropertyName("maxOutputBytes")]
    public int MaxOutputBytes { get; set; } = 1_048_576;
}

public sealed class NetworkManifest
{
    [JsonPropertyName("allowedOrigins")]
    public string[] AllowedOrigins { get; set; } = [];

    [JsonPropertyName("timeoutMs")]
    public int TimeoutMs { get; set; } = 10_000;

    [JsonPropertyName("redirectLimit")]
    public int RedirectLimit { get; set; } = 5;

    [JsonPropertyName("maxResponseBytes")]
    public int MaxResponseBytes { get; set; } = 1_048_576;

    [JsonPropertyName("allowLan")]
    public bool AllowLan { get; set; }
}

public sealed class PerformanceManifest
{
    [JsonPropertyName("startupBoost")]
    public bool StartupBoost { get; set; } = true;

    [JsonPropertyName("preloadBridge")]
    public bool PreloadBridge { get; set; } = true;

    [JsonPropertyName("warmWorkerPool")]
    public bool WarmWorkerPool { get; set; } = true;

    [JsonPropertyName("workerPoolMin")]
    public int WorkerPoolMin { get; set; } = 1;

    [JsonPropertyName("workerPoolMax")]
    public int WorkerPoolMax { get; set; } = 4;

    [JsonPropertyName("shrinkWorkersAfterStartup")]
    public bool ShrinkWorkersAfterStartup { get; set; } = true;

    [JsonPropertyName("shrinkDelayMs")]
    public int ShrinkDelayMs { get; set; } = 8_000;

    [JsonPropertyName("cacheStaticAssets")]
    public bool CacheStaticAssets { get; set; } = true;

    [JsonPropertyName("lazyLoadNativeModules")]
    public bool LazyLoadNativeModules { get; set; } = true;
}

public sealed class BuildManifest
{
    [JsonPropertyName("target")]
    public string Target { get; set; } = "win-x64";

    [JsonPropertyName("singleFile")]
    public bool SingleFile { get; set; } = true;

    [JsonPropertyName("selfContained")]
    public bool SelfContained { get; set; } = true;

    [JsonPropertyName("nativeAot")]
    public bool NativeAot { get; set; }

    [JsonPropertyName("trim")]
    public bool Trim { get; set; }

    [JsonPropertyName("readyToRun")]
    public bool ReadyToRun { get; set; }

    [JsonPropertyName("outputName")]
    public string OutputName { get; set; } = "";
}

public sealed class PackageManifest
{
    [JsonPropertyName("secureBundle")]
    public bool SecureBundle { get; set; }

    [JsonPropertyName("signBundle")]
    public bool SignBundle { get; set; } = true;

    [JsonPropertyName("verifyOnStartup")]
    public bool VerifyOnStartup { get; set; } = true;

    [JsonPropertyName("serveFromBundle")]
    public bool ServeFromBundle { get; set; } = true;

    [JsonPropertyName("removeSourceMaps")]
    public bool RemoveSourceMaps { get; set; } = true;

    [JsonPropertyName("exposeAppFolder")]
    public bool ExposeAppFolder { get; set; }
}

public sealed class FrontendManifest
{
    [JsonPropertyName("framework")]
    public string Framework { get; set; } = "vanilla";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "js";

    [JsonPropertyName("adapter")]
    public string Adapter { get; set; } = "static";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "src";

    [JsonPropertyName("dist")]
    public string Dist { get; set; } = "dist";
}

public sealed class NodeManifest
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "dev-only";

    [JsonPropertyName("allowedScripts")]
    public string[] AllowedScripts { get; set; } = ["dev", "build"];
}

public sealed class BrowserManifest
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("allowedOrigins")]
    public string[] AllowedOrigins { get; set; } = [];

    [JsonPropertyName("navigation")]
    public bool Navigation { get; set; } = true;

    [JsonPropertyName("externalOrigins")]
    public string ExternalOrigins { get; set; } = "open-system-browser";

    [JsonPropertyName("downloads")]
    public bool Downloads { get; set; } = true;

    [JsonPropertyName("popups")]
    public bool Popups { get; set; } = true;
}
