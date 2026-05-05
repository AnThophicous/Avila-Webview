using Avila.Bridge;
using Avila.Core;
using Avila.Diagnostics;
using Avila.Security;
using Avila.Windowing;
using Avila.Workers;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;

namespace Avila.Runtime;

public sealed class AvilaApplicationForm : Form
{
    private const int IdleCheckIntervalMs = 15_000;
    private readonly AvilaProject _project;
    private readonly RuntimeOptions _options;
    private readonly CapabilityManager _capabilities;
    private readonly SafeLogger _logger;
    private readonly DiagnosticsCollector _diagnostics;
    private readonly AvilaWorkerPool _workers;
    private readonly NodeHostGateway _nodeHost;
    private readonly string _logDirectory;
    private readonly WebView2 _webView = new();
    private readonly Win32WindowController _windowController;
    private readonly NativeShellGateway _nativeShell;
    private readonly bool _secureBundleEnabled;
    private readonly string? _benchmarkFilePath;
    private SecureBundleHost? _secureBundleHost;
    private FileSystemWatcher? _devWatcher;
    private System.Threading.Timer? _devReloadTimer;
    private readonly System.Windows.Forms.Timer _idleTimer = new();
    private RemoteWebViewManager? _remoteWebViews;
    private BridgeHost? _bridge;
    private bool _devErrorRendered;
    private bool _suspended;
    private bool _openStageReached;
    private DateTimeOffset _lastActivityAt = DateTimeOffset.UtcNow;
    private string? _pendingReloadState;

    public AvilaApplicationForm(
        AvilaProject project,
        RuntimeOptions options,
        CapabilityManager capabilities,
        SafeLogger logger,
        DiagnosticsCollector diagnostics,
        AvilaWorkerPool workers,
        string logDirectory)
    {
        _project = project;
        _options = options;
        _capabilities = capabilities;
        _logger = logger;
        _diagnostics = diagnostics;
        _workers = workers;
        _nodeHost = new NodeHostGateway(project, options, logger);
        _logDirectory = logDirectory;
        _windowController = new Win32WindowController(this);
        _secureBundleEnabled = !string.IsNullOrWhiteSpace(project.BundlePath);
        _benchmarkFilePath = options.BenchmarkFilePath;

        Text = project.Manifest.App.Name;
        ShowIcon = !string.IsNullOrWhiteSpace(project.Manifest.App.Icon);
        ApplyAppIcon();
        _nativeShell = new NativeShellGateway(this, PostAvilaEvent, _logger);
        KeyPreview = true;
        _webView.Dock = DockStyle.Fill;
        _webView.DefaultBackgroundColor = Color.FromArgb(30, 30, 33);
        Controls.Add(_webView);
        WireWindowEvents();
        ConfigureIdleMonitor();
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        try
        {
            MarkActivity("startup");
            TransitionStartupStage(RuntimeStage.BackgroundPreparation);
            _windowController.ApplyInitialWindowManifest(_project.Manifest.Window);
            await InitializeWebViewAsync().ConfigureAwait(true);
            _diagnostics.MarkInitialMemory();

            if (_project.Manifest.Performance.ShrinkWorkersAfterStartup)
            {
                _ = Task.Run(async () =>
                {
                    await _workers.ShrinkAfterStartupAsync().ConfigureAwait(false);
                    _diagnostics.MarkPostShrinkMemory();
                });
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Runtime startup failed");
            if (_options.Mode.Equals("dev", StringComparison.OrdinalIgnoreCase))
            {
                using var form = new DevErrorForm(DevErrorSnapshot.FromException("Startup failure", exception));
                form.ShowDialog(this);
            }
            else
            {
                MessageBox.Show(_logger.Sanitize(exception.Message), "Avila", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            Close();
        }
    }

    protected override async void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _nativeShell.Dispose();
        _secureBundleHost?.Dispose();
        _devWatcher?.Dispose();
        _devReloadTimer?.Dispose();
        _idleTimer.Dispose();
        _remoteWebViews?.Dispose();
        await _workers.DisposeAsync().ConfigureAwait(false);
    }

    protected override void WndProc(ref Message m)
    {
        if (_nativeShell.HandleHotKeyMessage(m))
        {
            return;
        }

        if (_windowController.HandleWndProc(ref m))
        {
            return;
        }

        base.WndProc(ref m);
    }

    private async Task InitializeWebViewAsync()
    {
        TransitionStartupStage(RuntimeStage.WebViewWorking);
        var userDataPath = GetUserDataPath();
        Directory.CreateDirectory(userDataPath);

        var environmentOptions = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = BuildAdditionalBrowserArguments()
        };
        var environment = await CoreWebView2Environment.CreateAsync(null, userDataPath, environmentOptions).ConfigureAwait(true);
        await _webView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
        if (!IsBrowserAppMode())
        {
            _remoteWebViews = new RemoteWebViewManager(this, environment, _project, PostAvilaEvent, _logger);
        }

        ConfigureWebViewSettings();
        if (_secureBundleEnabled && _project.BundlePath is not null)
        {
            _secureBundleHost = new SecureBundleHost(_webView.CoreWebView2, _project.RootPath, _project.Manifest.App.Entry);
            _secureBundleHost.Attach();
        }
        if (!IsBrowserAppMode())
        {
            ConfigureBridge();
            _diagnostics.MarkBridgeReady();
        }
        ConfigureNavigationPolicy();

        if (!IsBrowserAppMode() && (_options.Mode.Equals("dev", StringComparison.OrdinalIgnoreCase) || _project.Manifest.Performance.PreloadBridge))
        {
            var script = SdkInjector.BuildInjectedScript(_project, _options.Mode, _capabilities.SessionCapability);
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script).ConfigureAwait(true);
        }

        if (_options.Mode.Equals("dev", StringComparison.OrdinalIgnoreCase) && !IsBrowserAppMode() && !_secureBundleEnabled)
        {
            StartDevHotReload();
        }

        if (!IsBrowserAppMode() && !_secureBundleEnabled)
        {
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                OriginPolicy.VirtualHost,
                _project.RootPath,
                CoreWebView2HostResourceAccessKind.DenyCors);
        }

