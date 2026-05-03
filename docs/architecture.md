# Architecture

Avila is split into small runtime layers so the bridge, permissions, windowing, packaging, diagnostics, and native core can evolve without becoming an Electron-style monolith.

## Layers

```txt
User app: HTML/CSS/JS
Avila.SDK: avila.js
Avila.Bridge: structured JS-native calls
Avila.Security: manifest, permissions, origins, capabilities
Avila.Host: WebView2 host and lifecycle
Avila.Windowing: WinForms, Win32, DWM APIs
Avila.Workers: bounded async worker pool
Avila.Diagnostics: safe logs and metrics
Avila.Tooling / Avila.Packager: .avw validation and EXE output
Avila.Native: optional C++ ABI modules
```

## Design Rules

- The frontend is never trusted.
- The bridge is message-based and validates origin, schema, capability, payload size, permission, timeout, and command name.
- Local app assets are served through a WebView2 virtual host and normalized as `avila://local`.
- WebView2 messaging is used instead of a production HTTP/WebSocket server.
- Heavy work runs through `Avila.Workers` or future native/process isolation.
- Window effects are best-effort and fall back cleanly on Windows versions that do not support them.

## MVP Boundaries

- The MVP is Windows-only.
- AppView uses the installed WebView2 Runtime.
- BrowserApp uses the same host, but starts from `browser.url` and keeps the bridge isolated from remote pages.
- Packaging currently copies the `.avw` app into `dist/app` next to the runtime EXE.
- Native AOT remains experimental because WebView2 and desktop UI stacks are not fully AOT-friendly.
