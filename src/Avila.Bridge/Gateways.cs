namespace Avila.Bridge;

public interface IRuntimeGateway
{
    IReadOnlyList<string> GetArgs();

    string GetPath(string name);

    string ProtectSecret(string value);

    string UnprotectSecret(string protectedValue);

    Task QuitAsync(CancellationToken cancellationToken);

    Task RestartAsync(CancellationToken cancellationToken);

    Task OpenExternalAsync(Uri uri, CancellationToken cancellationToken);

    Task RevealPathAsync(string path, CancellationToken cancellationToken);
}

public interface INodeHostGateway
{
    Task<NodeRunResult> RunAsync(string script, IReadOnlyList<string> args, CancellationToken cancellationToken);
}

public interface INativeShellGateway
{
    Task TrayShowAsync(NativeTrayRequest request, CancellationToken cancellationToken);

    Task TrayHideAsync(CancellationToken cancellationToken);

    Task TraySetTooltipAsync(string tooltip, CancellationToken cancellationToken);

    Task TraySetMenuAsync(IReadOnlyList<NativeMenuItem> items, CancellationToken cancellationToken);

    Task SetWindowMenuAsync(IReadOnlyList<NativeMenuItem> items, CancellationToken cancellationToken);

    Task ShowContextMenuAsync(IReadOnlyList<NativeMenuItem> items, CancellationToken cancellationToken);

    Task ShowNotificationAsync(NativeNotificationRequest request, CancellationToken cancellationToken);

    Task RegisterShortcutAsync(NativeShortcutRequest request, CancellationToken cancellationToken);

    Task UnregisterShortcutAsync(string id, CancellationToken cancellationToken);

    Task ClearShortcutsAsync(CancellationToken cancellationToken);

    Task SetTaskbarProgressAsync(TaskbarProgressRequest request, CancellationToken cancellationToken);
}

public sealed record NativeTrayRequest(string? IconPath, string? Tooltip, IReadOnlyList<NativeMenuItem> Menu);

public sealed record NativeMenuItem(
    string Id,
    string Label,
    string Type,
    bool Enabled,
    bool Checked,
    string? Accelerator,
    IReadOnlyList<NativeMenuItem> Items);

public sealed record NativeNotificationRequest(string Id, string Title, string Body, string? IconPath, int TimeoutMs);

public sealed record NativeShortcutRequest(string Id, string Accelerator, bool Global);

public sealed record TaskbarProgressRequest(string State, int Value, int Maximum);

