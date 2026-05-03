using System.Text.Json;
using Avila.Bridge;
using Microsoft.Web.WebView2.WinForms;

namespace Avila.Runtime;

public sealed class WebViewBrowserGateway : IBrowserGateway
{
    private readonly WebView2 _webView;

    public WebViewBrowserGateway(WebView2 webView)
    {
        _webView = webView;
    }

    public Task NavigateAsync(Uri uri, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => _webView.CoreWebView2.Navigate(uri.ToString()), cancellationToken);
    }

    public Task BackAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            if (_webView.CoreWebView2.CanGoBack)
            {
                _webView.CoreWebView2.GoBack();
            }
        }, cancellationToken);
    }

    public Task ForwardAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            if (_webView.CoreWebView2.CanGoForward)
            {
                _webView.CoreWebView2.GoForward();
            }
        }, cancellationToken);
    }

    public Task ReloadAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => _webView.CoreWebView2.Reload(), cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => _webView.CoreWebView2.Stop(), cancellationToken);
    }

    public Task<bool> CanGoBackAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => _webView.CoreWebView2.CanGoBack, cancellationToken);
    }

    public Task<bool> CanGoForwardAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => _webView.CoreWebView2.CanGoForward, cancellationToken);
    }

    public Task SetZoomAsync(double level, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => _webView.ZoomFactor = level, cancellationToken);
    }

    public async Task<bool> FindAsync(string text, CancellationToken cancellationToken)
    {
        var encoded = JsonSerializer.Serialize(text);
        var scriptTask = await OnUiThreadAsync(
            () => _webView.CoreWebView2.ExecuteScriptAsync($"window.find({encoded}, false, false, true, false, true, false)"),
            cancellationToken).ConfigureAwait(false);
        var result = await scriptTask.ConfigureAwait(false);

        return result.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public Task OpenDevToolsAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => _webView.CoreWebView2.OpenDevToolsWindow(), cancellationToken);
    }

    private Task OnUiThreadAsync(Action action, CancellationToken cancellationToken)
    {
        if (!_webView.InvokeRequired)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _webView.BeginInvoke(() =>
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
        if (!_webView.InvokeRequired)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(action());
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _webView.BeginInvoke(() =>
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
}
