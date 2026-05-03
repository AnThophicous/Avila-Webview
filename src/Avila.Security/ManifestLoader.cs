using System.Text.Json;

namespace Avila.Security;

public sealed record AvilaProject(string RootPath, string ManifestPath, AvilaManifest Manifest);

public sealed record ManifestIssue(string Code, string Message, bool IsError);

public sealed class ManifestValidationResult
{
    private readonly List<ManifestIssue> _issues = [];

    public IReadOnlyList<ManifestIssue> Issues => _issues;

    public bool IsValid => _issues.All(issue => !issue.IsError);

    public IEnumerable<ManifestIssue> Errors => _issues.Where(issue => issue.IsError);

    public IEnumerable<ManifestIssue> Warnings => _issues.Where(issue => !issue.IsError);

    public void Error(string code, string message) => _issues.Add(new ManifestIssue(code, message, true));

    public void Warning(string code, string message) => _issues.Add(new ManifestIssue(code, message, false));
}

public static class ManifestLoader
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public static async Task<AvilaProject> LoadProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var manifestPath = ResolveManifestPath(projectPath);
        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<AvilaManifest>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("avila.json is empty or invalid.");

        return new AvilaProject(Path.GetDirectoryName(manifestPath)!, manifestPath, manifest);
    }

    public static string ResolveManifestPath(string projectPath)
    {
        var fullPath = Path.GetFullPath(projectPath);
        if (File.Exists(fullPath) && Path.GetFileName(fullPath).Equals("avila.json", StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        if (Directory.Exists(fullPath))
        {
            var direct = Path.Combine(fullPath, "avila.json");
            if (File.Exists(direct))
            {
                return direct;
            }
        }

        throw new FileNotFoundException("Could not find avila.json. Pass a .avw directory or a manifest path.", fullPath);
    }

    public static ManifestValidationResult Validate(AvilaProject project)
    {
        var result = new ManifestValidationResult();
        var manifest = project.Manifest;

        if (string.IsNullOrWhiteSpace(manifest.App.Id))
        {
            result.Error("APP_ID_REQUIRED", "app.id is required.");
        }

        if (string.IsNullOrWhiteSpace(manifest.App.Name))
        {
            result.Error("APP_NAME_REQUIRED", "app.name is required.");
        }

        var browserMode = manifest.Mode.Equals("browser-app", StringComparison.OrdinalIgnoreCase);

        if (!browserMode)
        {
            if (!IsSafeRelativePath(manifest.App.Entry))
            {
                result.Error("ENTRY_PATH_INVALID", "app.entry must be a safe relative path.");
            }
            else if (!File.Exists(Path.Combine(project.RootPath, manifest.App.Entry)))
            {
                result.Error("ENTRY_MISSING", $"app.entry was not found: {manifest.App.Entry}");
            }
        }
        else if (!string.IsNullOrWhiteSpace(manifest.App.Entry) && !IsSafeRelativePath(manifest.App.Entry))
        {
            result.Error("ENTRY_PATH_INVALID", "app.entry must be a safe relative path.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.App.Icon) && !IsSafeRelativePath(manifest.App.Icon))
        {
            result.Error("ICON_PATH_INVALID", "app.icon must be a safe relative path.");
        }
        else if (!string.IsNullOrWhiteSpace(manifest.App.Icon) && !File.Exists(Path.Combine(project.RootPath, manifest.App.Icon)))
        {
            result.Warning("ICON_MISSING", $"app.icon was not found: {manifest.App.Icon}");
        }

        if (!string.IsNullOrWhiteSpace(manifest.Browser.Url))
        {
            if (!Uri.TryCreate(manifest.Browser.Url, UriKind.Absolute, out var browserUrl)
                || browserUrl.Scheme is not ("http" or "https"))
            {
                result.Error("BROWSER_URL_INVALID", "browser.url must be an absolute http or https URL.");
            }
            else if (manifest.Browser.AllowedOrigins.Length > 0
                && !manifest.Browser.AllowedOrigins.Any(origin => OriginMatches(browserUrl, origin)))
            {
                result.Error("BROWSER_URL_BLOCKED", "browser.url must match one of browser.allowedOrigins.");
            }
        }

        if (browserMode && string.IsNullOrWhiteSpace(manifest.Browser.Url))
        {
            result.Error("BROWSER_URL_REQUIRED", "mode browser-app requires browser.url.");
        }

        if (browserMode && manifest.Browser.AllowedOrigins.Length == 0)
        {
            result.Warning("BROWSER_ALLOWLIST_EMPTY", "browser-app mode should define browser.allowedOrigins. The runtime will fall back to browser.url origin only.");
        }

        if (!browserMode && !string.IsNullOrWhiteSpace(manifest.Browser.Url))
        {
            result.Warning("BROWSER_URL_IGNORED", "browser.url is set, but mode is not browser-app.");
        }

        if (browserMode && !manifest.Browser.Navigation)
        {
            result.Warning("BROWSER_NAVIGATION_DISABLED", "mode browser-app is set, but browser.navigation is disabled.");
        }

        if (!manifest.Security.DefaultPolicy.Equals("deny", StringComparison.OrdinalIgnoreCase)
            && !manifest.Security.DefaultPolicy.Equals("allow", StringComparison.OrdinalIgnoreCase))
        {
            result.Error("POLICY_INVALID", "security.defaultPolicy must be either deny or allow.");
        }

        if (!manifest.Security.DefaultPolicy.Equals("deny", StringComparison.OrdinalIgnoreCase))
        {
            result.Warning("POLICY_NOT_DENY", "security.defaultPolicy should be deny for production.");
        }

        if (manifest.Security.AllowRemoteContent)
        {
            result.Warning("REMOTE_CONTENT_ENABLED", "Remote content is enabled. Native APIs should remain restricted.");
        }

        if (manifest.Security.MaxPayloadBytes is < 1024 or > 16_777_216)
        {
            result.Error("PAYLOAD_LIMIT_INVALID", "security.maxPayloadBytes must be between 1 KB and 16 MB.");
        }

        if (manifest.Security.BridgeTimeoutMs is < 100 or > 60_000)
        {
            result.Error("BRIDGE_TIMEOUT_INVALID", "security.bridgeTimeoutMs must be between 100 ms and 60000 ms.");
        }

        var validRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "app",
            "data",
            "config",
            "cache",
            "downloads",
            "temp"
        };
        foreach (var root in manifest.FileSystem.AllowedRoots)
        {
            if (!validRoots.Contains(root))
            {
                result.Error("FS_ROOT_INVALID", $"fs.allowedRoots contains an unknown root: {root}");
            }
        }

        if (manifest.FileSystem.MaxReadBytes is < 1_024 or > 67_108_864)
        {
            result.Error("FS_READ_LIMIT_INVALID", "fs.maxReadBytes must be between 1 KB and 64 MB.");
        }

        if (manifest.FileSystem.MaxWriteBytes is < 1_024 or > 67_108_864)
        {
            result.Error("FS_WRITE_LIMIT_INVALID", "fs.maxWriteBytes must be between 1 KB and 64 MB.");
        }

        var validStores = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "data",
            "config",
            "cache"
        };
        foreach (var store in manifest.Storage.AllowedStores)
        {
            if (!validStores.Contains(store))
            {
                result.Error("STORE_ROOT_INVALID", $"storage.allowedStores contains an unknown store: {store}");
            }
        }

        if (manifest.Storage.MaxStoreBytes is < 1_024 or > 67_108_864)
        {
            result.Error("STORE_LIMIT_INVALID", "storage.maxStoreBytes must be between 1 KB and 64 MB.");
        }

        foreach (var root in manifest.Process.AllowedCwdRoots)
        {
            if (!validRoots.Contains(root))
            {
                result.Error("PROCESS_CWD_ROOT_INVALID", $"process.allowedCwdRoots contains an unknown root: {root}");
            }
        }

        if (manifest.Process.TimeoutMs is < 100 or > 600_000)
        {
            result.Error("PROCESS_TIMEOUT_INVALID", "process.timeoutMs must be between 100 ms and 600000 ms.");
        }

        if (manifest.Process.MaxOutputBytes is < 1024 or > 16_777_216)
        {
            result.Error("PROCESS_OUTPUT_LIMIT_INVALID", "process.maxOutputBytes must be between 1 KB and 16 MB.");
        }

        if (manifest.Network.TimeoutMs is < 100 or > 120_000)
        {
            result.Error("NETWORK_TIMEOUT_INVALID", "network.timeoutMs must be between 100 ms and 120000 ms.");
        }

        if (manifest.Network.RedirectLimit is < 0 or > 10)
        {
            result.Error("NETWORK_REDIRECT_LIMIT_INVALID", "network.redirectLimit must be between 0 and 10.");
        }

        if (manifest.Network.MaxResponseBytes is < 1024 or > 67_108_864)
        {
            result.Error("NETWORK_RESPONSE_LIMIT_INVALID", "network.maxResponseBytes must be between 1 KB and 64 MB.");
        }

        if (manifest.Window.Width <= 0 || manifest.Window.Height <= 0)
        {
            result.Error("WINDOW_SIZE_INVALID", "window.width and window.height must be positive.");
        }

        if (manifest.Window.MinWidth > manifest.Window.Width || manifest.Window.MinHeight > manifest.Window.Height)
        {
            result.Warning("WINDOW_MIN_EXCEEDS_INITIAL", "window.minWidth/minHeight are larger than the initial size.");
        }

        if (manifest.Performance.WorkerPoolMin < 0 || manifest.Performance.WorkerPoolMax < 1)
        {
            result.Error("WORKER_POOL_INVALID", "workerPoolMin must be >= 0 and workerPoolMax must be >= 1.");
        }

        if (manifest.Performance.WorkerPoolMin > manifest.Performance.WorkerPoolMax)
        {
            result.Error("WORKER_POOL_RANGE_INVALID", "workerPoolMin cannot be greater than workerPoolMax.");
        }

        if (manifest.Build.NativeAot)
        {
            result.Warning("NATIVE_AOT_EXPERIMENTAL", "Native AOT is experimental with WebView2 and reflection-heavy APIs.");
        }

        if (manifest.Build.Trim)
        {
            result.Warning("TRIM_DISABLED_FOR_WINDOWS_FORMS", "Trimming is requested, but the current WinForms/WebView2 host publishes with trimming disabled.");
        }

        if (!IsSafeRelativePath(manifest.Frontend.Source))
        {
            result.Error("FRONTEND_SOURCE_INVALID", "frontend.source must be a safe relative path.");
        }

        if (!IsSafeRelativePath(manifest.Frontend.Dist))
        {
            result.Error("FRONTEND_DIST_INVALID", "frontend.dist must be a safe relative path.");
        }

        var validNodeModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dev-only",
            "build-only",
            "dev-build",
            "always"
        };

        if (!validNodeModes.Contains(manifest.Node.Mode))
        {
            result.Error("NODE_MODE_INVALID", "node.mode must be dev-only, build-only, dev-build, or always.");
        }

        foreach (var script in manifest.Node.AllowedScripts)
        {
            if (!PermissionPolicy.IsValidCommandName(script))
            {
                result.Error("NODE_SCRIPT_INVALID", $"node.allowedScripts contains an invalid script name: {script}");
            }
        }

        foreach (var command in PermissionPolicy.GetDangerousEnabledCommands(manifest))
        {
            result.Warning("DANGEROUS_PERMISSION", $"Dangerous permission is enabled: {command}");
        }

        return result;
    }

    private static bool OriginMatches(Uri uri, string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        var uriOrigin = uri.IsDefaultPort ? $"{uri.Scheme}://{uri.Host}" : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        var originOrigin = originUri.IsDefaultPort ? $"{originUri.Scheme}://{originUri.Host}" : $"{originUri.Scheme}://{originUri.Host}:{originUri.Port}";
        return uriOrigin.Equals(originOrigin, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (Path.IsPathRooted(path))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/');
        return !normalized.Split('/').Any(segment => segment is ".." or "");
    }
}
