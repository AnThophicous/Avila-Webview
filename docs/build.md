# Build and Packaging

The packaging pipeline is intentionally simple and auditable in the MVP.

## Commands

```powershell
avila upcheck
avila upgrade --scope user
avila check --project .\my-app.avw
avila build --project .\my-app.avw
avila package --project .\my-app.avw
avila package --project .\my-app.avw --secure
avila verify .\dist\app.avila.bundle
avila audit --project .\my-app.avw
avila audit --project .\my-app.avw --production
avila benchmark --project .\my-app.avw --electron C:\Tools\Electron\electron.exe
avila publish
```

## Pipeline

1. Load `avila.json`.
2. Validate schema, safe paths, permissions, and insecure options.
3. Write `build/avila.build.json` and `build/avila.build.txt`.
4. Copy app files into `dist/app` for normal packages, or seal them into `app.avila.bundle` for secure packages.
5. Generate the bundle manifest, SHA256 file, signature, and embedded public key when `--secure` is enabled.
6. Run `dotnet publish` for `src/Avila.Runtime`.
7. Rename the published `Avila.exe` to the configured `build.outputName`.
8. Remove PDB/XML files from the package output.
9. Validate that no dev-only or secret-like files are present.
10. Write `<OutputName>.avwmeta.json`, `build-report.txt`, and `Versionate.txt`.
11. Mirror the final distributable into `buildclear/dist` when the repository root is available, and copy the release marker next to it.
12. Emit a startup marker during dev so `avila benchmark` can measure first paint and compare it to Electron when a local Electron path is supplied.
13. Use `avila publish` for the compiled engine bundle that users can download directly.

## Output

```txt
dist/
  MyApp.exe
  Versionate.txt
  MyApp.avwmeta.json
  app/
    avila.json
    src/
  logs/
  build-report.txt
```

Secure package layout:

```txt
dist/
  MyApp.exe
  Versionate.txt
  MyApp.avwmeta.json
  build-report.txt
  logs/
  app.avila.bundle
  app.avila.bundle.manifest.json
  app.avila.bundle.sig
  app.avila.bundle.publickey.txt
  app.avila.bundle.sha256.txt
```

## Native AOT

Native AOT is exposed as an option but remains experimental. WebView2, WinForms, COM interop, and reflection-based APIs may require additional trimming annotations or may not be compatible in all configurations.

## Technical Risks

- WebView2 Runtime may be missing on clean Windows installs.
- Advanced DWM effects vary by Windows version and GPU settings.
- Aggressive trimming can break desktop UI or COM interop.
- Remote content support needs stricter origin-specific API isolation.
- `os.exec` remains disabled; use `process.spawn` or `process.execFile`, which require allowlists, argument arrays, output limits, cwd roots, and timeouts.
- Secure bundles protect integrity and tamper detection, not secrecy. The bundle must still be signed with a real release certificate if the goal is to reduce SmartScreen friction for end users.

## Inspect

```powershell
dotnet run --project src/Avila.CLI -- inspect apis --project .\my-app.avw
dotnet run --project src/Avila.CLI -- inspect permissions --project .\my-app.avw
dotnet run --project src/Avila.CLI -- inspect package --project .\my-app.avw
```

`inspect apis` lists registered bridge commands with current allow/deny status. `inspect permissions` explains manifest permission state and validation issues. `inspect package` scans `dist` for package size and blocked files.
`check` is the fast validation pass. `audit` is the strict security pass. `audit --production` expects a secure bundle in `dist` and treats missing bundle verification as a production failure. `publish` is the compiled engine bundle flow.

`avila dev` now behaves like a real developer loop:

- hot reload reacts to frontend file changes and the backend runtime can restart or reload based on the project configuration
- dev errors are redirected to the inspector console screen instead of failing silently in the terminal
- console errors and frontend runtime errors are captured in a dedicated debug surface
- `sandbox`, `contextIsolation`, `AreHostObjectsAllowed = false`, and the permission allowlist keep the runtime tight by default
- `window` chrome options such as `roundedCorners`, `borderRadiusPx`, `blur`, and `blurAmount` come from the manifest and are clamped to safe values

The web frontend should remain the only thing changing rapidly; the engine itself stays controlled and predictable.

The `package` command also keeps `buildclear/dist` synced with the latest release when Avila is running from the source repository. That folder is the clean latest-release snapshot for launcher and smoke-test use.
`Versionate.txt` is the single release marker. For the current official patch release, the file contains `26.0.2 Release`.

## Roadmap

1. Add signed plugin manifests and native module trust policy.
2. Add production app resource embedding instead of folder copy.
3. Add per-permission object scopes in addition to command booleans.
4. Add CI package smoke tests and optional installer generation.
5. Add Linux/macOS backend abstractions.