public sealed record NodeRunResult(
    string Script,
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

public interface IWindowGateway
{
    Task CloseAsync(CancellationToken cancellationToken);

    Task ShowAsync(CancellationToken cancellationToken);

    Task HideAsync(CancellationToken cancellationToken);

    Task FocusAsync(CancellationToken cancellationToken);

    Task BlurAsync(CancellationToken cancellationToken);

    Task MinimizeAsync(CancellationToken cancellationToken);

    Task MaximizeAsync(CancellationToken cancellationToken);

    Task RestoreAsync(CancellationToken cancellationToken);

    Task ToggleMaximizeAsync(CancellationToken cancellationToken);

    Task<bool> IsMaximizedAsync(CancellationToken cancellationToken);

    Task SetFullscreenAsync(bool enabled, CancellationToken cancellationToken);

    Task<bool> IsFullscreenAsync(CancellationToken cancellationToken);

    Task SetAlwaysOnTopAsync(bool enabled, CancellationToken cancellationToken);

    Task SetTitleAsync(string title, CancellationToken cancellationToken);

    Task SetIconAsync(string path, CancellationToken cancellationToken);

    Task SetSizeAsync(int width, int height, CancellationToken cancellationToken);

    Task SetMinSizeAsync(int width, int height, CancellationToken cancellationToken);

    Task SetMaxSizeAsync(int width, int height, CancellationToken cancellationToken);

    Task SetPositionAsync(int x, int y, CancellationToken cancellationToken);

    Task CenterAsync(CancellationToken cancellationToken);

    Task<WindowBounds> GetBoundsAsync(CancellationToken cancellationToken);

    Task SetResizableAsync(bool enabled, CancellationToken cancellationToken);

    Task SetDecorationsAsync(bool enabled, CancellationToken cancellationToken);

    Task SetOpacityAsync(double opacity, CancellationToken cancellationToken);

    Task SetDraggableAsync(bool enabled, CancellationToken cancellationToken);

    Task SetMicaAsync(bool enabled, CancellationToken cancellationToken);

    Task SetRoundedCornersAsync(bool enabled, CancellationToken cancellationToken);

    Task BeginDragAsync(CancellationToken cancellationToken);
}

public sealed record WindowBounds(
    int X,
    int Y,
    int Width,
    int Height,
    int ClientWidth,
    int ClientHeight,
    string State,
    bool IsVisible,
    bool IsFullscreen,
    bool IsResizable,
    bool HasDecorations,
    double Opacity);

public interface IDialogGateway
{
    Task<IReadOnlyList<string>> OpenFileAsync(OpenFileRequest request, CancellationToken cancellationToken);

    Task<string?> SaveFileAsync(SaveFileRequest request, CancellationToken cancellationToken);

    Task<string?> SelectFolderAsync(SelectFolderRequest request, CancellationToken cancellationToken);

    Task ShowMessageAsync(MessageDialogRequest request, CancellationToken cancellationToken);

    Task<bool> ConfirmAsync(ConfirmDialogRequest request, CancellationToken cancellationToken);
}

public sealed record OpenFileRequest(
    string? Title,
    bool Multiple,
    string? Filter,
    string? InitialDirectory);

public sealed record SaveFileRequest(
    string? Title,
    string? Filter,
    string? InitialDirectory,
    string? FileName,
    string? DefaultExtension,
    bool OverwritePrompt);

public sealed record SelectFolderRequest(string? Title, string? InitialDirectory);

public sealed record MessageDialogRequest(string? Title, string Message, string Kind);

public sealed record ConfirmDialogRequest(string? Title, string Message, string Kind);

public interface IClipboardGateway
{
    Task<string> ReadTextAsync(CancellationToken cancellationToken);

    Task WriteTextAsync(string text, CancellationToken cancellationToken);

    Task<string> ReadHtmlAsync(CancellationToken cancellationToken);

    Task WriteHtmlAsync(string html, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}

public interface IBrowserGateway
{
    Task NavigateAsync(Uri uri, CancellationToken cancellationToken);

    Task BackAsync(CancellationToken cancellationToken);

    Task ForwardAsync(CancellationToken cancellationToken);

    Task ReloadAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    Task<bool> CanGoBackAsync(CancellationToken cancellationToken);

    Task<bool> CanGoForwardAsync(CancellationToken cancellationToken);

    Task SetZoomAsync(double level, CancellationToken cancellationToken);

    Task<bool> FindAsync(string text, CancellationToken cancellationToken);

    Task OpenDevToolsAsync(CancellationToken cancellationToken);
}

public interface IRemoteWebViewGateway
{
    Task<RemoteWebViewSnapshot> CreateAsync(RemoteWebViewCreateRequest request, CancellationToken cancellationToken);

    Task DestroyAsync(string id, CancellationToken cancellationToken);

    Task NavigateAsync(string id, Uri uri, CancellationToken cancellationToken);

    Task BackAsync(string id, CancellationToken cancellationToken);

    Task ForwardAsync(string id, CancellationToken cancellationToken);

    Task ReloadAsync(string id, CancellationToken cancellationToken);

    Task StopAsync(string id, CancellationToken cancellationToken);

    Task ShowAsync(string id, CancellationToken cancellationToken);

    Task HideAsync(string id, CancellationToken cancellationToken);

    Task FocusAsync(string id, CancellationToken cancellationToken);

    Task SetBoundsAsync(string id, RemoteWebViewBounds bounds, CancellationToken cancellationToken);

    Task SetZoomAsync(string id, double level, CancellationToken cancellationToken);

    Task<bool> FindAsync(string id, string text, CancellationToken cancellationToken);

    Task<string> ExecuteScriptAsync(string id, string script, CancellationToken cancellationToken);

    Task InjectCssAsync(string id, string css, CancellationToken cancellationToken);

    Task<string> ScreenshotAsync(string id, CancellationToken cancellationToken);

    Task OpenDevToolsAsync(string id, CancellationToken cancellationToken);

    Task<RemoteWebViewSnapshot?> ActivateAsync(string id, CancellationToken cancellationToken);

    Task<RemoteWebViewSnapshot?> GetActiveAsync(CancellationToken cancellationToken);

    Task<RemoteWebViewSnapshot?> DuplicateAsync(string id, CancellationToken cancellationToken);

    Task<RemoteWebViewSnapshot?> ReopenClosedAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<RemoteWebViewSnapshot>> ListAsync(CancellationToken cancellationToken);
}

public sealed record RemoteWebViewCreateRequest(
    Uri? Url,
    RemoteWebViewBounds Bounds,
    bool Hidden,
    bool Activate,
    string? UserAgent);

public sealed record RemoteWebViewBounds(int X, int Y, int Width, int Height);

public sealed record RemoteWebViewSnapshot(
    string Id,
    string Url,
    string Title,
    bool IsVisible,
    bool IsActive,
    bool CanGoBack,
    bool CanGoForward,
    double Zoom,
    RemoteWebViewBounds Bounds);
