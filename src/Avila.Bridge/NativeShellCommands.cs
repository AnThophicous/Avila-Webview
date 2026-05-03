using System.Text.Json;

namespace Avila.Bridge;

public sealed partial class BridgeCommandRegistry
{
    private void RegisterNativeShellCommands()
    {
        Register("tray.show", async (context, request, token) =>
        {
            EnsureNativeFeature(context, context.Project.Manifest.Native.Tray, "Tray is disabled by native.tray.");
            var iconPath = ResolveOptionalProjectPath(context, request.Payload, "icon");
            var tooltip = PayloadReader.GetString(request.Payload, "tooltip", 128, required: false);
            var menu = ReadMenuItems(request.Payload, "menu");
            await context.NativeShell.TrayShowAsync(new NativeTrayRequest(iconPath, tooltip, menu), token).ConfigureAwait(false);
            return new { shown = true };
        });

        Register("tray.hide", async (context, _, token) =>
        {
            EnsureNativeFeature(context, context.Project.Manifest.Native.Tray, "Tray is disabled by native.tray.");
            await context.NativeShell.TrayHideAsync(token).ConfigureAwait(false);
            return new { hidden = true };
        });

        Register("tray.setTooltip", async (context, request, token) =>
        {
            EnsureNativeFeature(context, context.Project.Manifest.Native.Tray, "Tray is disabled by native.tray.");
            var tooltip = PayloadReader.GetString(request.Payload, "tooltip", 128);
            await context.NativeShell.TraySetTooltipAsync(tooltip, token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("tray.setMenu", async (context, request, token) =>
        {
            EnsureNativeFeature(context, context.Project.Manifest.Native.Tray, "Tray is disabled by native.tray.");
            var menu = ReadMenuItems(request.Payload, "items");
            await context.NativeShell.TraySetMenuAsync(menu, token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("menu.set", async (context, request, token) =>
        {
            var items = ReadMenuItems(request.Payload, "items");
            await context.NativeShell.SetWindowMenuAsync(items, token).ConfigureAwait(false);
            return new { applied = true };
        });

        Register("contextMenu.show", async (context, request, token) =>
        {
            var items = ReadMenuItems(request.Payload, "items");
            await context.NativeShell.ShowContextMenuAsync(items, token).ConfigureAwait(false);
            return new { shown = true };
        });

        Register("notifications.show", async (context, request, token) =>
        {
            EnsureNativeFeature(context, context.Project.Manifest.Native.Notifications, "Notifications are disabled by native.notifications.");
            var id = PayloadReader.GetString(request.Payload, "id", 96, required: false);
            var title = PayloadReader.GetString(request.Payload, "title", 120);
            var body = PayloadReader.GetString(request.Payload, "body", 512, required: false);
            var iconPath = ResolveOptionalProjectPath(context, request.Payload, "icon");
            var timeoutMs = GetOptionalInt(request.Payload, "timeoutMs", 1000, 30000, 5000);
            await context.NativeShell.ShowNotificationAsync(
                new NativeNotificationRequest(string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id, title, body, iconPath, timeoutMs),
                token).ConfigureAwait(false);
            return new { shown = true };
        });

        Register("shortcuts.register", async (context, request, token) =>
        {
            EnsureNativeFeature(context, context.Project.Manifest.Native.Shortcuts, "Shortcuts are disabled by native.shortcuts.");
            var id = PayloadReader.GetString(request.Payload, "id", 96);
            var accelerator = PayloadReader.GetString(request.Payload, "accelerator", 64);
            var global = PayloadReader.GetOptionalBoolean(request.Payload, "global", false);
            if (global)
            {
                EnsureNativeFeature(context, context.Project.Manifest.Native.GlobalShortcuts, "Global shortcuts are disabled by native.globalShortcuts.");
            }

            await context.NativeShell.RegisterShortcutAsync(new NativeShortcutRequest(id, accelerator, global), token).ConfigureAwait(false);
            return new { registered = true };
        });

        Register("shortcuts.unregister", async (context, request, token) =>
        {
            var id = PayloadReader.GetString(request.Payload, "id", 96);
            await context.NativeShell.UnregisterShortcutAsync(id, token).ConfigureAwait(false);
            return new { unregistered = true };
        });

        Register("shortcuts.clear", async (context, _, token) =>
        {
            await context.NativeShell.ClearShortcutsAsync(token).ConfigureAwait(false);
            return new { cleared = true };
        });

        Register("taskbar.setProgress", async (context, request, token) =>
        {
            EnsureNativeFeature(context, context.Project.Manifest.Native.Taskbar, "Taskbar progress is disabled by native.taskbar.");
            var state = PayloadReader.GetString(request.Payload, "state", 24, required: false);
            var value = GetOptionalInt(request.Payload, "value", 0, 1_000_000, 0);
            var maximum = GetOptionalInt(request.Payload, "maximum", 1, 1_000_000, 100);
            await context.NativeShell.SetTaskbarProgressAsync(new TaskbarProgressRequest(state, value, maximum), token).ConfigureAwait(false);
            return new { applied = true };
        });
    }

    private static void EnsureNativeFeature(BridgeCommandContext context, bool enabled, string message)
    {
        if (!enabled && !context.Mode.Equals("dev", StringComparison.OrdinalIgnoreCase))
        {
            throw new BridgeException(BridgeErrorCodes.PermissionDenied, message);
        }
    }

    private static string? ResolveOptionalProjectPath(BridgeCommandContext context, JsonElement payload, string property)
    {
        var relative = PayloadReader.GetString(payload, property, 512, required: false);
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        return Avila.Security.SafePath.ResolveInside(context.Project.RootPath, relative);
    }

    private static IReadOnlyList<NativeMenuItem> ReadMenuItems(JsonElement payload, string property)
    {
        if (!payload.TryGetProperty(property, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{property} must be an array.");
        }

        return value.EnumerateArray().Take(64).Select(ReadMenuItem).ToArray();
    }

    private static NativeMenuItem ReadMenuItem(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Menu item must be an object.");
        }

        var type = ReadOptionalString(value, "type", 24, "normal");
        var id = ReadOptionalString(value, "id", 96, Guid.NewGuid().ToString("N"));
        var label = type.Equals("separator", StringComparison.OrdinalIgnoreCase)
            ? ""
            : ReadOptionalString(value, "label", 120, id);
        var enabled = ReadOptionalBoolean(value, "enabled", true);
        var @checked = ReadOptionalBoolean(value, "checked", false);
        var accelerator = ReadOptionalString(value, "accelerator", 64, "");
        var children = value.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Take(64).Select(ReadMenuItem).ToArray()
            : [];

        return new NativeMenuItem(id, label, type, enabled, @checked, string.IsNullOrWhiteSpace(accelerator) ? null : accelerator, children);
    }

    private static string ReadOptionalString(JsonElement payload, string name, int maxLength, string defaultValue)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return defaultValue;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name} must be a string.");
        }

        var text = value.GetString() ?? "";
        if (text.Length > maxLength)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name} is too long.");
        }

        return text;
    }

    private static bool ReadOptionalBoolean(JsonElement payload, string name, bool defaultValue)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return defaultValue;
        }

        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name} must be a boolean.");
        }

        return value.GetBoolean();
    }

    private static int GetOptionalInt(JsonElement payload, string name, int min, int max, int defaultValue)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return defaultValue;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name} must be an integer.");
        }

        if (number < min || number > max)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name} is out of range.");
        }

        return number;
    }
}
