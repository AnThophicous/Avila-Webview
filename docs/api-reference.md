# Avila API Reference

This document describes the public JavaScript API exposed by `avila.js`. The SDK is injected only into `avila://local` pages served by the Avila runtime virtual host. Remote pages do not receive `window.avila`.

## Invocation Model

```js
const result = await avila.invoke("app.info", {});
```

Every call becomes a structured native message:

```json
{
  "id": "uuid",
  "type": "avila.invoke",
  "command": "app.info",
  "payload": {},
  "timestamp": 1710000000000,
  "capability": "session-capability"
}
```

Runtime validation order:

1. Payload byte limit.
2. JSON schema.
3. Source origin.
4. Session capability.
5. Command registration.
6. Remote-content restrictions.
7. Manifest permission.
8. Command timeout.

## Events

```js
const unsubscribe = avila.browser.on("loaded", event => {
  console.log(event.url);
});

unsubscribe();
```

Low-level event subscription is also available:

```js
avila.on("browser.title", event => {
  document.title = event.title;
});
```

Native events use the format:

```json
{
  "type": "avila.event",
  "event": "browser.loaded",
  "payload": {}
}
```

## app

### `avila.app.info()`

Returns app identity and runtime metadata.

Required permission:

```json
"app.info": true
```

Example:

```js
const info = await avila.app.info();
```

### `avila.has(command)` / `avila.app.has(command)`

Returns `true` when a bridge command exists and is allowed by the current manifest.

```js
if (await avila.has("fs.readText")) {
  const text = await avila.fs.readText("src/index.html");
}
```

`avila.app.apiVersion()` returns the bridge API version. `avila.app.getArgs()` returns runtime arguments. `avila.app.getPath(name)` resolves one of `app`, `data`, `config`, `cache`, `downloads`, `logs`, or `temp`.

Runtime lifecycle commands:

- `avila.app.quit()`
- `avila.app.restart()`
- `avila.app.openExternal(url)` for `http`, `https`, and `mailto`
- `avila.app.revealPath({ root, path })`
- `avila.app.diagnostics()`

## system

### `avila.system.ping()`

Returns a safe liveness response.

Required permission:

```json
"system.ping": true
```

Example:

```js
const pong = await avila.system.ping();
```

### `avila.system.info()`

Returns OS, architecture, CPU count, working-set memory, uptime, hostname, locale, and timezone.

## window

### `avila.window.setTitle(title)`

Sets the native window title.

Required permission:

```json
"window.setTitle": true
```

### `avila.window.setSize(width, height)`

Sets native client size. Width and height are range-checked.

Required permission:

```json
"window.setSize": true
```

### `avila.window.center()`

Centers the window on the current screen.

Required permission:

```json
"window.center": true
```

### `avila.window.setDraggable(enabled)`

Enables or disables custom drag regions.

Required permission:

```json
"window.setDraggable": true
```

### `avila.window.setMica(enabled)`

Applies the native DWM Mica backdrop when supported. Windows 10 falls back cleanly.

Required permission:

```json
"window.setMica": true
```

Additional main-window commands now available:

- `close()`, `show()`, `hide()`, `focus()`, `blur()`
- `minimize()`, `maximize()`, `restore()`, `toggleMaximize()`
- `isMaximized()`, `setFullscreen(enabled)`, `isFullscreen()`
- `setAlwaysOnTop(enabled)`, `setIcon(path)`
- `setMinSize(width, height)`, `setMaxSize(width, height)`
- `setPosition(x, y)`, `getBounds()`
- `setResizable(enabled)`, `setDecorations(enabled)`, `setOpacity(value)`
- Border APIs are intentionally not part of the public SDK. The runtime is borderless by default and resizes through native hit testing.

Window events:

```js
avila.window.on("resize", event => console.log(event.width, event.height));
avila.window.on("closeRequested", event => console.log(event.reason));
```

## browser

The current browser API controls the main WebView2 instance. It is the first step toward a full browser shell. A future tab model should move remote pages into isolated child views while keeping the local app shell alive.

### `avila.browser.navigate(url)`

Navigates to an absolute `http` or `https` URL.

Security requirements:

- `security.allowRemoteContent` must be `true`.
- URL origin must match `security.allowedOrigins`.
- Remote pages do not receive `window.avila`.

Required permission:

```json
"browser.navigate": true
```

Example:

```js
await avila.browser.navigate("https://example.com");
```

