using Avila.Bridge;

namespace Avila.Windowing;

public sealed class WinFormsWindowGateway : IWindowGateway
{
    private readonly Win32WindowController _controller;

    public WinFormsWindowGateway(Win32WindowController controller)
    {
        _controller = controller;
    }

    public Task CloseAsync(CancellationToken cancellationToken) => _controller.CloseAsync(cancellationToken);

    public Task ShowAsync(CancellationToken cancellationToken) => _controller.ShowAsync(cancellationToken);

    public Task HideAsync(CancellationToken cancellationToken) => _controller.HideAsync(cancellationToken);

    public Task FocusAsync(CancellationToken cancellationToken) => _controller.FocusAsync(cancellationToken);

    public Task BlurAsync(CancellationToken cancellationToken) => _controller.BlurAsync(cancellationToken);

    public Task MinimizeAsync(CancellationToken cancellationToken) => _controller.MinimizeAsync(cancellationToken);

    public Task MaximizeAsync(CancellationToken cancellationToken) => _controller.MaximizeAsync(cancellationToken);

    public Task RestoreAsync(CancellationToken cancellationToken) => _controller.RestoreAsync(cancellationToken);

    public Task ToggleMaximizeAsync(CancellationToken cancellationToken) => _controller.ToggleMaximizeAsync(cancellationToken);

    public Task<bool> IsMaximizedAsync(CancellationToken cancellationToken) => _controller.IsMaximizedAsync(cancellationToken);

    public Task SetFullscreenAsync(bool enabled, CancellationToken cancellationToken) => _controller.SetFullscreenAsync(enabled, cancellationToken);

    public Task<bool> IsFullscreenAsync(CancellationToken cancellationToken) => _controller.IsFullscreenAsync(cancellationToken);

    public Task SetAlwaysOnTopAsync(bool enabled, CancellationToken cancellationToken) => _controller.SetAlwaysOnTopAsync(enabled, cancellationToken);

    public Task SetTitleAsync(string title, CancellationToken cancellationToken) => _controller.SetTitleAsync(title, cancellationToken);

    public Task SetIconAsync(string path, CancellationToken cancellationToken) => _controller.SetIconAsync(path, cancellationToken);

    public Task SetSizeAsync(int width, int height, CancellationToken cancellationToken) => _controller.SetSizeAsync(width, height, cancellationToken);

    public Task SetMinSizeAsync(int width, int height, CancellationToken cancellationToken) => _controller.SetMinSizeAsync(width, height, cancellationToken);

    public Task SetMaxSizeAsync(int width, int height, CancellationToken cancellationToken) => _controller.SetMaxSizeAsync(width, height, cancellationToken);

    public Task SetPositionAsync(int x, int y, CancellationToken cancellationToken) => _controller.SetPositionAsync(x, y, cancellationToken);

    public Task CenterAsync(CancellationToken cancellationToken) => _controller.CenterAsync(cancellationToken);

    public Task<WindowBounds> GetBoundsAsync(CancellationToken cancellationToken) => _controller.GetBoundsAsync(cancellationToken);

    public Task SetResizableAsync(bool enabled, CancellationToken cancellationToken) => _controller.SetResizableAsync(enabled, cancellationToken);

    public Task SetDecorationsAsync(bool enabled, CancellationToken cancellationToken) => _controller.SetDecorationsAsync(enabled, cancellationToken);

    public Task SetOpacityAsync(double opacity, CancellationToken cancellationToken) => _controller.SetOpacityAsync(opacity, cancellationToken);

    public Task SetDraggableAsync(bool enabled, CancellationToken cancellationToken) => _controller.SetDraggableAsync(enabled, cancellationToken);

    public Task SetMicaAsync(bool enabled, CancellationToken cancellationToken) => _controller.SetMicaAsync(enabled, cancellationToken);

    public Task SetRoundedCornersAsync(bool enabled, CancellationToken cancellationToken) => _controller.SetRoundedCornersAsync(enabled, cancellationToken);

    public Task BeginDragAsync(CancellationToken cancellationToken) => _controller.BeginDragAsync(cancellationToken);
}

public sealed class WinFormsDialogGateway : IDialogGateway
{
    private readonly IWin32Window _owner;

