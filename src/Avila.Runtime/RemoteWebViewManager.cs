using System.Text.Json;
using Avila.Bridge;
using Avila.Diagnostics;
using Avila.Security;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Avila.Runtime;

public sealed class RemoteWebViewManager : IRemoteWebViewGateway, IDisposable
{
    private readonly Form _form;
    private readonly CoreWebView2Environment _environment;
    private readonly AvilaProject _project;
    private readonly Action<string, object> _postEvent;
    private readonly SafeLogger _logger;
    private readonly OriginPolicy _originPolicy;
    private readonly Dictionary<string, RemoteView> _views = new(StringComparer.Ordinal);
    private readonly Stack<ClosedView> _closedViews = new();
    private string? _activeId;

    public RemoteWebViewManager(
        Form form,
        CoreWebView2Environment environment,
        AvilaProject project,
        Action<string, object> postEvent,
        SafeLogger logger)
    {
        _form = form;
        _environment = environment;
        _project = project;
        _postEvent = postEvent;
        _logger = logger;
        _originPolicy = new OriginPolicy(project.Manifest);
    }

    public async Task<RemoteWebViewSnapshot> CreateAsync(RemoteWebViewCreateRequest request, CancellationToken cancellationToken)
    {
        return await OnUiThreadAsync(async () =>
        {
            var id = Guid.NewGuid().ToString("N");
            var webView = new WebView2
            {
                Name = $"avila-remote-{id}",
                Bounds = ToRectangle(request.Bounds),
                Visible = !request.Hidden,
                DefaultBackgroundColor = Color.FromArgb(24, 24, 27)
            };

            _form.Controls.Add(webView);
            webView.BringToFront();
            await webView.EnsureCoreWebView2Async(_environment).ConfigureAwait(true);
            ConfigureSettings(webView, request.UserAgent);

            var view = new RemoteView(id, webView, request.Bounds);
            _views[id] = view;
            WireEvents(view);

            if (request.Url is not null)
            {
                ValidateRemoteUri(request.Url);
                view.Url = request.Url.ToString();
                webView.CoreWebView2.Navigate(request.Url.ToString());
            }

            if (request.Activate || _activeId is null)
            {
                ActivateView(id);
            }

            PostEvent("created", Snapshot(view));
            return Snapshot(view);
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task DestroyAsync(string id, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            var view = Require(id);
            _views.Remove(id);
            _closedViews.Push(new ClosedView(view.Url, view.Title, view.Bounds));
            if (_activeId == id)
            {
                _activeId = _views.Keys.FirstOrDefault();
                if (_activeId is not null)
                {
                    ActivateView(_activeId);
                }
            }

            _form.Controls.Remove(view.WebView);
            view.WebView.Dispose();
            PostEvent("destroyed", new { id });
        }, cancellationToken);
    }

    public Task NavigateAsync(string id, Uri uri, CancellationToken cancellationToken)
    {
        ValidateRemoteUri(uri);
        return OnUiThreadAsync(() =>
        {
            var view = Require(id);
            view.Url = uri.ToString();
            view.WebView.CoreWebView2.Navigate(uri.ToString());
        }, cancellationToken);
    }

    public Task BackAsync(string id, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            var view = Require(id);
            if (view.WebView.CoreWebView2.CanGoBack)
            {
                view.WebView.CoreWebView2.GoBack();
            }
        }, cancellationToken);
    }

    public Task ForwardAsync(string id, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            var view = Require(id);
            if (view.WebView.CoreWebView2.CanGoForward)
            {
                view.WebView.CoreWebView2.GoForward();
            }
        }, cancellationToken);
    }

    public Task ReloadAsync(string id, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => Require(id).WebView.CoreWebView2.Reload(), cancellationToken);
    }

    public Task StopAsync(string id, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => Require(id).WebView.CoreWebView2.Stop(), cancellationToken);
    }

    public Task ShowAsync(string id, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            var view = Require(id);
            view.WebView.Visible = true;
            view.WebView.BringToFront();
            PostEvent("shown", Snapshot(view));
        }, cancellationToken);
    }

    public Task HideAsync(string id, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            var view = Require(id);
            view.WebView.Visible = false;
            PostEvent("hidden", Snapshot(view));
        }, cancellationToken);
    }

    public Task FocusAsync(string id, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            var view = Require(id);
            view.WebView.Focus();
        }, cancellationToken);
    }

    public Task SetBoundsAsync(string id, RemoteWebViewBounds bounds, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            var view = Require(id);
            view.Bounds = bounds;
            view.WebView.Bounds = ToRectangle(bounds);
            PostEvent("bounds", Snapshot(view));
        }, cancellationToken);
    }

    public Task SetZoomAsync(string id, double level, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            var view = Require(id);
            view.WebView.ZoomFactor = level;
        }, cancellationToken);
    }

    public async Task<bool> FindAsync(string id, string text, CancellationToken cancellationToken)
    {
        var encoded = JsonSerializer.Serialize(text);
        var result = await ExecuteScriptAsync(id, $"window.find({encoded}, false, false, true, false, true, false)", cancellationToken).ConfigureAwait(false);
        return result.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public Task<string> ExecuteScriptAsync(string id, string script, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(async () =>
        {
            var view = Require(id);
            return await view.WebView.CoreWebView2.ExecuteScriptAsync(script).ConfigureAwait(true);
        }, cancellationToken);
    }

    public Task InjectCssAsync(string id, string css, CancellationToken cancellationToken)
    {
        var encoded = JsonSerializer.Serialize(css);
        var script = $$"""
(() => {
  const style = document.createElement("style");
  style.dataset.avilaInjected = "true";
  style.textContent = {{encoded}};
  document.documentElement.appendChild(style);
})();
""";
        return ExecuteScriptAsync(id, script, cancellationToken);
    }

    public Task<string> ScreenshotAsync(string id, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(async () =>
        {
            var view = Require(id);
            await using var stream = new MemoryStream();
            await view.WebView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream).ConfigureAwait(true);
            return Convert.ToBase64String(stream.ToArray());
        }, cancellationToken);
    }

    public Task OpenDevToolsAsync(string id, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => Require(id).WebView.CoreWebView2.OpenDevToolsWindow(), cancellationToken);
    }

    public Task<RemoteWebViewSnapshot?> ActivateAsync(string id, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync<RemoteWebViewSnapshot?>(() =>
        {
            var view = Require(id);
            ActivateView(id);
            return Snapshot(view);
        }, cancellationToken);
    }

    public Task<RemoteWebViewSnapshot?> GetActiveAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync<RemoteWebViewSnapshot?>(() =>
        {
            return _activeId is null || !_views.TryGetValue(_activeId, out var view)
                ? null
                : Snapshot(view);
        }, cancellationToken);
    }

    public async Task<RemoteWebViewSnapshot?> DuplicateAsync(string id, CancellationToken cancellationToken)
    {
        var view = await OnUiThreadAsync(() => Require(id), cancellationToken).ConfigureAwait(false);
        var url = Uri.TryCreate(view.Url, UriKind.Absolute, out var uri) ? uri : null;
        return await CreateAsync(new RemoteWebViewCreateRequest(url, Offset(view.Bounds), Hidden: false, Activate: true, UserAgent: null), cancellationToken).ConfigureAwait(false);
    }

    public async Task<RemoteWebViewSnapshot?> ReopenClosedAsync(CancellationToken cancellationToken)
    {
        ClosedView? closed = null;
        await OnUiThreadAsync(() =>
        {
            if (_closedViews.Count > 0)
            {
                closed = _closedViews.Pop();
            }
        }, cancellationToken).ConfigureAwait(false);

        if (closed is null)
        {
            return null;
        }

        var url = Uri.TryCreate(closed.Url, UriKind.Absolute, out var uri) ? uri : null;
        return await CreateAsync(new RemoteWebViewCreateRequest(url, closed.Bounds, Hidden: false, Activate: true, UserAgent: null), cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<RemoteWebViewSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync<IReadOnlyList<RemoteWebViewSnapshot>>(() => _views.Values.Select(Snapshot).ToArray(), cancellationToken);
    }

    public void Dispose()
    {
        foreach (var view in _views.Values.ToArray())
        {
            _form.Controls.Remove(view.WebView);
            view.WebView.Dispose();
        }

        _views.Clear();
    }

    private void ConfigureSettings(WebView2 webView, string? userAgent)
    {
        webView.CoreWebView2.Settings.AreDevToolsEnabled = _project.Manifest.Security.DevTools;
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        webView.CoreWebView2.Settings.IsZoomControlEnabled = true;
        webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            webView.CoreWebView2.Settings.UserAgent = userAgent;
        }
    }

    private void WireEvents(RemoteView view)
    {
        view.WebView.CoreWebView2.NavigationStarting += (_, args) =>
        {
            if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri))
            {
                args.Cancel = true;
                return;
            }

            try
            {
                ValidateRemoteUri(uri);
                view.Url = uri.ToString();
                PostEvent("loading", new { id = view.Id, url = args.Uri });
            }
            catch (Exception exception)
            {
                args.Cancel = true;
                _logger.Warning($"Blocked remote WebView navigation: {exception.Message}");
                PostEvent("error", new { id = view.Id, url = args.Uri, status = "BlockedByPolicy" });
            }
        };

        view.WebView.CoreWebView2.SourceChanged += (_, _) =>
        {
            view.Url = view.WebView.CoreWebView2.Source;
            PostEvent("url", Snapshot(view));
        };
        view.WebView.CoreWebView2.DocumentTitleChanged += (_, _) =>
        {
            view.Title = view.WebView.CoreWebView2.DocumentTitle;
            PostEvent("title", Snapshot(view));
        };
        view.WebView.CoreWebView2.NavigationCompleted += (_, args) =>
        {
            PostEvent(args.IsSuccess ? "loaded" : "error", args.IsSuccess
                ? Snapshot(view)
                : new { id = view.Id, url = view.Url, status = args.WebErrorStatus.ToString() });
        };
        view.WebView.CoreWebView2.ProcessFailed += (_, args) =>
        {
            PostEvent("crash", new { id = view.Id, reason = args.ProcessFailedKind.ToString() });
        };
        view.WebView.CoreWebView2.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri))
            {
                return;
            }

            _ = CreateAsync(new RemoteWebViewCreateRequest(uri, Offset(view.Bounds), Hidden: false, Activate: true, UserAgent: null), CancellationToken.None);
        };
    }

    private void ActivateView(string id)
    {
        var view = Require(id);
        _activeId = id;
        foreach (var item in _views.Values)
        {
            item.WebView.Visible = item.Id == id;
        }

        view.WebView.BringToFront();
        view.WebView.Focus();
        PostEvent("active", Snapshot(view));
    }

    private RemoteView Require(string id)
    {
        if (!_views.TryGetValue(id, out var view))
        {
            throw new InvalidOperationException($"Remote WebView was not found: {id}");
        }

        return view;
    }

    private void ValidateRemoteUri(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Remote WebViews only accept http or https URLs.");
        }

        if (!_project.Manifest.Security.AllowRemoteContent || !_originPolicy.IsAllowed(uri.ToString()))
        {
            throw new InvalidOperationException("Remote WebView origin is not allowed by security.allowedOrigins.");
        }
    }

    private RemoteWebViewSnapshot Snapshot(RemoteView view)
    {
        var core = view.WebView.CoreWebView2;
        return new RemoteWebViewSnapshot(
            view.Id,
            view.Url,
            view.Title,
            view.WebView.Visible,
            view.Id == _activeId,
            core?.CanGoBack ?? false,
            core?.CanGoForward ?? false,
            view.WebView.ZoomFactor,
            view.Bounds);
    }

    private void PostEvent(string name, object payload)
    {
        _postEvent($"webview.{name}", payload);
    }

    private Task OnUiThreadAsync(Action action, CancellationToken cancellationToken)
    {
        if (!_form.InvokeRequired)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _form.BeginInvoke(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                action();
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
    }

    private Task<T> OnUiThreadAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        if (!_form.InvokeRequired)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(action());
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _form.BeginInvoke(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                completion.TrySetResult(action());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
    }

    private Task<T> OnUiThreadAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        if (!_form.InvokeRequired)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return action();
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _form.BeginInvoke(async () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                completion.TrySetResult(await action().ConfigureAwait(true));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
    }

    private static Rectangle ToRectangle(RemoteWebViewBounds bounds)
    {
        return new Rectangle(bounds.X, bounds.Y, Math.Max(80, bounds.Width), Math.Max(80, bounds.Height));
    }

    private static RemoteWebViewBounds Offset(RemoteWebViewBounds bounds)
    {
        return new RemoteWebViewBounds(bounds.X + 24, bounds.Y + 24, bounds.Width, bounds.Height);
    }

    private sealed class RemoteView
    {
        public RemoteView(string id, WebView2 webView, RemoteWebViewBounds bounds)
        {
            Id = id;
            WebView = webView;
            Bounds = bounds;
        }

        public string Id { get; }

        public WebView2 WebView { get; }

        public RemoteWebViewBounds Bounds { get; set; }

        public string Url { get; set; } = "";

        public string Title { get; set; } = "";
    }

    private sealed record ClosedView(string Url, string Title, RemoteWebViewBounds Bounds);
}