### `avila.browser.back()`

Navigates back if WebView2 has history.

Required permission:

```json
"browser.back": true
```

### `avila.browser.forward()`

Navigates forward if WebView2 has history.

Required permission:

```json
"browser.forward": true
```

### `avila.browser.reload()`

Reloads the active page.

Required permission:

```json
"browser.reload": true
```

### `avila.browser.stop()`

Stops the active navigation.

Required permission:

```json
"browser.stop": true
```

### `avila.browser.canGoBack()`

Returns `true` when back navigation is possible.

Required permission:

```json
"browser.canGoBack": true
```

### `avila.browser.canGoForward()`

Returns `true` when forward navigation is possible.

Required permission:

```json
"browser.canGoForward": true
```

### `avila.browser.setZoom(level)`

Sets WebView2 zoom factor. Valid range: `0.25` to `5`.

Required permission:

```json
"browser.setZoom": true
```

### `avila.browser.find(text)`

Runs a basic in-page text search through the active document. The MVP uses `window.find`; a future version should use WebView2 native find APIs when available.

Required permission:

```json
"browser.find": true
```

### `avila.browser.openDevTools()`

Opens WebView2 DevTools only when `security.devtools` and the command permission are both enabled.

Required permission:

```json
"browser.openDevTools": true
```

## webview

`avila.webview` creates remote WebView2 surfaces owned by the native runtime. Remote WebViews do not receive `window.avila`; the local shell controls them through the bridge.

Security requirements:

- `security.allowRemoteContent` must be `true`.
- URL origin must match `security.allowedOrigins`.
- Each command must still be allowed in `permissions`.

```js
const view = await avila.webview.create({
  url: "https://example.com",
  bounds: { x: 220, y: 44, width: 980, height: 680 }
});

await avila.webview.navigate(view.id, "https://example.com/docs");
await avila.webview.setBounds(view.id, { x: 0, y: 44, width: 1200, height: 760 });
```

Commands:

- `create(options)`
- `destroy(id)`
- `navigate(id, url)`
- `back(id)`, `forward(id)`, `reload(id)`, `stop(id)`
- `show(id)`, `hide(id)`, `focus(id)`
- `setBounds(id, bounds)`
- `setZoom(id, level)`
- `find(id, text)`
- `executeScript(id, script)`
- `injectCss(id, css)`
- `screenshot(id)`
- `openDevTools(id)`
- `list()`

Events use `avila.webview.on(name, handler)` with names like `created`, `loading`, `loaded`, `title`, `url`, `error`, `crash`, and `active`.

## tabs

Tabs are a thin browser layer over remote WebViews.

```js
const tab = await avila.tabs.create({
  url: "https://example.com",
  bounds: { x: 0, y: 48, width: 1280, height: 720 }
});

await avila.tabs.activate(tab.id);
const tabs = await avila.tabs.list();
```

Commands:

- `create(options)`
- `close(id)`
- `activate(id)`
- `active()`
- `list()`
- `duplicate(id)`
- `reopenClosed()`

## dialog

### `avila.dialog.openFile(options)`

Opens a native file dialog.

```js
const result = await avila.dialog.openFile({
  title: "Select file",
  multiple: false,
  filter: "Text files (*.txt)|*.txt|All files (*.*)|*.*"
});
```

Required permission:

```json
"dialog.openFile": true
```

Additional dialog commands:

- `avila.dialog.saveFile(options)`
- `avila.dialog.selectFolder(options)`
- `avila.dialog.message({ title, message, kind })`
- `avila.dialog.confirm({ title, message, kind })`

## clipboard

### `avila.clipboard.writeText(text)`

Writes text to the system clipboard.

Required permission:

```json
"clipboard.writeText": true
```

Additional clipboard commands:

- `avila.clipboard.readText()`
- `avila.clipboard.readHtml()`
- `avila.clipboard.writeHtml(html)`
- `avila.clipboard.clear()`

## Native Surface

Native commands are permission-gated and also controlled by the `native` section in `avila.json`.

### Tray

```js
await avila.tray.show({
  tooltip: "Avila",
  icon: "",
  menu: [
    { id: "show", label: "Show" },
    { type: "separator" },
    { id: "quit", label: "Quit" }
  ]
});

avila.tray.on("menu", event => console.log(event.id));
```

Commands: `show`, `hide`, `setTooltip`, `setMenu`.

