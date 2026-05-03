using System.Runtime.InteropServices;
using Avila.Bridge;
using Avila.Security;

namespace Avila.Windowing;

public sealed class Win32WindowController
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_AUTO = 0;
    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_MAINWINDOW = 2;
    private const int DWMSBT_TABBEDWINDOW = 4;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 0x0002;

    private readonly Form _form;
    private bool _draggable = true;
    private bool _fullscreen;
    private bool _resizable = true;
    private bool _hasDecorations = true;
    private Rectangle _restoreBounds;
    private FormWindowState _restoreWindowState = FormWindowState.Normal;

    public Win32WindowController(Form form)
    {
        _form = form;
    }

    public void ApplyInitialWindowManifest(WindowManifest manifest)
    {
        _draggable = manifest.Draggable;
        _resizable = manifest.Resizable;
        _hasDecorations = !manifest.Borderless;
        _form.Text = _form.Text.Length == 0 ? "Avila App" : _form.Text;
        _form.StartPosition = manifest.Center ? FormStartPosition.CenterScreen : FormStartPosition.Manual;
        _form.ClientSize = new Size(manifest.Width, manifest.Height);
        _form.MinimumSize = new Size(manifest.MinWidth, manifest.MinHeight);
        _form.BackColor = Color.FromArgb(30, 30, 33);
        ApplyBorderStyle();

        ApplyDarkMode();
        SetRoundedCorners(manifest.RoundedCorners);

        if (manifest.Mica || manifest.MicaAlt)
        {
            SetMica(enabled: true, useAlt: manifest.MicaAlt);
        }
    }

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(_form.Close, cancellationToken);
    }

    public Task ShowAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(_form.Show, cancellationToken);
    }

    public Task HideAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(_form.Hide, cancellationToken);
    }

    public Task FocusAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            if (_form.WindowState == FormWindowState.Minimized)
            {
                _form.WindowState = FormWindowState.Normal;
            }

            _form.Show();
            _form.Activate();
        }, cancellationToken);
    }

    public Task BlurAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            _form.ActiveControl = null;
            SetForegroundWindow(GetDesktopWindow());
        }, cancellationToken);
    }

    public Task MinimizeAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => _form.WindowState = FormWindowState.Minimized, cancellationToken);
    }

    public Task MaximizeAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            if (!_resizable)
            {
                return;
            }

            _form.WindowState = FormWindowState.Maximized;
        }, cancellationToken);
    }

    public Task RestoreAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            if (_fullscreen)
            {
                SetFullscreen(false);
                return;
            }

            _form.WindowState = FormWindowState.Normal;
        }, cancellationToken);
    }

    public Task ToggleMaximizeAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            if (_form.WindowState == FormWindowState.Maximized)
            {
                _form.WindowState = FormWindowState.Normal;
                return;
            }

            if (_resizable)
            {
                _form.WindowState = FormWindowState.Maximized;
            }
        }, cancellationToken);
    }

    public Task<bool> IsMaximizedAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => _form.WindowState == FormWindowState.Maximized, cancellationToken);
    }

    public Task SetFullscreenAsync(bool enabled, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => SetFullscreen(enabled), cancellationToken);
    }

    public Task<bool> IsFullscreenAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => _fullscreen, cancellationToken);
    }

    public Task SetAlwaysOnTopAsync(bool enabled, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => _form.TopMost = enabled, cancellationToken);
    }

    public Task SetTitleAsync(string title, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => _form.Text = title, cancellationToken);
    }

    public Task SetIconAsync(string path, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Icon file was not found.", path);
            }

            using var icon = new Icon(path);
            _form.Icon = (Icon)icon.Clone();
        }, cancellationToken);
    }

    public Task SetSizeAsync(int width, int height, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => _form.ClientSize = new Size(width, height), cancellationToken);
    }

    public Task SetMinSizeAsync(int width, int height, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => _form.MinimumSize = new Size(width, height), cancellationToken);
    }

    public Task SetMaxSizeAsync(int width, int height, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => _form.MaximumSize = new Size(width, height), cancellationToken);
    }

    public Task SetPositionAsync(int x, int y, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => _form.Location = new Point(x, y), cancellationToken);
    }

    public Task CenterAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            var area = Screen.FromControl(_form).WorkingArea;
            _form.Location = new Point(
                area.Left + (area.Width - _form.Width) / 2,
                area.Top + (area.Height - _form.Height) / 2);
        }, cancellationToken);
    }

    public Task<WindowBounds> GetBoundsAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => new WindowBounds(
            _form.Left,
            _form.Top,
            _form.Width,
            _form.Height,
            _form.ClientSize.Width,
            _form.ClientSize.Height,
            _form.WindowState.ToString().ToLowerInvariant(),
            _form.Visible,
            _fullscreen,
            _resizable,
            _hasDecorations,
            _form.Opacity), cancellationToken);
    }

    public Task SetResizableAsync(bool enabled, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            _resizable = enabled;
            ApplyBorderStyle();
        }, cancellationToken);
    }

    public Task SetDecorationsAsync(bool enabled, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            _hasDecorations = enabled;
            ApplyBorderStyle();
        }, cancellationToken);
    }

    public Task SetOpacityAsync(double opacity, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => _form.Opacity = opacity, cancellationToken);
    }

    public Task SetDraggableAsync(bool enabled, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => _draggable = enabled, cancellationToken);
    }

    public Task SetMicaAsync(bool enabled, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => SetMica(enabled, useAlt: false), cancellationToken);
    }

    public Task SetRoundedCornersAsync(bool enabled, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() => SetRoundedCorners(enabled), cancellationToken);
    }

    public Task BeginDragAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            if (!_draggable || _form.WindowState == FormWindowState.Maximized)
            {
                return;
            }

            ReleaseCapture();
            SendMessage(_form.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        }, cancellationToken);
    }

    private void SetFullscreen(bool enabled)
    {
        if (enabled == _fullscreen)
        {
            return;
        }

        if (enabled)
        {
            _restoreBounds = _form.Bounds;
            _restoreWindowState = _form.WindowState;
            _form.WindowState = FormWindowState.Normal;
            _form.FormBorderStyle = FormBorderStyle.None;
            _form.Bounds = Screen.FromControl(_form).Bounds;
            _fullscreen = true;
            return;
        }

        _fullscreen = false;
        ApplyBorderStyle();
        _form.Bounds = _restoreBounds;
        _form.WindowState = _restoreWindowState;
    }

    private void ApplyBorderStyle()
    {
        if (_fullscreen)
        {
            return;
        }

        _form.FormBorderStyle = _hasDecorations
            ? _resizable ? FormBorderStyle.Sizable : FormBorderStyle.FixedSingle
            : FormBorderStyle.None;
        _form.MaximizeBox = _resizable;
        _form.MinimizeBox = true;
    }

    private void SetMica(bool enabled, bool useAlt)
    {
        if (!_form.IsHandleCreated)
        {
            return;
        }

        ApplyDarkMode();
        var value = enabled ? (useAlt ? DWMSBT_TABBEDWINDOW : DWMSBT_MAINWINDOW) : DWMSBT_NONE;
        TryDwmSetWindowAttribute(_form.Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref value);

        if (!enabled)
        {
            var auto = DWMSBT_AUTO;
            TryDwmSetWindowAttribute(_form.Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref auto);
        }
    }

    private void SetRoundedCorners(bool enabled)
    {
        if (!_form.IsHandleCreated)
        {
            return;
        }

        var preference = enabled ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
        TryDwmSetWindowAttribute(_form.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference);
    }

    private void ApplyDarkMode()
    {
        if (!_form.IsHandleCreated)
        {
            return;
        }

        var enabled = 1;
        TryDwmSetWindowAttribute(_form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled);
    }

    private static bool TryDwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value)
    {
        try
        {
            return DwmSetWindowAttribute(hwnd, attribute, ref value, Marshal.SizeOf<int>()) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    private Task OnUiThreadAsync(Action action, CancellationToken cancellationToken)
    {
        if (_form.IsDisposed)
        {
            return Task.CompletedTask;
        }

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
        if (_form.IsDisposed)
        {
            return Task.FromException<T>(new ObjectDisposedException(nameof(Form)));
        }

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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();
}
