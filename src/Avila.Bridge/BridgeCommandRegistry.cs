using System.Text;
using System.Runtime.InteropServices;
using Avila.Security;

namespace Avila.Bridge;

public sealed partial class BridgeCommandRegistry
{
    public const string ApiVersion = "0.3.0";

    private readonly Dictionary<string, BridgeCommand> _commands = new(StringComparer.Ordinal);

    public BridgeCommandRegistry()
    {
        RegisterBuiltIns();
    }

    public IReadOnlyCollection<BridgeCommand> Commands => _commands.Values.ToArray();

    public bool TryGet(string command, out BridgeCommand bridgeCommand) => _commands.TryGetValue(command, out bridgeCommand!);

    private void Register(
        string name,
        Func<BridgeCommandContext, BridgeRequest, CancellationToken, Task<object?>> handler,
        bool requiresPermission = true,
        bool allowRemote = false)
    {
        _commands[name] = new BridgeCommand(name, requiresPermission, allowRemote, handler);
    }

    private void RegisterBuiltIns()
    {
        Register("app.info", (context, _, _) =>
        {
            var app = context.Project.Manifest.App;
            return Task.FromResult<object?>(new
            {
                id = app.Id,
                name = app.Name,
                version = app.Version,
                apiVersion = ApiVersion,
                mode = context.Mode,
                runtime = "Avila",
                platform = Environment.OSVersion.Platform.ToString(),
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
            });
        });

        Register("app.apiVersion", (_, _, _) => Task.FromResult<object?>(new
        {
            version = ApiVersion
        }), requiresPermission: false);

        Register("app.has", (context, request, _) =>
        {
            var feature = PayloadReader.GetString(request.Payload, "feature", 96);
            var registered = TryGet(feature, out var command);
            var allowed = registered && (!command.RequiresPermission || new PermissionPolicy(context.Project.Manifest).IsAllowed(feature));
            return Task.FromResult<object?>(new
            {
                feature,
                registered,
                available = allowed
            });
        }, requiresPermission: false);

        Register("app.getArgs", (context, _, _) => Task.FromResult<object?>(new
        {
            args = context.Runtime.GetArgs()
        }));

        Register("app.getPath", (context, request, _) =>
        {
            var name = PayloadReader.GetString(request.Payload, "name", 32);
            return Task.FromResult<object?>(new
            {
                name,
                path = context.Runtime.GetPath(name)
            });
        });

        Register("app.quit", async (context, _, token) =>
        {
            await context.Runtime.QuitAsync(token).ConfigureAwait(false);
            return new { quitting = true };
        });

        Register("app.restart", async (context, _, token) =>
        {
            await context.Runtime.RestartAsync(token).ConfigureAwait(false);
            return new { restarting = true };
        });

        Register("app.openExternal", async (context, request, token) =>
        {
            var url = PayloadReader.GetString(request.Payload, "url", 2048);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "mailto"))
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, "app.openExternal only accepts http, https, or mailto URLs.");
            }

            await context.Runtime.OpenExternalAsync(uri, token).ConfigureAwait(false);
            return new { opened = true };
        });

        Register("app.revealPath", async (context, request, token) =>
        {
            var path = ResolveFilePath(context, request.Payload);
            await context.Runtime.RevealPathAsync(path, token).ConfigureAwait(false);
            return new { revealed = true };
        });

        Register("app.diagnostics", (context, _, _) =>
        {
            var snapshot = context.Diagnostics.Snapshot(context.Workers.QueueDepth, context.Workers.ActiveWorkers);
            return Task.FromResult<object?>(new
            {
                startupMs = snapshot.StartupElapsed.TotalMilliseconds,
                stage = snapshot.CurrentStage.ToString(),
                backgroundPreparationMs = snapshot.BackgroundPreparationAt?.TotalMilliseconds,
                webviewWorkingMs = snapshot.WebViewWorkingAt?.TotalMilliseconds,
                openMs = snapshot.OpenAt?.TotalMilliseconds,
                firstPaintMs = snapshot.FirstPaintAt?.TotalMilliseconds,
                bridgeReadyMs = snapshot.BridgeReadyAt?.TotalMilliseconds,
                initialWorkingSetBytes = snapshot.InitialWorkingSetBytes,
                postShrinkWorkingSetBytes = snapshot.PostShrinkWorkingSetBytes,
                workerQueueDepth = snapshot.WorkerQueueDepth,
                activeWorkers = snapshot.ActiveWorkers,
                bridgeCalls = snapshot.BridgeCalls,
                errors = snapshot.Errors,
                stageHits = snapshot.StageHits
            });
        });

        Register("system.ping", (_, _, _) => Task.FromResult<object?>(new
        {
            pong = true,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }));

        Register("system.info", (_, _, _) => Task.FromResult<object?>(new
        {
            os = RuntimeInformation.OSDescription,
            version = Environment.OSVersion.VersionString,
            architecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            processArchitecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            cpuCount = Environment.ProcessorCount,
            ramWorkingSetBytes = Environment.WorkingSet,
            uptimeMs = Environment.TickCount64,
            hostname = Environment.MachineName,
            locale = System.Globalization.CultureInfo.CurrentCulture.Name,
            timezone = TimeZoneInfo.Local.Id,
            monitors = Array.Empty<object>()
        }));

        RegisterNativeShellCommands();
        RegisterStorageCommands();

        Register("window.close", async (context, _, token) =>
        {
            await context.Window.CloseAsync(token).ConfigureAwait(false);
            return new { closed = true };
        });

        Register("window.show", async (context, _, token) =>
        {
            await context.Window.ShowAsync(token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.hide", async (context, _, token) =>
        {
            await context.Window.HideAsync(token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.focus", async (context, _, token) =>
        {
            await context.Window.FocusAsync(token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.blur", async (context, _, token) =>
        {
            await context.Window.BlurAsync(token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.minimize", async (context, _, token) =>
        {
            await context.Window.MinimizeAsync(token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.maximize", async (context, _, token) =>
        {
            await context.Window.MaximizeAsync(token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.restore", async (context, _, token) =>
        {
            await context.Window.RestoreAsync(token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.toggleMaximize", async (context, _, token) =>
        {
            await context.Window.ToggleMaximizeAsync(token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.isMaximized", async (context, _, token) => new
        {
            value = await context.Window.IsMaximizedAsync(token).ConfigureAwait(false)
        });

        Register("window.setFullscreen", async (context, request, token) =>
        {
            var enabled = PayloadReader.GetBoolean(request.Payload, "enabled");
            await context.Window.SetFullscreenAsync(enabled, token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.isFullscreen", async (context, _, token) => new
        {
            value = await context.Window.IsFullscreenAsync(token).ConfigureAwait(false)
        });

        Register("window.setAlwaysOnTop", async (context, request, token) =>
        {
            var enabled = PayloadReader.GetBoolean(request.Payload, "enabled");
            await context.Window.SetAlwaysOnTopAsync(enabled, token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.setTitle", async (context, request, token) =>
        {
            var title = PayloadReader.GetString(request.Payload, "title", 128);
            await context.Window.SetTitleAsync(title, token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.setIcon", async (context, request, token) =>
        {
            var relativePath = PayloadReader.GetString(request.Payload, "path", 512);
            var path = SafePath.ResolveInside(context.Project.RootPath, relativePath);
            await context.Window.SetIconAsync(path, token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.setSize", async (context, request, token) =>
        {
            var width = PayloadReader.GetInt(request.Payload, "width", 240, 7680);
            var height = PayloadReader.GetInt(request.Payload, "height", 160, 4320);
            await context.Window.SetSizeAsync(width, height, token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.setMinSize", async (context, request, token) =>
        {
            var width = PayloadReader.GetInt(request.Payload, "width", 120, 7680);
            var height = PayloadReader.GetInt(request.Payload, "height", 80, 4320);
            await context.Window.SetMinSizeAsync(width, height, token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.setMaxSize", async (context, request, token) =>
        {
            var width = PayloadReader.GetInt(request.Payload, "width", 0, 7680);
            var height = PayloadReader.GetInt(request.Payload, "height", 0, 4320);
            await context.Window.SetMaxSizeAsync(width, height, token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.setPosition", async (context, request, token) =>
        {
            var x = PayloadReader.GetInt(request.Payload, "x", -32768, 32767);
            var y = PayloadReader.GetInt(request.Payload, "y", -32768, 32767);
            await context.Window.SetPositionAsync(x, y, token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.center", async (context, _, token) =>
        {
            await context.Window.CenterAsync(token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.getBounds", async (context, _, token) =>
            (object?)await context.Window.GetBoundsAsync(token).ConfigureAwait(false));

        Register("window.setResizable", async (context, request, token) =>
        {
            var enabled = PayloadReader.GetBoolean(request.Payload, "enabled");
            await context.Window.SetResizableAsync(enabled, token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.setDecorations", async (context, request, token) =>
        {
            var enabled = PayloadReader.GetBoolean(request.Payload, "enabled");
            await context.Window.SetDecorationsAsync(enabled, token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.setOpacity", async (context, request, token) =>
        {
            if (!request.Payload.TryGetProperty("opacity", out var opacityValue) || opacityValue.ValueKind != System.Text.Json.JsonValueKind.Number)
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, "payload.opacity must be a number.");
            }

            var opacity = opacityValue.GetDouble();
            if (opacity is < 0.2 or > 1)
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Opacity must be between 0.2 and 1.");
            }

            await context.Window.SetOpacityAsync(opacity, token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.setDraggable", async (context, request, token) =>
        {
            var enabled = PayloadReader.GetBoolean(request.Payload, "enabled");
            await context.Window.SetDraggableAsync(enabled, token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.setMica", async (context, request, token) =>
        {
            var enabled = PayloadReader.GetBoolean(request.Payload, "enabled");
            await context.Window.SetMicaAsync(enabled, token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("window.beginDrag", async (context, _, token) =>
        {
            if (!context.Project.Manifest.Window.Draggable)
            {
                throw new BridgeException(BridgeErrorCodes.PermissionDenied, "Window dragging is disabled.");
            }

            await context.Window.BeginDragAsync(token).ConfigureAwait(false);
            return new { applied = true };
        }, requiresPermission: false);

        Register("dialog.openFile", async (context, request, token) =>
        {
            var title = PayloadReader.GetString(request.Payload, "title", 120, required: false);
            var filter = PayloadReader.GetString(request.Payload, "filter", 240, required: false);
            var initialDirectory = PayloadReader.GetString(request.Payload, "initialDirectory", 512, required: false);
            var multiple = PayloadReader.GetOptionalBoolean(request.Payload, "multiple", false);
            var files = await context.Dialog.OpenFileAsync(new OpenFileRequest(title, multiple, filter, initialDirectory), token).ConfigureAwait(false);
            return new { files };
        });

        Register("dialog.saveFile", async (context, request, token) =>
        {
            var title = PayloadReader.GetString(request.Payload, "title", 120, required: false);
            var filter = PayloadReader.GetString(request.Payload, "filter", 240, required: false);
            var initialDirectory = PayloadReader.GetString(request.Payload, "initialDirectory", 512, required: false);
            var fileName = PayloadReader.GetString(request.Payload, "fileName", 180, required: false);
            var defaultExtension = PayloadReader.GetString(request.Payload, "defaultExtension", 32, required: false);
            var overwritePrompt = PayloadReader.GetOptionalBoolean(request.Payload, "overwritePrompt", true);
            var file = await context.Dialog.SaveFileAsync(
                new SaveFileRequest(title, filter, initialDirectory, fileName, defaultExtension, overwritePrompt),
                token).ConfigureAwait(false);
            return new { file };
        });

        Register("dialog.selectFolder", async (context, request, token) =>
        {
            var title = PayloadReader.GetString(request.Payload, "title", 120, required: false);
            var initialDirectory = PayloadReader.GetString(request.Payload, "initialDirectory", 512, required: false);
            var folder = await context.Dialog.SelectFolderAsync(new SelectFolderRequest(title, initialDirectory), token).ConfigureAwait(false);
            return new { folder };
        });

        Register("dialog.message", async (context, request, token) =>
        {
            var title = PayloadReader.GetString(request.Payload, "title", 120, required: false);
            var message = PayloadReader.GetString(request.Payload, "message", 2000);
            var kind = PayloadReader.GetString(request.Payload, "kind", 24, required: false);
            await context.Dialog.ShowMessageAsync(new MessageDialogRequest(title, message, kind), token).ConfigureAwait(false);
            return new { shown = true };
        });

        Register("dialog.confirm", async (context, request, token) =>
        {
            var title = PayloadReader.GetString(request.Payload, "title", 120, required: false);
            var message = PayloadReader.GetString(request.Payload, "message", 2000);
            var kind = PayloadReader.GetString(request.Payload, "kind", 24, required: false);
            var confirmed = await context.Dialog.ConfirmAsync(new ConfirmDialogRequest(title, message, kind), token).ConfigureAwait(false);
            return new { confirmed };
        });

        Register("clipboard.readText", async (context, _, token) => new
        {
            text = await context.Clipboard.ReadTextAsync(token).ConfigureAwait(false)
        });

        Register("clipboard.writeText", async (context, request, token) =>
        {
            var text = PayloadReader.GetString(request.Payload, "text", context.Project.Manifest.Security.MaxPayloadBytes);
            await context.Clipboard.WriteTextAsync(text, token).ConfigureAwait(false);
            return new { written = true };
        });

        Register("clipboard.readHtml", async (context, _, token) => new
        {
            html = await context.Clipboard.ReadHtmlAsync(token).ConfigureAwait(false)
        });

        Register("clipboard.writeHtml", async (context, request, token) =>
        {
            var html = PayloadReader.GetString(request.Payload, "html", context.Project.Manifest.Security.MaxPayloadBytes);
            await context.Clipboard.WriteHtmlAsync(html, token).ConfigureAwait(false);
            return new { written = true };
        });

        Register("clipboard.clear", async (context, _, token) =>
        {
            await context.Clipboard.ClearAsync(token).ConfigureAwait(false);
            return new { cleared = true };
        });

        Register("browser.navigate", async (context, request, token) =>
        {
            var url = PayloadReader.GetString(request.Payload, "url", 2048);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, "browser.navigate only accepts absolute http or https URLs.");
            }

            if (!context.Project.Manifest.Security.AllowRemoteContent)
            {
                throw new BridgeException(BridgeErrorCodes.PermissionDenied, "Remote navigation is disabled by security.allowRemoteContent.");
            }

            var originPolicy = new OriginPolicy(context.Project.Manifest);
            if (!originPolicy.IsAllowed(uri.ToString()))
            {
                throw new BridgeException(BridgeErrorCodes.PermissionDenied, "Navigation origin is not allowed by security.allowedOrigins.");
            }

            await context.Browser.NavigateAsync(uri, token).ConfigureAwait(false);
            return new { url = uri.ToString() };
        });

        Register("browser.back", async (context, _, token) =>
        {
            await context.Browser.BackAsync(token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("browser.forward", async (context, _, token) =>
        {
            await context.Browser.ForwardAsync(token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("browser.reload", async (context, _, token) =>
        {
            await context.Browser.ReloadAsync(token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("browser.stop", async (context, _, token) =>
        {
            await context.Browser.StopAsync(token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("browser.canGoBack", async (context, _, token) => new
        {
            value = await context.Browser.CanGoBackAsync(token).ConfigureAwait(false)
        });

        Register("browser.canGoForward", async (context, _, token) => new
        {
            value = await context.Browser.CanGoForwardAsync(token).ConfigureAwait(false)
        });

        Register("browser.setZoom", async (context, request, token) =>
        {
            if (!request.Payload.TryGetProperty("level", out var levelValue) || levelValue.ValueKind != System.Text.Json.JsonValueKind.Number)
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, "payload.level must be a number.");
            }

            var level = levelValue.GetDouble();
            if (level is < 0.25 or > 5)
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Zoom level must be between 0.25 and 5.");
            }

            await context.Browser.SetZoomAsync(level, token).ConfigureAwait(false);
            return new { level };
        });

        Register("browser.find", async (context, request, token) =>
        {
            var text = PayloadReader.GetString(request.Payload, "text", 256);
            var found = await context.Browser.FindAsync(text, token).ConfigureAwait(false);
            return new { found };
        });

        Register("browser.openDevTools", async (context, _, token) =>
        {
            if (!context.Project.Manifest.Security.DevTools)
            {
                throw new BridgeException(BridgeErrorCodes.PermissionDenied, "DevTools are disabled in avila.json.");
            }

            await context.Browser.OpenDevToolsAsync(token).ConfigureAwait(false);
            return new { opened = true };
        });

        RegisterRemoteWebViewCommands();

        Register("fs.readFile", async (context, request, token) =>
        {
            var relativePath = PayloadReader.GetString(request.Payload, "path", 512);
            var path = SafePath.ResolveInside(context.Project.RootPath, relativePath);
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, "File was not found.");
            }

            if (info.Length > 5_242_880)
            {
                throw new BridgeException(BridgeErrorCodes.PayloadTooLarge, "File is larger than the allowed read limit.");
            }

            return await context.Workers.EnqueueAsync<object?>(async workerToken =>
            {
                var content = await File.ReadAllTextAsync(path, Encoding.UTF8, workerToken).ConfigureAwait(false);
                return new { content };
            }, cancellationToken: token).ConfigureAwait(false);
        });

        Register("fs.writeFile", async (context, request, token) =>
        {
            var relativePath = PayloadReader.GetString(request.Payload, "path", 512);
            var content = PayloadReader.GetString(request.Payload, "content", 5_242_880);
            var path = SafePath.ResolveInside(context.Project.RootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            return await context.Workers.EnqueueAsync<object?>(async workerToken =>
            {
                await File.WriteAllTextAsync(path, content, Encoding.UTF8, workerToken).ConfigureAwait(false);
                return new { written = true };
            }, cancellationToken: token).ConfigureAwait(false);
        });

        RegisterFileSystemCommands();

        Register("os.exec", (_, _, _) =>
            throw new BridgeException(BridgeErrorCodes.NotImplemented, "os.exec requires a command allowlist and is not enabled in this MVP."));

        RegisterProcessCommands();
        RegisterNodeCommands();
        RegisterNetworkCommands();
    }
}

public sealed record BridgeCommand(
    string Name,
    bool RequiresPermission,
    bool AllowRemote,
    Func<BridgeCommandContext, BridgeRequest, CancellationToken, Task<object?>> Handler);
