# avila.json

`avila.json` defines app identity, window behavior, bridge security, permissions, performance policy, browser mode, and packaging options.

## Core Shape

```json
{
  "app": {
    "id": "com.example.app",
    "name": "Example App",
    "version": "1.0.0",
    "entry": "src/index.html",
    "icon": ""
  },
  "mode": "appview",
  "security": {
    "defaultPolicy": "deny",
    "allowRemoteContent": false,
    "allowedOrigins": ["avila://local"],
    "devtools": false,
    "sanitizeLogs": true,
    "maxPayloadBytes": 1048576,
    "bridgeTimeoutMs": 5000,
    "tokenSecurity": "session-capability"
  },
  "permissions": {
    "app.info": true,
    "system.ping": true,
    "window.setMica": true,
    "window.setRoundedCorners": true,
    "os.exec": false
  },
  "fs": {
    "allowedRoots": ["app"],
    "maxReadBytes": 5242880,
    "maxWriteBytes": 5242880,
    "atomicWrites": true
  },
  "native": {
    "tray": false,
    "notifications": false,
    "shortcuts": false,
    "globalShortcuts": false,
    "taskbar": true
  },
  "storage": {
    "allowedStores": ["data", "config", "cache"],
    "maxStoreBytes": 5242880,
    "secrets": false
  },
  "process": {
    "allowedCommands": [],
    "allowedCwdRoots": ["app", "data", "temp"],
    "allowedEnv": [],
    "timeoutMs": 10000,
    "maxOutputBytes": 1048576
  },
  "network": {
    "allowedOrigins": [],
    "timeoutMs": 10000,
    "redirectLimit": 5,
    "maxResponseBytes": 1048576,
    "allowLan": false
  },
  "browser": {
    "url": "",
    "allowedOrigins": [],
    "navigation": true,
    "externalOrigins": "open-system-browser",
    "downloads": true,
    "popups": true
  },
  "frontend": {
    "framework": "vanilla",
    "language": "js",
    "adapter": "static",
    "source": "src",
    "dist": "dist"
  },
  "node": {
    "enabled": false,
    "mode": "dev-only",
    "allowedScripts": ["dev", "build"]
  },
  "package": {
    "secureBundle": false,
    "signBundle": false,
    "verifyOnStartup": true,
    "serveFromBundle": false,
    "removeSourceMaps": true,
    "exposeAppFolder": true
  }
}
```

## Modes

`AppView` is the local app mode:

- `mode: "appview"`
- `app.entry` points to the local HTML entry
- `window.avila` is injected only into the local shell

`BrowserApp` opens a URL inside an Avila window:

- `mode: "browser-app"`
- `browser.url` is required
- `browser.allowedOrigins` controls in-app navigation
- remote pages do not receive `window.avila`
- native APIs stay off by default

Recommended BrowserApp example:

```json
{
  "mode": "browser-app",
  "app": {
    "id": "com.example.site",
    "name": "Example Site",
    "version": "1.0.0",
    "entry": "",
    "icon": ""
  },
  "browser": {
    "url": "https://meusite.com",
    "allowedOrigins": ["https://meusite.com"],
    "navigation": true,
    "externalOrigins": "open-system-browser",
    "downloads": true,
    "popups": false
  }
}
```

## Package

`package` controls how Avila distributes the app after build:

- `secureBundle`: seal the frontend into `app.avila.bundle`
- `signBundle`: write a signed manifest and public key sidecar
- `verifyOnStartup`: require bundle verification before loading the app
- `serveFromBundle`: serve assets from the verified bundle instead of an open folder
- `removeSourceMaps`: delete source maps from packaged output
- `exposeAppFolder`: keep `dist/app` visible for folder-mode distribution

Recommended production defaults:

- set `secureBundle` to `true` for customer releases
- set `signBundle` to `true` when you ship a signed release process
- keep `verifyOnStartup` on
- keep `serveFromBundle` on when using secure distribution
- keep `removeSourceMaps` on unless the build is a debug distribution
- set `exposeAppFolder` to `false` for secure bundle releases

## Window

The MVP supports initial size, minimum size, centering, borderless mode, rounded corners, Mica, custom draggable regions, and runtime calls such as `window.setMica` and `window.setRoundedCorners`.

## Permissions

Permissions are command names mapped to booleans. Missing commands are denied when `defaultPolicy` is `deny`.

Recommended production defaults:

- keep `allowRemoteContent` false
- keep `devtools` false
- keep `os.exec` false
- keep file permissions false unless the app needs them
- enable only the bridge commands the app calls
- set `app.icon` to your own brand before packaging
- set `build.outputName` to your own product name before packaging
- use `package.secureBundle` for customer-facing releases that should not expose the frontend as loose files

## File System

`fs.allowedRoots` controls which named roots file APIs can touch:

- `app`: the `.avw` project folder
- `data`: per-app data under LocalAppData
- `config`: per-app config under AppData
- `cache`: per-app cache under LocalAppData
- `downloads`: the user's Downloads folder
- `temp`: per-app temp folder

All paths passed to `avila.fs.*` must stay relative to one of those roots. `..`, absolute paths, and empty path segments are rejected. Text and byte writes use atomic replacement when `fs.atomicWrites` is true.

## Native Surface

`native` enables OS integration beyond the main window:

- `tray`: tray icon, tooltip, and tray menu
- `notifications`: native balloon notifications
- `shortcuts`: local shortcuts
- `globalShortcuts`: global Windows hotkeys
- `taskbar`: taskbar progress state/value

Dev mode may be less strict for native feature switches, but production apps should enable only the exact surfaces they need and still grant command permissions one by one.

## Storage

`storage.allowedStores` controls JSON stores under app data, config, and cache folders. `storage.secrets` enables DPAPI-protected secrets stored under app data. Store namespace and key names accept only letters, numbers, `_`, `-`, and `.`.

## Process

`process.allowedCommands` is an allowlist. `process.spawn()` and `process.execFile()` never use a shell string. Arguments are arrays, cwd is restricted to `process.allowedCwdRoots`, environment variables must be present in `process.allowedEnv`, and output is capped by `process.maxOutputBytes`.

## Network

`network.fetch()` is denied unless the destination origin is listed in `network.allowedOrigins`. Redirects are manually limited, response size is capped, cookies are off, dangerous headers are blocked, and LAN/private IP destinations are blocked unless `network.allowLan` is true.

## Build

`build` controls `dotnet publish` options:

- `target`: runtime identifier such as `win-x64`
- `singleFile`: publish single-file runtime
- `selfContained`: include .NET runtime
- `trim`: request trimming; the MVP WinForms/WebView2 host currently publishes with trimming disabled because Windows Forms is not trim-supported
- `readyToRun`: precompile for faster startup; off by default so package time stays practical
- `nativeAot`: experimental
- `outputName`: final EXE name
