# Avila

Avila is a Windows-first desktop engine for HTML, CSS, and JavaScript apps. It combines .NET 8, WebView2, a secure JS-native bridge, native window customization, a bounded worker pool, and an EXE packaging pipeline.

## Commands

- `avila new` creates an app project.
- `avila create app <name>` creates an app project.
- `avila create <name> --url <site>` creates a `browser-app` project.
- `avila dev` runs an `AppView` project in WebView2.
- `avila build` validates `avila.json` and writes a build report.
- `avila package` publishes a production EXE and copies the app into `dist/app`.
- `avila package --secure` seals the app into a signed bundle and blocks tamper-at-startup.
- `avila verify` checks a secure bundle before distribution.
- `avila check` runs the main validation pass.
- `avila audit` runs the strict security audit.
- `avila publish` builds the engine release bundle.
- `avila benchmark` measures build/package time and output size.
- `avila version` prints the current engine release marker from `Versionate.txt`.
- `avila doctor` checks Windows x64, .NET SDK, WebView2 Runtime, and manifest issues.

Generated projects start without Avila branding in the window icon or process name; app identity stays configurable in `avila.json`. The engine release is tracked in `Versionate.txt` and mirrored into the local `buildclear/dist` snapshot on package.

For production apps, `--secure` is the recommended packaging mode. It keeps the frontend sealed inside `app.avila.bundle`, verifies the manifest signature on boot, and stops startup if the bundle changes.

For a detailed release and publish flow, see `docs/publishing.md`.

## Quick Start

```powershell
avila create app meu-app
cd meu-app
avila dev
avila build
avila package
```

Download the compiled release asset from GitHub Releases if you want the
engine and tooling ready to run without building the source tree yourself.

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
