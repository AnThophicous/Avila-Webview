# Avila

Avila is a Windows-first desktop engine for HTML, CSS, and JavaScript apps. It combines .NET 8, WebView2, a secure JS-native bridge, native window customization, a bounded worker pool, and an EXE packaging pipeline.

## Commands

- `avila create app <name>` creates an app project.
- `avila create <name> --url <site>` creates a `browser-app` project.
- `avila dev` runs an `AppView` project in WebView2.
- `avila build` validates `avila.json` and writes a build report.
- `avila package` publishes a production EXE and copies the app into `dist/app`.
- `avila benchmark` measures build/package time and output size.
- `avila version` prints the current engine release marker from `Versionate.txt`.
- `avila doctor` checks Windows x64, .NET SDK, WebView2 Runtime, and manifest issues.

Generated projects start without Avila branding in the window icon or process name; app identity stays configurable in `avila.json`. The engine release is tracked in `Versionate.txt` and mirrored into the local `buildclear/dist` snapshot on package.

For a detailed release and publish flow, see `docs/publishing.md`.

## Quick Start

```powershell
dotnet run --project src/Avila.CLI -- create app meu-app
dotnet run --project src/Avila.CLI -- create meu-site --url https://meusite.com
dotnet run --project src/Avila.CLI -- dev --project .\meu-app.avw --devtools
dotnet run --project src/Avila.CLI -- build --project .\meu-app.avw
dotnet run --project src/Avila.CLI -- package --project .\meu-app.avw
```

## Runtime Shape

```txt
HTML/CSS/JS app
  -> avila.js SDK
  -> structured WebView2 messages
  -> Avila.Bridge
  -> Avila.Security policy + session capability
  -> Avila.exe host / Avila.Core / Avila.Windowing / Avila.Workers
  -> WebView2 renderer, Win32, DWM, optional C++ core
```

See `docs/architecture.md`, `docs/security.md`, `docs/avila-json.md`, `docs/api-reference.md`, `docs/runtime-roadmap.md`, `docs/build.md`, `docs/clean-dist.md`, and `docs/versioning.md`.