    public WinFormsDialogGateway(IWin32Window owner)
    {
        _owner = owner;
    }

    public Task<IReadOnlyList<string>> OpenFileAsync(OpenFileRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var dialog = new OpenFileDialog
        {
            Title = string.IsNullOrWhiteSpace(request.Title) ? "Open file" : request.Title,
            Multiselect = request.Multiple,
            CheckFileExists = true,
            CheckPathExists = true,
            Filter = string.IsNullOrWhiteSpace(request.Filter) ? "All files (*.*)|*.*" : request.Filter
        };
        ApplyInitialDirectory(dialog, request.InitialDirectory);

        var result = dialog.ShowDialog(_owner);
        IReadOnlyList<string> files = result == DialogResult.OK ? dialog.FileNames : [];
        return Task.FromResult(files);
    }

    public Task<string?> SaveFileAsync(SaveFileRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var dialog = new SaveFileDialog
        {
            Title = string.IsNullOrWhiteSpace(request.Title) ? "Save file" : request.Title,
            CheckPathExists = true,
            Filter = string.IsNullOrWhiteSpace(request.Filter) ? "All files (*.*)|*.*" : request.Filter,
            FileName = request.FileName ?? "",
            DefaultExt = request.DefaultExtension ?? "",
            OverwritePrompt = request.OverwritePrompt
        };
        ApplyInitialDirectory(dialog, request.InitialDirectory);

        var result = dialog.ShowDialog(_owner);
        return Task.FromResult<string?>(result == DialogResult.OK ? dialog.FileName : null);
    }

    public Task<string?> SelectFolderAsync(SelectFolderRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var dialog = new FolderBrowserDialog
        {
            Description = string.IsNullOrWhiteSpace(request.Title) ? "Select folder" : request.Title,
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(request.InitialDirectory) ? request.InitialDirectory : ""
        };

        var result = dialog.ShowDialog(_owner);
        return Task.FromResult<string?>(result == DialogResult.OK ? dialog.SelectedPath : null);
    }

    public Task ShowMessageAsync(MessageDialogRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MessageBox.Show(
            _owner,
            request.Message,
            string.IsNullOrWhiteSpace(request.Title) ? "Avila" : request.Title,
            MessageBoxButtons.OK,
            ToIcon(request.Kind));
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(ConfirmDialogRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = MessageBox.Show(
            _owner,
            request.Message,
            string.IsNullOrWhiteSpace(request.Title) ? "Confirm" : request.Title,
            MessageBoxButtons.OKCancel,
            ToIcon(request.Kind));
        return Task.FromResult(result == DialogResult.OK);
    }

    private static void ApplyInitialDirectory(FileDialog dialog, string? initialDirectory)
    {
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }
    }

    private static MessageBoxIcon ToIcon(string kind)
    {
        return kind.ToLowerInvariant() switch
        {
            "error" => MessageBoxIcon.Error,
            "warning" => MessageBoxIcon.Warning,
            "question" => MessageBoxIcon.Question,
            _ => MessageBoxIcon.Information
        };
    }
}

public sealed class WinFormsClipboardGateway : IClipboardGateway
{
    private readonly Control _control;

    public WinFormsClipboardGateway(Control control)
    {
        _control = control;
    }

    public Task<string> ReadTextAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Clipboard.ContainsText() ? Clipboard.GetText(TextDataFormat.UnicodeText) : "";
        });
    }

    public Task WriteTextAsync(string text, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Clipboard.SetText(text, TextDataFormat.UnicodeText);
        });
    }

    public Task<string> ReadHtmlAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Clipboard.ContainsText(TextDataFormat.Html) ? Clipboard.GetText(TextDataFormat.Html) : "";
        });
    }

    public Task WriteHtmlAsync(string html, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Clipboard.SetText(html, TextDataFormat.Html);
        });
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Clipboard.Clear();
        });
    }

    private Task OnUiThreadAsync(Action action)
    {
        if (!_control.InvokeRequired)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _control.BeginInvoke(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        return completion.Task;
    }

    private Task<T> OnUiThreadAsync<T>(Func<T> action)
    {
        if (!_control.InvokeRequired)
        {
            return Task.FromResult(action());
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _control.BeginInvoke(() =>
        {
            try
            {
                completion.SetResult(action());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        return completion.Task;
    }
}
