namespace Avila.Security;

public sealed class PermissionPolicy
{
    private static readonly HashSet<string> DangerousCommands = new(StringComparer.Ordinal)
    {
        "app.openExternal",
        "app.revealPath",
        "app.restart",
        "os.exec",
        "process.spawn",
        "process.execFile",
        "process.kill",
        "node.run",
        "network.fetch",
        "webview.create",
        "webview.destroy",
        "webview.navigate",
        "webview.executeScript",
        "webview.injectCss",
        "webview.screenshot",
        "webview.openDevTools",
        "tabs.create",
        "tabs.close",
        "tabs.duplicate",
        "tabs.reopenClosed",
        "fs.readFile",
        "fs.writeFile",
        "fs.readText",
        "fs.writeText",
        "fs.readBytes",
        "fs.writeBytes",
        "fs.exists",
        "fs.stat",
        "fs.listDir",
        "fs.createDir",
        "fs.removeFile",
        "fs.removeDir",
        "fs.copy",
        "fs.move",
        "fs.rename",
        "fs.hash",
        "clipboard.readText",
        "clipboard.readHtml",
        "secrets.get",
        "secrets.set",
        "secrets.delete",
        "secrets.clear",
        "notifications.show",
        "shortcuts.register",
        "shortcuts.unregister",
        "shortcuts.clear",
        "tray.show",
        "tray.hide",
        "tray.setTooltip",
        "tray.setMenu",
        "menu.set",
        "contextMenu.show",
        "taskbar.setProgress"
    };

    private readonly AvilaManifest _manifest;

    public PermissionPolicy(AvilaManifest manifest)
    {
        _manifest = manifest;
    }

    public bool IsAllowed(string command)
    {
        if (!IsValidCommandName(command))
        {
            return false;
        }

        if (_manifest.Permissions.TryGetValue(command, out var allowed))
        {
            return allowed;
        }

        return _manifest.Security.DefaultPolicy.Equals("allow", StringComparison.OrdinalIgnoreCase)
            && !DangerousCommands.Contains(command);
    }

    public static bool IsDangerous(string command) => DangerousCommands.Contains(command);

    public static IEnumerable<string> GetDangerousEnabledCommands(AvilaManifest manifest)
    {
        return manifest.Permissions
            .Where(pair => pair.Value && DangerousCommands.Contains(pair.Key))
            .Select(pair => pair.Key);
    }

    public static bool IsValidCommandName(string command)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Length > 96)
        {
            return false;
        }

        foreach (var character in command)
        {
            if (character is '.' or '-' or '_' || char.IsAsciiLetterOrDigit(character))
            {
                continue;
            }

            return false;
        }

        return true;
    }
}