        if (IsBrowserAppMode())
        {
            var browserUrl = ResolveBrowserStartUrl();
            if (browserUrl is not null)
            {
                _webView.CoreWebView2.Navigate(browserUrl);
            }
        }
        else if (_secureBundleEnabled)
        {
            var entryPath = _project.Manifest.App.Entry.Replace('\\', '/').TrimStart('/');
            _webView.CoreWebView2.Navigate($"https://{OriginPolicy.VirtualHost}/{entryPath}");
        }
        else
        {
            var entryPath = _project.Manifest.App.Entry.Replace('\\', '/').TrimStart('/');
            _webView.CoreWebView2.Navigate($"https://{OriginPolicy.VirtualHost}/{entryPath}");
        }

        MarkActivity("webview-ready");
    }

    private void ConfigureWebViewSettings()
    {
        var devToolsEnabled = _options.DevToolsOverride
            ?? (_options.Mode.Equals("dev", StringComparison.OrdinalIgnoreCase) && _project.Manifest.Security.DevTools);

        _webView.CoreWebView2.Settings.AreDevToolsEnabled = devToolsEnabled;
        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = _options.Mode.Equals("dev", StringComparison.OrdinalIgnoreCase);
        _webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = devToolsEnabled;
        _webView.CoreWebView2.Settings.AreHostObjectsAllowed = false;
        _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
    }

    private string BuildAdditionalBrowserArguments()
    {
        var flags = _project.Manifest.Performance.BrowserFlags
            .Where(flag => !string.IsNullOrWhiteSpace(flag))
            .Select(flag => flag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return string.Join(' ', flags);
    }

    private void ConfigureBridge()
    {
        var context = new BridgeCommandContext
        {
            Project = _project,
            Mode = _options.Mode,
            Runtime = new RuntimeGateway(_project, _options, _logDirectory),
            NodeHost = _nodeHost,
            NativeShell = _nativeShell,
            Window = new WinFormsWindowGateway(_windowController),
            Dialog = new WinFormsDialogGateway(this),
            Clipboard = new WinFormsClipboardGateway(this),
            Browser = new WebViewBrowserGateway(_webView),
            RemoteWebViews = _remoteWebViews ?? throw new InvalidOperationException("Remote WebView manager is not ready."),
            Workers = _workers,
            Logger = _logger,
            Diagnostics = _diagnostics
        };

        _bridge = new BridgeHost(context, new BridgeCommandRegistry(), _capabilities);

        _webView.CoreWebView2.WebMessageReceived += async (_, args) =>
        {
            if (TryHandleRuntimeMessage(args.WebMessageAsJson))
            {
                return;
            }

            if (TryHandleDevMessage(args.WebMessageAsJson))
            {
                return;
            }

            if (_bridge is null)
            {
                return;
            }

            var response = await _bridge.HandleMessageAsync(args.Source, args.WebMessageAsJson).ConfigureAwait(true);
            _webView.CoreWebView2.PostWebMessageAsJson(response);
        };

        _webView.CoreWebView2.DOMContentLoaded += (_, _) =>
        {
            _diagnostics.MarkFirstPaint();
            MarkBenchmarkReady();
            MarkActivity("domcontentloaded");
            if (!_openStageReached)
            {
                TransitionStartupStage(RuntimeStage.Open);
                _openStageReached = true;
            }
            _ = RestoreReloadStateAsync();
        };
        _webView.CoreWebView2.SourceChanged += (_, _) =>
        {
            MarkActivity("sourcechanged");
            PostBrowserEvent("url", new { url = _webView.CoreWebView2.Source });
        };
        _webView.CoreWebView2.DocumentTitleChanged += (_, _) =>
        {
            MarkActivity("titlechanged");
            PostBrowserEvent("title", new { title = _webView.CoreWebView2.DocumentTitle });
        };
        _webView.CoreWebView2.NavigationStarting += (_, _) => MarkActivity("navigationstarting");
        _webView.CoreWebView2.NavigationCompleted += (_, args) =>
        {
            MarkActivity("navigationcompleted");
            if (args.IsSuccess)
            {
                PostBrowserEvent("loaded", new { url = _webView.CoreWebView2.Source });
                if (!_openStageReached)
                {
                    TransitionStartupStage(RuntimeStage.Open);
                    _openStageReached = true;
                }
            }
            else
            {
                PostBrowserEvent("error", new
                {
                    url = _webView.CoreWebView2.Source,
                    status = args.WebErrorStatus.ToString()
                });
            }
        };
    }

    private void ConfigureNavigationPolicy()
    {
        var browserMode = IsBrowserAppMode();
        var originPolicy = new OriginPolicy(_project.Manifest);

        _webView.CoreWebView2.NavigationStarting += (_, args) =>
        {
            if (IsLocalUri(args.Uri))
            {
                PostBrowserEvent("loading", new { url = args.Uri });
                return;
            }

            if (browserMode)
            {
                HandleBrowserAppNavigation(args.Uri, args);
                return;
            }

            if (!_project.Manifest.Security.AllowRemoteContent || !originPolicy.IsAllowed(args.Uri))
            {
                args.Cancel = true;
                _logger.Warning($"Blocked navigation to disallowed origin: {args.Uri}");
                PostBrowserEvent("error", new { url = args.Uri, status = "BlockedByPolicy" });
                return;
            }

            PostBrowserEvent("loading", new { url = args.Uri });
        };

        _webView.CoreWebView2.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            if (browserMode && !_project.Manifest.Browser.Popups)
            {
                if (TryOpenBrowserAppExternal(args.Uri))
                {
                    return;
                }

                _logger.Warning($"Blocked popup by policy: {args.Uri}");
                PostBrowserEvent("error", new { url = args.Uri, status = "PopupsDisabled" });
                return;
            }

            if (browserMode)
            {
                if (TryOpenBrowserAppExternal(args.Uri))
                {
                    return;
                }

                if (TryNavigateBrowserApp(args.Uri))
                {
                    return;
                }
            }
            else if (_project.Manifest.Security.AllowRemoteContent && originPolicy.IsAllowed(args.Uri))
            {
                _webView.CoreWebView2.Navigate(args.Uri);
            }
        };

        _webView.CoreWebView2.DownloadStarting += (_, args) =>
        {
            if (!browserMode || _project.Manifest.Browser.Downloads)
            {
                return;
            }

            args.Cancel = true;
            PostBrowserEvent("downloadBlocked", new { url = args.DownloadOperation.Uri });
        };
    }

    private string GetUserDataPath()
    {
        if (_options.Mode.Equals("dev", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(_project.RootPath, "build", "webview2-data");
        }

        var safeId = string.Join("_", _project.Manifest.App.Id.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), EngineIdentity.AppDataRoot, safeId, "webview2-data");
    }

    private void ApplyAppIcon()
    {
        var iconPath = _project.Manifest.App.Icon;
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            ShowIcon = false;
            return;
        }

        try
        {
            var resolved = SafePath.ResolveInside(_project.RootPath, iconPath);
            if (File.Exists(resolved))
            {
                Icon = new Icon(resolved);
                ShowIcon = true;
            }
        }
        catch (Exception exception)
        {
            _logger.Warning($"Could not apply app icon: {exception.Message}");
        }
    }

    private void PostBrowserEvent(string name, object payload)
    {
        PostAvilaEvent($"browser.{name}", payload);
    }

    private void PostWindowEvent(string name, object payload)
    {
        PostAvilaEvent($"window.{name}", payload);
    }

    private void PostAvilaEvent(string name, object payload)
    {
        if (_webView.CoreWebView2 is null || _webView.IsDisposed)
        {
            return;
        }

        var message = JsonSerializer.Serialize(new
        {
            type = "avila.event",
            @event = name,
            payload
        });

        try
        {
            _webView.CoreWebView2.PostWebMessageAsJson(message);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private bool TryHandleDevMessage(string json)
    {
        if (_devErrorRendered)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeProperty)
                || !string.Equals(typeProperty.GetString(), "avila.dev.error", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _devErrorRendered = true;
            var snapshot = DevErrorSnapshot.FromFrontendJson(root);
            RenderDevErrorPage(snapshot);
            return true;
        }
        catch (Exception exception)
        {
            _logger.Warning($"Could not parse dev error payload: {exception.Message}");
            return false;
        }
    }

    private bool TryHandleRuntimeMessage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeProperty))
            {
                return false;
            }

            var type = typeProperty.GetString();
            if (string.Equals(type, "avila.activity", StringComparison.OrdinalIgnoreCase))
            {
                MarkActivity("bridge-activity");
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private void RenderDevErrorPage(DevErrorSnapshot snapshot)
    {
        if (_webView.CoreWebView2 is null || _webView.IsDisposed)
        {
            return;
        }

        var html = DevErrorPageBuilder.Build(snapshot);
        try
        {
            _webView.CoreWebView2.NavigateToString(html);
        }
        catch (Exception exception)
        {
            _logger.Warning($"Could not render dev error page: {exception.Message}");
        }
    }

    private void MarkBenchmarkReady()
    {
        if (string.IsNullOrWhiteSpace(_benchmarkFilePath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_benchmarkFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_benchmarkFilePath, $"{DateTimeOffset.UtcNow:O}{Environment.NewLine}");
        }
        catch (Exception exception)
        {
            _logger.Warning($"Could not write benchmark marker: {exception.Message}");
        }
    }

    private void ConfigureIdleMonitor()
    {
        _idleTimer.Interval = IdleCheckIntervalMs;
        _idleTimer.Tick += async (_, _) =>
        {
            try
            {
                await EvaluateIdleStateAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _logger.Warning($"Idle monitor failed: {exception.Message}");
            }
        };
        _idleTimer.Start();
    }

    private async Task EvaluateIdleStateAsync()
    {
        if (_webView.CoreWebView2 is null || _webView.IsDisposed)
        {
            return;
        }

        var idleFor = DateTimeOffset.UtcNow - _lastActivityAt;
        var threshold = TimeSpan.FromMilliseconds(_project.Manifest.Performance.IdleSuspendAfterMs);

        if (!_suspended && idleFor >= threshold)
        {
            _logger.Trace($"idle suspend after {idleFor.TotalMinutes:N1} min");
            _workers.ConfigureTargetWorkers(_project.Manifest.Performance.IdleWorkers, _project.Manifest.Performance.WorkerPoolMax);
            await TrySuspendWebViewAsync().ConfigureAwait(true);
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: true);
            _suspended = true;
            return;
        }

        if (_suspended && idleFor < threshold)
        {
            await ResumeWebViewAsync().ConfigureAwait(true);
            _workers.ConfigureTargetWorkers(_project.Manifest.Performance.OpenWorkers, _project.Manifest.Performance.WorkerPoolMax);
            _suspended = false;
        }
    }

    private Task TrySuspendWebViewAsync()
    {
        return InvokeUiAsync(async () =>
        {
            if (_webView.CoreWebView2 is null)
            {
                return;
            }

            try
            {
                await _webView.CoreWebView2.TrySuspendAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _logger.Warning($"Could not suspend WebView2: {exception.Message}");
            }
        });
    }

    private Task ResumeWebViewAsync()
    {
        return InvokeUiAsync(() =>
        {
            if (_webView.CoreWebView2 is null)
            {
                return Task.CompletedTask;
            }

            try
            {
                _webView.CoreWebView2.Resume();
            }
            catch (Exception exception)
            {
                _logger.Warning($"Could not resume WebView2: {exception.Message}");
            }

            return Task.CompletedTask;
        });
    }

    private void TransitionStartupStage(RuntimeStage stage)
    {
        _diagnostics.MarkStage(stage);
        _logger.Trace($"startup stage: {stage}");

        if (stage == RuntimeStage.Open)
        {
            _workers.ConfigureTargetWorkers(_project.Manifest.Performance.OpenWorkers, _project.Manifest.Performance.WorkerPoolMax);
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: true);
        }
        else if (stage == RuntimeStage.BackgroundPreparation)
        {
            _workers.ConfigureTargetWorkers(_project.Manifest.Performance.StartupWorkers, _project.Manifest.Performance.WorkerPoolMax);
        }
    }

    private void MarkActivity(string reason)
    {
        _lastActivityAt = DateTimeOffset.UtcNow;
        _logger.Trace($"activity: {reason}");

        if (_suspended)
        {
            _workers.ConfigureTargetWorkers(_project.Manifest.Performance.OpenWorkers, _project.Manifest.Performance.WorkerPoolMax);
            _ = ResumeWebViewAsync();
            _suspended = false;
        }
    }

    private async Task RestoreReloadStateAsync()
    {
        if (string.IsNullOrWhiteSpace(_pendingReloadState) || _webView.CoreWebView2 is null)
        {
            return;
        }

        var state = _pendingReloadState;
        _pendingReloadState = null;

        var script = $$"""
(() => {
  const state = {{state}};
  if (!state) {
    return;
  }
  try {
    if (state.sessionStorage && typeof state.sessionStorage === "object") {
      for (const [key, value] of Object.entries(state.sessionStorage)) {
        sessionStorage.setItem(key, value);
      }
    }
    if (typeof state.scrollX === "number" && typeof state.scrollY === "number") {
      window.scrollTo(state.scrollX, state.scrollY);
    }
    if (state.activeElementId) {
      const element = document.getElementById(state.activeElementId);
      if (element && typeof element.focus === "function") {
        element.focus({ preventScroll: true });
      }
    }
  } catch (_) {
  }
})();
""";

        try
        {
            await _webView.CoreWebView2.ExecuteScriptAsync(script).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.Warning($"Could not restore reload state: {exception.Message}");
        }
    }

    private async Task CaptureReloadStateAndReloadAsync()
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            var result = await _webView.CoreWebView2.ExecuteScriptAsync("""
(() => JSON.stringify({
  scrollX: window.scrollX,
  scrollY: window.scrollY,
  activeElementId: document.activeElement && document.activeElement.id ? document.activeElement.id : "",
  sessionStorage: Object.fromEntries(Object.keys(sessionStorage).map(key => [key, sessionStorage.getItem(key)]))
}))
""").ConfigureAwait(true);
            _pendingReloadState = JsonSerializer.Deserialize<string>(result) ?? result;
        }
        catch (Exception exception)
        {
            _logger.Warning($"Could not capture reload state: {exception.Message}");
            _pendingReloadState = null;
        }

        try
        {
            _webView.CoreWebView2.Reload();
        }
        catch (Exception exception)
        {
            _logger.Warning($"Could not trigger smart reload: {exception.Message}");
            _webView.CoreWebView2.Reload();
        }
    }

    private Task InvokeUiAsync(Func<Task> action)
    {
        if (_webView.IsDisposed)
        {
            return Task.CompletedTask;
        }

        if (!_webView.InvokeRequired)
        {
            return action();
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _webView.BeginInvoke(new Action(async () =>
        {
            try
            {
                await action().ConfigureAwait(true);
                tcs.TrySetResult();
            }
            catch (Exception exception)
            {
                tcs.TrySetException(exception);
            }
        }));

        return tcs.Task;
    }

    private void WireWindowEvents()
    {
        Resize += (_, _) =>
        {
            MarkActivity("resize");
            PostWindowEvent("resize", new
            {
                width = ClientSize.Width,
                height = ClientSize.Height,
                state = WindowState.ToString().ToLowerInvariant()
            });
        };
        Move += (_, _) =>
        {
            MarkActivity("move");
            PostWindowEvent("move", new { x = Left, y = Top });
        };
        Activated += (_, _) =>
        {
            MarkActivity("activated");
            PostWindowEvent("focus", new { focused = true });
        };
        Deactivate += (_, _) => PostWindowEvent("blur", new { focused = false });
        KeyDown += (_, args) =>
        {
            MarkActivity("keydown");
            _nativeShell.HandleLocalKeyDown(args);
        };
        FormClosing += (_, args) => PostWindowEvent("closeRequested", new
        {
            reason = args.CloseReason.ToString(),
            cancellable = false
        });
        MouseMove += (_, _) => MarkActivity("mousemove");
        MouseDown += (_, _) => MarkActivity("mousedown");
        MouseWheel += (_, _) => MarkActivity("mousewheel");
    }

    private static bool IsLocalUri(string uri)
    {
        return Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            && parsed.Host.Equals(OriginPolicy.VirtualHost, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsBrowserAppMode()
    {
        return _project.Manifest.Mode.Equals("browser-app", StringComparison.OrdinalIgnoreCase);
    }

    private string? ResolveBrowserStartUrl()
    {
        if (string.IsNullOrWhiteSpace(_project.Manifest.Browser.Url))
        {
            return null;
        }

        return _project.Manifest.Browser.Url;
    }

    private void HandleBrowserAppNavigation(string uri, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            args.Cancel = true;
            return;
        }

        if (IsAllowedBrowserOrigin(parsed))
        {
            if (!_project.Manifest.Browser.Navigation)
            {
                args.Cancel = true;
                _logger.Warning($"Blocked browser navigation by policy: {uri}");
                PostBrowserEvent("error", new { url = uri, status = "NavigationDisabled" });
                return;
            }

            PostBrowserEvent("loading", new { url = uri });
            return;
        }

        if (TryOpenBrowserAppExternal(uri))
        {
            args.Cancel = true;
            PostBrowserEvent("external", new { url = uri });
            return;
        }

        args.Cancel = true;
        _logger.Warning($"Blocked navigation to disallowed browser origin: {uri}");
        PostBrowserEvent("error", new { url = uri, status = "BlockedByPolicy" });
    }

    private bool TryOpenBrowserAppExternal(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return false;
        }

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (!IsAllowedBrowserOrigin(parsed)
            && string.Equals(_project.Manifest.Browser.ExternalOrigins, "open-system-browser", StringComparison.OrdinalIgnoreCase))
        {
            OpenInSystemBrowser(uri);
            return true;
        }

        return false;
    }

    private bool TryNavigateBrowserApp(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (!IsAllowedBrowserOrigin(parsed))
        {
            return false;
        }

        if (!_project.Manifest.Browser.Navigation)
        {
            return false;
        }

        _webView.CoreWebView2.Navigate(uri);
        return true;
    }

    private bool IsAllowedBrowserOrigin(Uri uri)
    {
        if (_project.Manifest.Browser.AllowedOrigins.Length == 0)
        {
            if (string.IsNullOrWhiteSpace(_project.Manifest.Browser.Url))
            {
                return false;
            }

            if (!Uri.TryCreate(_project.Manifest.Browser.Url, UriKind.Absolute, out var startUrl))
            {
                return false;
            }

            var startOrigin = startUrl.IsDefaultPort ? $"{startUrl.Scheme}://{startUrl.Host}" : $"{startUrl.Scheme}://{startUrl.Host}:{startUrl.Port}";
            var currentOrigin = uri.IsDefaultPort ? $"{uri.Scheme}://{uri.Host}" : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
            return startOrigin.Equals(currentOrigin, StringComparison.OrdinalIgnoreCase);
        }

        var origin = uri.IsDefaultPort ? $"{uri.Scheme}://{uri.Host}" : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        return _project.Manifest.Browser.AllowedOrigins.Any(allowed =>
            Uri.TryCreate(allowed, UriKind.Absolute, out var allowedUri)
            && (allowedUri.IsDefaultPort ? $"{allowedUri.Scheme}://{allowedUri.Host}" : $"{allowedUri.Scheme}://{allowedUri.Host}:{allowedUri.Port}")
                .Equals(origin, StringComparison.OrdinalIgnoreCase));
    }

    private static void OpenInSystemBrowser(string uri)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = uri,
            UseShellExecute = true
        });
    }

    private void StartDevHotReload()
    {
        _devReloadTimer ??= new System.Threading.Timer(_ =>
        {
            if (_webView.IsDisposed || _webView.CoreWebView2 is null)
            {
                return;
            }

            try
            {
                if (_webView.InvokeRequired)
                {
                    _webView.BeginInvoke(new Action(() => _ = CaptureReloadStateAndReloadAsync()));
                }
                else
                {
                    _ = CaptureReloadStateAndReloadAsync();
                }
            }
            catch
            {
            }
        }, null, Timeout.Infinite, Timeout.Infinite);

        _devWatcher = new FileSystemWatcher(_project.RootPath)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            Filter = "*.*"
        };

        _devWatcher.Changed += (_, e) => OnDevFileChanged(e.FullPath);
        _devWatcher.Created += (_, e) => OnDevFileChanged(e.FullPath);
        _devWatcher.Deleted += (_, e) => OnDevFileChanged(e.FullPath);
        _devWatcher.Renamed += (_, e) => OnDevFileChanged(e.FullPath);
    }

    private void OnDevFileChanged(string path)
    {
        if (ShouldIgnoreDevChange(path))
        {
            return;
        }

        ScheduleDevReload();
    }

    private bool ShouldIgnoreDevChange(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(_project.RootPath, fullPath);
        var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "build",
            "dist",
            "logs",
            "node_modules",
            "webview2-data",
            "bin",
            "obj",
            ".git"
        };

        return segments.Any(ignored.Contains);
    }

    private void ScheduleDevReload()
    {
        if (_devReloadTimer is null)
        {
            return;
        }

        _logger.Trace("dev hot reload scheduled");
        _devReloadTimer.Change(350, Timeout.Infinite);
    }
}
