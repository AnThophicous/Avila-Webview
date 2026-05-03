using System.Runtime.InteropServices;
using Avila.Bridge;
using Avila.Diagnostics;

namespace Avila.Runtime;

public sealed class NativeShellGateway : INativeShellGateway, IDisposable
{
    private readonly Form _form;
    private readonly Action<string, object> _postEvent;
    private readonly SafeLogger _logger;
    private readonly NotifyIcon _tray;
    private readonly Dictionary<string, ShortcutRegistration> _shortcuts = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _globalShortcutIds = new();
    private readonly TaskbarProgress _taskbar = new();
    private int _nextHotKeyId = 100;
    private bool _trayExplicitVisible;
    private string? _activeNotificationId;

    public NativeShellGateway(Form form, Action<string, object> postEvent, SafeLogger logger)
    {
        _form = form;
        _postEvent = postEvent;
        _logger = logger;
        _tray = new NotifyIcon
        {
            Icon = form.Icon ?? SystemIcons.Application,
            Text = TrimTooltip(form.Text),
            Visible = false
        };
        _tray.Click += (_, _) => _postEvent("tray.click", new { });
        _tray.DoubleClick += (_, _) => _postEvent("tray.doubleClick", new { });
        _tray.BalloonTipClicked += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_activeNotificationId))
            {
                _postEvent("notification.click", new { id = _activeNotificationId });
            }
        };
        _tray.BalloonTipClosed += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_activeNotificationId))
            {
                _postEvent("notification.close", new { id = _activeNotificationId });
                _activeNotificationId = null;
            }

            if (!_trayExplicitVisible)
            {
                _tray.Visible = false;
            }
        };
    }

    public Task TrayShowAsync(NativeTrayRequest request, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(request.IconPath))
            {
                _tray.Icon = LoadIcon(request.IconPath);
            }

            if (!string.IsNullOrWhiteSpace(request.Tooltip))
            {
                _tray.Text = TrimTooltip(request.Tooltip);
            }

            if (request.Menu.Count > 0)
            {
                _tray.ContextMenuStrip = BuildContextMenu(request.Menu, "tray.menu");
            }

            _trayExplicitVisible = true;
            _tray.Visible = true;
        });
    }

    public Task TrayHideAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _trayExplicitVisible = false;
            _tray.Visible = false;
        });
    }

    public Task TraySetTooltipAsync(string tooltip, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _tray.Text = TrimTooltip(tooltip);
        });
    }

    public Task TraySetMenuAsync(IReadOnlyList<NativeMenuItem> items, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _tray.ContextMenuStrip = BuildContextMenu(items, "tray.menu");
        });
    }

    public Task SetWindowMenuAsync(IReadOnlyList<NativeMenuItem> items, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var menu = new MenuStrip
            {
                BackColor = Color.FromArgb(32, 32, 35),
                ForeColor = Color.FromArgb(230, 230, 230)
            };
            foreach (var item in items)
            {
                menu.Items.Add(BuildToolStripItem(item, "menu.click"));
            }

            var old = _form.MainMenuStrip;
            if (old is not null)
            {
                _form.Controls.Remove(old);
                old.Dispose();
            }

            _form.MainMenuStrip = menu;
            _form.Controls.Add(menu);
            menu.BringToFront();
        });
    }

    public Task ShowContextMenuAsync(IReadOnlyList<NativeMenuItem> items, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var menu = BuildContextMenu(items, "contextMenu.click");
            menu.Closed += (_, _) => menu.Dispose();
            menu.Show(_form, _form.PointToClient(Cursor.Position));
        });
    }

    public Task ShowNotificationAsync(NativeNotificationRequest request, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(request.IconPath))
            {
                _tray.Icon = LoadIcon(request.IconPath);
            }

            _activeNotificationId = request.Id;
            _tray.Visible = true;
            _tray.ShowBalloonTip(request.TimeoutMs, request.Title, request.Body, ToolTipIcon.Info);
        });
    }

    public Task RegisterShortcutAsync(NativeShortcutRequest request, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            UnregisterShortcut(request.Id);

            var parsed = ShortcutKey.Parse(request.Accelerator);
            var hotKeyId = 0;
            if (request.Global)
            {
                hotKeyId = Interlocked.Increment(ref _nextHotKeyId);
                var ok = RegisterHotKey(_form.Handle, hotKeyId, parsed.Win32Modifiers, (uint)parsed.Key);
                if (!ok)
                {
                    throw new InvalidOperationException("Shortcut is already registered by another app.");
                }

                _globalShortcutIds[hotKeyId] = request.Id;
            }

            _shortcuts[request.Id] = new ShortcutRegistration(request.Id, parsed, request.Global, hotKeyId);
        });
    }

    public Task UnregisterShortcutAsync(string id, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            UnregisterShortcut(id);
        });
    }

    public Task ClearShortcutsAsync(CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var id in _shortcuts.Keys.ToArray())
            {
                UnregisterShortcut(id);
            }
        });
    }

    public Task SetTaskbarProgressAsync(TaskbarProgressRequest request, CancellationToken cancellationToken)
    {
        return OnUiThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _taskbar.SetProgress(_form.Handle, request.State, request.Value, request.Maximum);
        });
    }

    public bool HandleHotKeyMessage(Message message)
    {
        if (message.Msg != WM_HOTKEY)
        {
            return false;
        }

        var hotKeyId = message.WParam.ToInt32();
        if (!_globalShortcutIds.TryGetValue(hotKeyId, out var id))
        {
            return false;
        }

        _postEvent("shortcuts.trigger", new { id, global = true });
        return true;
    }

    public bool HandleLocalKeyDown(KeyEventArgs args)
    {
        foreach (var registration in _shortcuts.Values)
        {
            if (registration.Global || !registration.Key.Matches(args))
            {
                continue;
            }

            args.Handled = true;
            args.SuppressKeyPress = true;
            _postEvent("shortcuts.trigger", new { id = registration.Id, global = false });
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        foreach (var id in _shortcuts.Keys.ToArray())
        {
            UnregisterShortcut(id);
        }

        _tray.Visible = false;
        _tray.Dispose();
    }

    private ContextMenuStrip BuildContextMenu(IReadOnlyList<NativeMenuItem> items, string eventName)
    {
        var menu = new ContextMenuStrip();
        foreach (var item in items)
        {
            menu.Items.Add(BuildToolStripItem(item, eventName));
        }

        return menu;
    }

    private ToolStripItem BuildToolStripItem(NativeMenuItem item, string eventName)
    {
        if (item.Type.Equals("separator", StringComparison.OrdinalIgnoreCase))
        {
            return new ToolStripSeparator();
        }

        var menuItem = new ToolStripMenuItem(item.Label)
        {
            Name = item.Id,
            Enabled = item.Enabled,
            Checked = item.Checked,
            ShortcutKeyDisplayString = item.Accelerator ?? ""
        };

        foreach (var child in item.Items)
        {
            menuItem.DropDownItems.Add(BuildToolStripItem(child, eventName));
        }

        if (item.Items.Count == 0)
        {
            menuItem.Click += (_, _) => _postEvent(eventName, new { id = item.Id });
        }

        return menuItem;
    }

    private void UnregisterShortcut(string id)
    {
        if (!_shortcuts.Remove(id, out var registration))
        {
            return;
        }

        if (registration.Global)
        {
            UnregisterHotKey(_form.Handle, registration.HotKeyId);
            _globalShortcutIds.Remove(registration.HotKeyId);
        }
    }

    private Icon LoadIcon(string path)
    {
        try
        {
            using var icon = new Icon(path);
            return (Icon)icon.Clone();
        }
        catch (Exception exception)
        {
            _logger.Warning($"Could not load native shell icon: {exception.Message}");
            return _form.Icon ?? SystemIcons.Application;
        }
    }

    private Task OnUiThreadAsync(Action action)
    {
        if (_form.IsDisposed)
        {
            return Task.CompletedTask;
        }

        if (!_form.InvokeRequired)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _form.BeginInvoke(() =>
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

    private static string TrimTooltip(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "Avila"
            : value.Length > 63 ? value[..63] : value;
    }

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private sealed record ShortcutRegistration(string Id, ShortcutKey Key, bool Global, int HotKeyId);

    private sealed record ShortcutKey(Keys Key, bool Control, bool Alt, bool Shift, bool Win)
    {
        public uint Win32Modifiers =>
            MOD_NOREPEAT
            | (Control ? MOD_CONTROL : 0)
            | (Alt ? MOD_ALT : 0)
            | (Shift ? MOD_SHIFT : 0)
            | (Win ? MOD_WIN : 0);

        public bool Matches(KeyEventArgs args)
        {
            return args.KeyCode == Key
                && args.Control == Control
                && args.Alt == Alt
                && args.Shift == Shift;
        }

        public static ShortcutKey Parse(string accelerator)
        {
            var parts = accelerator.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                throw new InvalidOperationException("Shortcut accelerator is empty.");
            }

            var control = false;
            var alt = false;
            var shift = false;
            var win = false;
            Keys? key = null;

            foreach (var part in parts)
            {
                switch (part.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        control = true;
                        break;
                    case "alt":
                    case "option":
                        alt = true;
                        break;
                    case "shift":
                        shift = true;
                        break;
                    case "win":
                    case "cmd":
                    case "meta":
                        win = true;
                        break;
                    default:
                        key = ParseKey(part);
                        break;
                }
            }

            if (key is null)
            {
                throw new InvalidOperationException("Shortcut accelerator must include a key.");
            }

            return new ShortcutKey(key.Value, control, alt, shift, win);
        }

        private static Keys ParseKey(string value)
        {
            if (value.Length == 1 && char.IsLetterOrDigit(value[0]))
            {
                var upper = char.ToUpperInvariant(value[0]).ToString();
                return Enum.Parse<Keys>(upper);
            }

            if (Enum.TryParse<Keys>(value, ignoreCase: true, out var key))
            {
                return key;
            }

            throw new InvalidOperationException($"Unsupported shortcut key: {value}");
        }
    }
}

internal sealed class TaskbarProgress
{
    private ITaskbarList3? _taskbar;

    public void SetProgress(IntPtr handle, string state, int value, int maximum)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _taskbar ??= (ITaskbarList3)(object)new CTaskbarList();
            _taskbar.HrInit();
            var flag = state.ToLowerInvariant() switch
            {
                "none" or "noprogress" or "no-progress" => TaskbarProgressFlag.NoProgress,
                "indeterminate" => TaskbarProgressFlag.Indeterminate,
                "paused" => TaskbarProgressFlag.Paused,
                "error" => TaskbarProgressFlag.Error,
                _ => TaskbarProgressFlag.Normal
            };

            _taskbar.SetProgressState(handle, flag);
            if (flag is TaskbarProgressFlag.Normal or TaskbarProgressFlag.Paused or TaskbarProgressFlag.Error)
            {
                _taskbar.SetProgressValue(handle, (ulong)Math.Clamp(value, 0, maximum), (ulong)Math.Max(1, maximum));
            }
        }
        catch (COMException)
        {
        }
        catch (InvalidCastException)
        {
        }
    }

    private enum TaskbarProgressFlag
    {
        NoProgress = 0,
        Indeterminate = 0x1,
        Normal = 0x2,
        Error = 0x4,
        Paused = 0x8
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class CTaskbarList
    {
    }

    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEA84")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();

        void AddTab(IntPtr hwnd);

        void DeleteTab(IntPtr hwnd);

        void ActivateTab(IntPtr hwnd);

        void SetActiveAlt(IntPtr hwnd);

        void MarkFullscreenWindow(IntPtr hwnd, bool fullscreen);

        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);

        void SetProgressState(IntPtr hwnd, TaskbarProgressFlag flags);
    }
}
