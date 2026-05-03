# Build and Packaging

The packaging pipeline is intentionally simple and auditable in the MVP.

## Commands

```powershell
dotnet run --project src/Avila.CLI -- build --project .\my-app.avw
dotnet run --project src/Avila.CLI -- package --project .\my-app.avw
```

## Pipeline

1. Load `avila.json`.
2. Validate schema, safe paths, permissions, and insecure options.
3. Write `build/avila.build.json` and `build/avila.build.txt`.
4. Copy app files into `dist/app`.
5. Run `dotnet publish` for `src/Avila.Runtime`.
6. Rename the published `Avila.exe` to the configured `build.outputName`.
7. Remove PDB/XML files from the package output.
8. Validate that no dev-only or secret-like files are present.
9. Write `<OutputName>.avwmeta.json`, `build-report.txt`, and `Versionate.txt`.
10. Mirror the final distributable into `buildclear/dist` when the repository root is available, and copy the release marker next to it.

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

## Native AOT

Native AOT is exposed as an option but remains experimental. WebView2, WinForms, COM interop, and reflection-based APIs may require additional trimming annotations or may not be compatible in all configurations.

## Technical Risks

- WebView2 Runtime may be missing on clean Windows installs.
- Advanced DWM effects vary by Windows version and GPU settings.
- Aggressive trimming can break desktop UI or COM interop.
- Remote content support needs stricter origin-specific API isolation.
- `os.exec` remains disabled; use `process.spawn` or `process.execFile`, which require allowlists, argument arrays, output limits, cwd roots, and timeouts.

## Inspect

```powershell
dotnet run --project src/Avila.CLI -- inspect apis --project .\my-app.avw
dotnet run --project src/Avila.CLI -- inspect permissions --project .\my-app.avw
dotnet run --project src/Avila.CLI -- inspect package --project .\my-app.avw
```

`inspect apis` lists registered bridge commands with current allow/deny status. `inspect permissions` explains manifest permission state and validation issues. `inspect package` scans `dist` for package size and blocked files.

The `package` command also keeps `buildclear/dist` synced with the latest release when Avila is running from the source repository. That folder is the clean latest-release snapshot for launcher and smoke-test use.
`Versionate.txt` is the single release marker. For the first official release, the file contains `26.0 Release`.

## Roadmap

1. Add signed plugin manifests and native module trust policy.
2. Add production app resource embedding instead of folder copy.
3. Add per-permission object scopes in addition to command booleans.
4. Add CI package smoke tests and optional installer generation.
5. Add Linux/macOS backend abstractions.
