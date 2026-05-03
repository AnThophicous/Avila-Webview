using System.Text.Json;

namespace Avila.Bridge;

public sealed partial class BridgeCommandRegistry
{
    private void RegisterRemoteWebViewCommands()
    {
        Register("webview.create", async (context, request, token) =>
        {
            var url = PayloadReader.GetString(request.Payload, "url", 2048, required: false);
            var bounds = ReadWebViewBounds(request.Payload);
            var hidden = PayloadReader.GetOptionalBoolean(request.Payload, "hidden", false);
            var activate = PayloadReader.GetOptionalBoolean(request.Payload, "activate", true);
            var userAgent = PayloadReader.GetString(request.Payload, "userAgent", 512, required: false);
            var uri = string.IsNullOrWhiteSpace(url) ? null : ParseRemoteUri(context, url);
            var snapshot = await context.RemoteWebViews.CreateAsync(
                new RemoteWebViewCreateRequest(uri, bounds, hidden, activate, string.IsNullOrWhiteSpace(userAgent) ? null : userAgent),
                token).ConfigureAwait(false);
            return snapshot;
        });

        Register("webview.destroy", async (context, request, token) =>
        {
            await context.RemoteWebViews.DestroyAsync(ReadWebViewId(request.Payload), token).ConfigureAwait(false);
            return new { destroyed = true };
        });

        Register("webview.navigate", async (context, request, token) =>
        {
            var id = ReadWebViewId(request.Payload);
            var url = PayloadReader.GetString(request.Payload, "url", 2048);
            await context.RemoteWebViews.NavigateAsync(id, ParseRemoteUri(context, url), token).ConfigureAwait(false);
            return new { navigated = true };
        });

        Register("webview.back", async (context, request, token) =>
        {
            await context.RemoteWebViews.BackAsync(ReadWebViewId(request.Payload), token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("webview.forward", async (context, request, token) =>
        {
            await context.RemoteWebViews.ForwardAsync(ReadWebViewId(request.Payload), token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("webview.reload", async (context, request, token) =>
        {
            await context.RemoteWebViews.ReloadAsync(ReadWebViewId(request.Payload), token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("webview.stop", async (context, request, token) =>
        {
            await context.RemoteWebViews.StopAsync(ReadWebViewId(request.Payload), token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("webview.show", async (context, request, token) =>
        {
            await context.RemoteWebViews.ShowAsync(ReadWebViewId(request.Payload), token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("webview.hide", async (context, request, token) =>
        {
            await context.RemoteWebViews.HideAsync(ReadWebViewId(request.Payload), token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("webview.focus", async (context, request, token) =>
        {
            await context.RemoteWebViews.FocusAsync(ReadWebViewId(request.Payload), token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("webview.setBounds", async (context, request, token) =>
        {
            await context.RemoteWebViews.SetBoundsAsync(ReadWebViewId(request.Payload), ReadWebViewBounds(request.Payload), token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("webview.setZoom", async (context, request, token) =>
        {
            var level = ReadDouble(request.Payload, "level", 0.25, 5);
            await context.RemoteWebViews.SetZoomAsync(ReadWebViewId(request.Payload), level, token).ConfigureAwait(false);
            return new { level };
        });

        Register("webview.find", async (context, request, token) =>
        {
            var found = await context.RemoteWebViews.FindAsync(
                ReadWebViewId(request.Payload),
                PayloadReader.GetString(request.Payload, "text", 256),
                token).ConfigureAwait(false);
            return new { found };
        });

        Register("webview.executeScript", async (context, request, token) =>
        {
            var result = await context.RemoteWebViews.ExecuteScriptAsync(
                ReadWebViewId(request.Payload),
                PayloadReader.GetString(request.Payload, "script", context.Project.Manifest.Security.MaxPayloadBytes),
                token).ConfigureAwait(false);
            return new { result };
        });

        Register("webview.injectCss", async (context, request, token) =>
        {
            await context.RemoteWebViews.InjectCssAsync(
                ReadWebViewId(request.Payload),
                PayloadReader.GetString(request.Payload, "css", context.Project.Manifest.Security.MaxPayloadBytes),
                token).ConfigureAwait(false);
            return new { injected = true };
        });

        Register("webview.screenshot", async (context, request, token) =>
        {
            var base64 = await context.RemoteWebViews.ScreenshotAsync(ReadWebViewId(request.Payload), token).ConfigureAwait(false);
            return new { base64, mime = "image/png" };
        });

        Register("webview.openDevTools", async (context, request, token) =>
        {
            if (!context.Project.Manifest.Security.DevTools)
            {
                throw new BridgeException(BridgeErrorCodes.PermissionDenied, "DevTools are disabled in avila.json.");
            }

            await context.RemoteWebViews.OpenDevToolsAsync(ReadWebViewId(request.Payload), token).ConfigureAwait(false);
            return new { opened = true };
        });

        Register("webview.list", async (context, _, token) =>
            (object?)await context.RemoteWebViews.ListAsync(token).ConfigureAwait(false));

        Register("tabs.create", async (context, request, token) =>
        {
            var url = PayloadReader.GetString(request.Payload, "url", 2048, required: false);
            var uri = string.IsNullOrWhiteSpace(url) ? null : ParseRemoteUri(context, url);
            var bounds = ReadWebViewBounds(request.Payload);
            return await context.RemoteWebViews.CreateAsync(new RemoteWebViewCreateRequest(uri, bounds, Hidden: false, Activate: true, UserAgent: null), token).ConfigureAwait(false);
        });

        Register("tabs.close", async (context, request, token) =>
        {
            await context.RemoteWebViews.DestroyAsync(ReadWebViewId(request.Payload), token).ConfigureAwait(false);
            return new { closed = true };
        });

        Register("tabs.activate", async (context, request, token) =>
            await context.RemoteWebViews.ActivateAsync(ReadWebViewId(request.Payload), token).ConfigureAwait(false));

        Register("tabs.active", async (context, _, token) =>
            await context.RemoteWebViews.GetActiveAsync(token).ConfigureAwait(false));

        Register("tabs.list", async (context, _, token) =>
            (object?)await context.RemoteWebViews.ListAsync(token).ConfigureAwait(false));

        Register("tabs.duplicate", async (context, request, token) =>
            await context.RemoteWebViews.DuplicateAsync(ReadWebViewId(request.Payload), token).ConfigureAwait(false));

        Register("tabs.reopenClosed", async (context, _, token) =>
            await context.RemoteWebViews.ReopenClosedAsync(token).ConfigureAwait(false));
    }

    private static string ReadWebViewId(JsonElement payload)
    {
        return PayloadReader.GetString(payload, "id", 96);
    }

    private static RemoteWebViewBounds ReadWebViewBounds(JsonElement payload)
    {
        var x = GetOptionalInt(payload, "x", -32768, 32767, 0);
        var y = GetOptionalInt(payload, "y", -32768, 32767, 0);
        var width = GetOptionalInt(payload, "width", 80, 7680, 1024);
        var height = GetOptionalInt(payload, "height", 80, 4320, 720);
        if (payload.TryGetProperty("bounds", out var bounds) && bounds.ValueKind == JsonValueKind.Object)
        {
            x = GetOptionalInt(bounds, "x", -32768, 32767, x);
            y = GetOptionalInt(bounds, "y", -32768, 32767, y);
            width = GetOptionalInt(bounds, "width", 80, 7680, width);
            height = GetOptionalInt(bounds, "height", 80, 4320, height);
        }

        return new RemoteWebViewBounds(x, y, width, height);
    }

    private static double ReadDouble(JsonElement payload, string name, double min, double max)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name} must be a number.");
        }

        var number = value.GetDouble();
        if (number < min || number > max)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name} is out of range.");
        }

        return number;
    }

    private static Uri ParseRemoteUri(BridgeCommandContext context, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, "URL must be absolute http or https.");
        }

        if (!context.Project.Manifest.Security.AllowRemoteContent || !new Avila.Security.OriginPolicy(context.Project.Manifest).IsAllowed(uri.ToString()))
        {
            throw new BridgeException(BridgeErrorCodes.PermissionDenied, "URL origin is not allowed by security.allowedOrigins.");
        }

        return uri;
    }
}
