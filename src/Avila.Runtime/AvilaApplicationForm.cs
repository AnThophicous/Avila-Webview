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
    private RemoteWebViewManager? _remoteWebViews;
    private BridgeHost? _bridge;

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

        Text = project.Manifest.App.Name;
        ApplyAppIcon();
        _nativeShell = new NativeShellGateway(this, PostAvilaEvent, _logger);
        KeyPreview = true;
        _webView.Dock = DockStyle.Fill;
        _webView.DefaultBackgroundColor = Color.FromArgb(30, 30, 33);
        Controls.Add(_webView);
        WireWindowEvents();
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        try
        {
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
            MessageBox.Show(_logger.Sanitize(exception.Message), "Avila", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    protected override async void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _nativeShell.Dispose();
        _remoteWebViews?.Dispose();
        await _workers.DisposeAsync().ConfigureAwait(false);
    }

    protected override void WndProc(ref Message m)
    {
        if (_nativeShell.HandleHotKeyMessage(m))
        {
            return;
        }

        base.WndProc(ref m);
    }

    private async Task InitializeWebViewAsync()
    {
        var userDataPath = GetUserDataPath();
        Directory.CreateDirectory(userDataPath);

        var environment = await CoreWebView2Environment.CreateAsync(null, userDataPath).ConfigureAwait(true);
        await _webView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
        if (!IsBrowserAppMode())
        {
            _remoteWebViews = new RemoteWebViewManager(this, environment, _project, PostAvilaEvent, _logger);
        }

        ConfigureWebViewSettings();
        if (!IsBrowserAppMode())
        {
            ConfigureBridge();
            _diagnostics.MarkBridgeReady();
        }
        ConfigureNavigationPolicy();

        if (!IsBrowserAppMode())
        {
            var script = SdkInjector.BuildInjectedScript(_project, _options.Mode, _capabilities.SessionCapability);
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script).ConfigureAwait(true);
        }

        if (!IsBrowserAppMode())
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
        else
        {
            var entryPath = _project.Manifest.App.Entry.Replace('\\', '/').TrimStart('/');
            _webView.CoreWebView2.Navigate($"https://{OriginPolicy.VirtualHost}/{entryPath}");
        }
    }

    private void ConfigureWebViewSettings()
    {
        var devToolsEnabled = _options.DevToolsOverride
            ?? (_options.Mode.Equals("dev", StringComparison.OrdinalIgnoreCase) && _project.Manifest.Security.DevTools);

        _webView.CoreWebView2.Settings.AreDevToolsEnabled = devToolsEnabled;
        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = _options.Mode.Equals("dev", StringComparison.OrdinalIgnoreCase);
        _webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = devToolsEnabled;
        _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
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
            if (_bridge is null)
            {
                return;
            }

            var response = await _bridge.HandleMessageAsync(args.Source, args.WebMessageAsJson).ConfigureAwait(true);
            _webView.CoreWebView2.PostWebMessageAsJson(response);
        };

        _webView.CoreWebView2.DOMContentLoaded += (_, _) => _diagnostics.MarkFirstPaint();
        _webView.CoreWebView2.SourceChanged += (_, _) => PostBrowserEvent("url", new { url = _webView.CoreWebView2.Source });
        _webView.CoreWebView2.DocumentTitleChanged += (_, _) => PostBrowserEvent("title", new { title = _webView.CoreWebView2.DocumentTitle });
        _webView.CoreWebView2.NavigationCompleted += (_, args) =>
        {
            if (args.IsSuccess)
            {
                PostBrowserEvent("loaded", new { url = _webView.CoreWebView2.Source });
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
            return;
        }

        try
        {
            var resolved = SafePath.ResolveInside(_project.RootPath, iconPath);
            if (File.Exists(resolved))
            {
                Icon = new Icon(resolved);
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

    private void WireWindowEvents()
    {
        Resize += (_, _) => PostWindowEvent("resize", new
        {
            width = ClientSize.Width,
            height = ClientSize.Height,
            state = WindowState.ToString().ToLowerInvariant()
        });
        Move += (_, _) => PostWindowEvent("move", new { x = Left, y = Top });
        Activated += (_, _) => PostWindowEvent("focus", new { focused = true });
        Deactivate += (_, _) => PostWindowEvent("blur", new { focused = false });
        KeyDown += (_, args) => _nativeShell.HandleLocalKeyDown(args);
        FormClosing += (_, args) => PostWindowEvent("closeRequested", new
        {
            reason = args.CloseReason.ToString(),
            cancellable = false
        });
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
}