### Menus

`avila.menu.set(items)` creates a native window menu. `avila.contextMenu.show(items)` opens a native context menu at the cursor. Clicks emit `menu.click` or `contextMenu.click`.

### Notifications

```js
await avila.notifications.show({
  id: "build-done",
  title: "Build complete",
  body: "Package is ready."
});
```

Events: `notification.click`, `notification.close`.

### Shortcuts

```js
await avila.shortcuts.register({ id: "save", accelerator: "Ctrl+S" });
await avila.shortcuts.register({ id: "palette", accelerator: "Ctrl+Shift+P", global: true });

avila.shortcuts.on("trigger", event => console.log(event.id));
```

Commands: `register`, `unregister`, `clear`.

### Taskbar

```js
await avila.taskbar.setProgress({ state: "normal", value: 40, maximum: 100 });
await avila.taskbar.setProgress({ state: "none" });
```

## fs

File APIs are sandboxed to manifest-approved roots. The default root is `app`, which points to the `.avw` project folder. Configure additional roots with `fs.allowedRoots`.

```json
{
  "fs": {
    "allowedRoots": ["app", "data"],
    "maxReadBytes": 5242880,
    "maxWriteBytes": 5242880,
    "atomicWrites": true
  }
}
```

### `avila.fs.readFile(path)`

Reads a UTF-8 text file from inside the project sandbox.

Required permission:

```json
"fs.readFile": true
```

### `avila.fs.writeFile(path, content)`

Writes a UTF-8 text file inside the project sandbox.

Required permission:

```json
"fs.writeFile": true
```

`readFile` and `writeFile` are kept for compatibility. Prefer the clearer runtime API in new code:

- `readText(pathOrOptions)`
- `writeText(path, content, options)`
- `readBytes(pathOrOptions)`
- `writeBytes(path, base64, options)`
- `exists(pathOrOptions)`
- `stat(pathOrOptions)`
- `listDir(pathOrOptions)`
- `createDir(pathOrOptions)`
- `removeFile(pathOrOptions)`
- `removeDir(pathOrOptions)`
- `copy({ from, to, root, toRoot, overwrite })`
- `move({ from, to, root, toRoot, overwrite })`
- `rename(path, name, options)`
- `hash(pathOrOptions)`

Example using the app data root:

```js
await avila.fs.writeText("notes/today.txt", "Hello", { root: "data" });
const text = await avila.fs.readText({ root: "data", path: "notes/today.txt" });
```

## store

JSON stores live in `data`, `config`, or `cache`, depending on `storage.allowedStores`.

```js
await avila.store.set("theme", "dark", { namespace: "settings", store: "config" });
const result = await avila.store.get("theme", { namespace: "settings", store: "config" });
console.log(result.value);
```

Commands:

- `get(key, options)`
- `set(key, value, options)`
- `delete(key, options)`
- `clear(options)`
- `export(options)`
- `import(data, options)`

## secrets

Secrets use Windows DPAPI and require `storage.secrets: true`.

```js
await avila.secrets.set("token", "secret-value");
const token = await avila.secrets.get("token");
```

Commands: `get`, `set`, `delete`, `clear`.

## process

Processes are restricted by `process.allowedCommands`, `process.allowedCwdRoots`, `process.allowedEnv`, timeout, and output caps. Commands never run through shell strings.

```js
const result = await avila.process.execFile({
  command: "node",
  args: ["--version"],
  cwdRoot: "app"
});
```

Background processes return `{ id, pid }` and can be killed:

```js
const child = await avila.process.spawn({
  command: "node",
  args: ["server.js"],
  background: true
});

await avila.process.kill(child.id);
```

## network

`network.fetch()` requires `network.allowedOrigins`. Cookies are off, LAN/private IPs are blocked by default, redirects and response size are capped, and dangerous headers are rejected.

```js
const response = await avila.network.fetch({
  url: "https://api.example.com/status",
  method: "GET"
});

console.log(response.status, response.body);
```

## Error Shape

```js
try {
  await avila.invoke("fs.readFile", { path: "../secret.txt" });
} catch (error) {
  console.log(error.code);
  console.log(error.safe);
}
```

Native errors are returned as:

```json
{
  "id": "same-id",
  "ok": false,
  "error": {
    "code": "PERMISSION_DENIED",
    "message": "Command fs.readFile is not allowed.",
    "safe": true
  }
}
```
