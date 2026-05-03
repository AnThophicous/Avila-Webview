using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Avila.Bridge;
using Avila.Core;
using Avila.Diagnostics;
using Avila.Security;
using Avila.Workers;

namespace Avila.Tests;

internal sealed class TempWorkspace : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"avila-tests-{Guid.NewGuid():N}");

    public TempWorkspace()
    {
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }
}

internal sealed class BridgeTestEnvironment : IAsyncDisposable
{
    private readonly TempWorkspace _workspace;

    private BridgeTestEnvironment(
        TempWorkspace workspace,
        AvilaProject project,
        SafeLogger logger,
        DiagnosticsCollector diagnostics,
        AvilaWorkerPool workers,
        CapabilityManager capabilities,
        BridgeCommandContext context,
        BridgeHost host)
    {
        _workspace = workspace;
        Project = project;
        Logger = logger;
        Diagnostics = diagnostics;
        Workers = workers;
        Capabilities = capabilities;
        Context = context;
        Host = host;
    }

    public AvilaProject Project { get; }

    public SafeLogger Logger { get; }

    public DiagnosticsCollector Diagnostics { get; }

    public AvilaWorkerPool Workers { get; }

    public CapabilityManager Capabilities { get; }

    public BridgeCommandContext Context { get; }

    public BridgeHost Host { get; }

    public static BridgeTestEnvironment Create(
        Action<AvilaManifest>? configure = null,
        string mode = "production")
    {
        var workspace = new TempWorkspace();
        var srcDir = Path.Combine(workspace.Root, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "index.html"), "<!doctype html><html><body>Test</body></html>");

        var manifest = CreateManifest(configure);
        var project = new AvilaProject(workspace.Root, Path.Combine(workspace.Root, "avila.json"), manifest);
        var logger = new SafeLogger(verbose: false);
        var diagnostics = new DiagnosticsCollector();
        var workers = new AvilaWorkerPool(
            new WorkerPoolOptions
            {
                MinWorkers = 0,
                MaxWorkers = 1,
                MaxQueueLength = 8,
                DefaultTimeout = TimeSpan.FromSeconds(1),
                IdleShrinkDelay = TimeSpan.FromMilliseconds(50)
            },
            logger);
        var capabilities = new CapabilityManager();

        var context = new BridgeCommandContext
        {
            Project = project,
            Mode = mode,
            Runtime = new DummyRuntimeGateway(workspace.Root),
            NodeHost = new DummyNodeHostGateway(),
            NativeShell = new DummyNativeShellGateway(),
            Window = new DummyWindowGateway(),
            Dialog = new DummyDialogGateway(),
            Clipboard = new DummyClipboardGateway(),
            Browser = new DummyBrowserGateway(),
            RemoteWebViews = new DummyRemoteWebViewGateway(),
            Workers = workers,
            Logger = logger,
            Diagnostics = diagnostics
        };

        var host = new BridgeHost(context, new BridgeCommandRegistry(), capabilities);
        return new BridgeTestEnvironment(workspace, project, logger, diagnostics, workers, capabilities, context, host);
    }

    public string CreateRequestJson(
        string command,
        object payload,
        long? timestamp = null,
        string? capability = null,
        string type = "avila.invoke",
        string id = "req-1")
    {
        var request = new BridgeRequest
        {
            Id = id,
            Type = type,
            Command = command,
            Payload = JsonSerializer.SerializeToElement(payload, ManifestLoader.JsonOptions),
            Timestamp = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Capability = capability ?? Capabilities.SessionCapability
        };

        return JsonSerializer.Serialize(request, ManifestLoader.JsonOptions);
    }

    public async Task<BridgeResponse> SendAsync(
        string source,
        string command,
        object payload,
        long? timestamp = null,
        string? capability = null,
        string type = "avila.invoke",
        string id = "req-1",
        CancellationToken cancellationToken = default)
    {
        var json = CreateRequestJson(command, payload, timestamp, capability, type, id);
        var responseJson = await Host.HandleMessageAsync(source, json, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<BridgeResponse>(responseJson, ManifestLoader.JsonOptions)
            ?? throw new InvalidOperationException("Bridge response was empty.");
    }

    public async ValueTask DisposeAsync()
    {
        await Workers.DisposeAsync().ConfigureAwait(false);
        _workspace.Dispose();
    }

    private static AvilaManifest CreateManifest(Action<AvilaManifest>? configure)
    {
        var manifest = new AvilaManifest
        {
            Mode = "appview",
            App = new AppManifest
            {
                Id = "com.example.test",
                Name = "Test App",
                Entry = "src/index.html",
                Icon = ""
            },
            Security = new SecurityManifest
            {
                DefaultPolicy = "deny",
                AllowRemoteContent = false,
                AllowedOrigins = [OriginPolicy.LocalOrigin],
                DevTools = false,
                SanitizeLogs = true,
                MaxPayloadBytes = 1_048_576,
                BridgeTimeoutMs = 5_000
            },
            FileSystem = new FileSystemManifest
            {
                AllowedRoots = ["app"]
            },
            Storage = new StorageManifest
            {
                AllowedStores = ["data", "config", "cache"]
            },
            Process = new ProcessManifest
            {
                AllowedCommands = [],
                AllowedCwdRoots = ["app", "data", "temp"],
                AllowedEnv = []
            },
            Network = new NetworkManifest
            {
                AllowedOrigins = [],
                AllowLan = false
            },
            Browser = new BrowserManifest
            {
                Url = "https://example.com",
                AllowedOrigins = ["https://example.com"],
                Navigation = true,
                ExternalOrigins = "open-system-browser",
                Downloads = true,
                Popups = true
            },
            Node = new NodeManifest
            {
                Enabled = false,
                Mode = "dev-only",
                AllowedScripts = ["dev", "build"]
            }
        };

        configure?.Invoke(manifest);
        return manifest;
    }
}

internal sealed class DummyRuntimeGateway : IRuntimeGateway
{
    private readonly string _root;

    public DummyRuntimeGateway(string root)
    {
        _root = root;
    }

    public IReadOnlyList<string> GetArgs() => [];

    public string GetPath(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public string ProtectSecret(string value) => value;

    public string UnprotectSecret(string protectedValue) => protectedValue;

    public Task QuitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RestartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task OpenExternalAsync(Uri uri, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RevealPathAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class DummyNodeHostGateway : INodeHostGateway
{
    public Task<NodeRunResult> RunAsync(string script, IReadOnlyList<string> args, CancellationToken cancellationToken)
        => Task.FromResult(new NodeRunResult(script, 0, "", ""));
}

internal sealed class DummyNativeShellGateway : INativeShellGateway
{
    public Task TrayShowAsync(NativeTrayRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task TrayHideAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task TraySetTooltipAsync(string tooltip, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task TraySetMenuAsync(IReadOnlyList<NativeMenuItem> items, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetWindowMenuAsync(IReadOnlyList<NativeMenuItem> items, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ShowContextMenuAsync(IReadOnlyList<NativeMenuItem> items, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ShowNotificationAsync(NativeNotificationRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RegisterShortcutAsync(NativeShortcutRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task UnregisterShortcutAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ClearShortcutsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetTaskbarProgressAsync(TaskbarProgressRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class DummyWindowGateway : IWindowGateway
{
    public Task CloseAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ShowAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task HideAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task FocusAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BlurAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task MinimizeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task MaximizeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RestoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ToggleMaximizeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> IsMaximizedAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public Task SetFullscreenAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> IsFullscreenAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public Task SetAlwaysOnTopAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetTitleAsync(string title, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetIconAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetSizeAsync(int width, int height, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetMinSizeAsync(int width, int height, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetMaxSizeAsync(int width, int height, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetPositionAsync(int x, int y, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CenterAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<WindowBounds> GetBoundsAsync(CancellationToken cancellationToken)
        => Task.FromResult(new WindowBounds(0, 0, 100, 100, 100, 100, "normal", true, false, true, true, 1));

    public Task SetResizableAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetDecorationsAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetOpacityAsync(double opacity, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetDraggableAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetMicaAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetRoundedCornersAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BeginDragAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class DummyDialogGateway : IDialogGateway
{
    public Task<IReadOnlyList<string>> OpenFileAsync(OpenFileRequest request, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<string?> SaveFileAsync(SaveFileRequest request, CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);

    public Task<string?> SelectFolderAsync(SelectFolderRequest request, CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);

    public Task ShowMessageAsync(MessageDialogRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> ConfirmAsync(ConfirmDialogRequest request, CancellationToken cancellationToken)
        => Task.FromResult(false);
}

internal sealed class DummyClipboardGateway : IClipboardGateway
{
    public Task<string> ReadTextAsync(CancellationToken cancellationToken) => Task.FromResult(string.Empty);

    public Task WriteTextAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<string> ReadHtmlAsync(CancellationToken cancellationToken) => Task.FromResult(string.Empty);

    public Task WriteHtmlAsync(string html, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class DummyBrowserGateway : IBrowserGateway
{
    public Task NavigateAsync(Uri uri, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BackAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ForwardAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> CanGoBackAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<bool> CanGoForwardAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public Task SetZoomAsync(double level, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> FindAsync(string text, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task OpenDevToolsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class DummyRemoteWebViewGateway : IRemoteWebViewGateway
{
    public Task<RemoteWebViewSnapshot> CreateAsync(RemoteWebViewCreateRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new RemoteWebViewSnapshot("id", request.Url?.ToString() ?? string.Empty, "", true, true, false, false, 1, request.Bounds));

    public Task DestroyAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task NavigateAsync(string id, Uri uri, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BackAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ForwardAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReloadAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ShowAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task HideAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task FocusAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetBoundsAsync(string id, RemoteWebViewBounds bounds, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetZoomAsync(string id, double level, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> FindAsync(string id, string text, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<string> ExecuteScriptAsync(string id, string script, CancellationToken cancellationToken) => Task.FromResult(string.Empty);

    public Task InjectCssAsync(string id, string css, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<string> ScreenshotAsync(string id, CancellationToken cancellationToken) => Task.FromResult(string.Empty);

    public Task OpenDevToolsAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<RemoteWebViewSnapshot?> ActivateAsync(string id, CancellationToken cancellationToken) => Task.FromResult<RemoteWebViewSnapshot?>(null);

    public Task<RemoteWebViewSnapshot?> GetActiveAsync(CancellationToken cancellationToken) => Task.FromResult<RemoteWebViewSnapshot?>(null);

    public Task<RemoteWebViewSnapshot?> DuplicateAsync(string id, CancellationToken cancellationToken) => Task.FromResult<RemoteWebViewSnapshot?>(null);

    public Task<RemoteWebViewSnapshot?> ReopenClosedAsync(CancellationToken cancellationToken) => Task.FromResult<RemoteWebViewSnapshot?>(null);

    public Task<IReadOnlyList<RemoteWebViewSnapshot>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<RemoteWebViewSnapshot>>([]);
}

internal sealed class MiniHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<string, HttpResponseSpec> _handler;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _runTask;

    private MiniHttpServer(Func<string, HttpResponseSpec> handler)
    {
        _handler = handler;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _runTask = Task.Run(RunAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public static MiniHttpServer Start(Func<string, HttpResponseSpec> handler) => new(handler);

    private async Task RunAsync()
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync(_shutdown.Token).ConfigureAwait(false);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var requestBuilder = new StringBuilder();
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync().ConfigureAwait(false)))
            {
                requestBuilder.AppendLine(line);
            }

            var response = _handler(requestBuilder.ToString());
            var bodyBytes = Encoding.UTF8.GetBytes(response.Body);
            var headers = new Dictionary<string, string>(response.Headers, StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Length"] = bodyBytes.Length.ToString(CultureInfo.InvariantCulture),
                ["Connection"] = "close"
            };

            var headerBlock = string.Join("\r\n", headers.Select(pair => $"{pair.Key}: {pair.Value}"));
            var payload = Encoding.UTF8.GetBytes($"HTTP/1.1 {response.StatusCode} {GetReasonPhrase(response.StatusCode)}\r\n{headerBlock}\r\n\r\n");
            await stream.WriteAsync(payload, _shutdown.Token).ConfigureAwait(false);
            if (bodyBytes.Length > 0)
            {
                await stream.WriteAsync(bodyBytes, _shutdown.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _listener.Stop();
        try
        {
            await _runTask.ConfigureAwait(false);
        }
        catch
        {
        }

        _shutdown.Dispose();
    }

    private static string GetReasonPhrase(int statusCode)
    {
        return statusCode switch
        {
            200 => "OK",
            301 => "Moved Permanently",
            302 => "Found",
            303 => "See Other",
            307 => "Temporary Redirect",
            308 => "Permanent Redirect",
            400 => "Bad Request",
            404 => "Not Found",
            500 => "Internal Server Error",
            _ => "OK"
        };
    }
}

internal sealed record HttpResponseSpec(int StatusCode, string Body, IReadOnlyDictionary<string, string> Headers)
{
    public static HttpResponseSpec Text(string body) => new(200, body, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Content-Type"] = "text/plain; charset=utf-8"
    });

    public static HttpResponseSpec Redirect(string location)
    {
        return new HttpResponseSpec(302, string.Empty, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Location"] = location
        });
    }
}
