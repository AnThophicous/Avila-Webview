# Publishing and Release Flow

This repository is published as the source of the Avila engine and tooling.
The goal is to keep the public GitHub repository clean, auditable, and safe to
clone without exposing user-specific data or local build noise.

## What Gets Published

- Source code under `src/`
- CLI, runtime, bridge, SDK, tooling, and tests
- Templates and examples
- Documentation
- `Versionate.txt` as the release marker

## What Stays Local

- `bin/`
- `obj/`
- generated `dist/` output
- temporary build folders
- local `buildclear/dist` snapshots

The package pipeline still refreshes `buildclear/dist` locally so the newest
clean release is always available on the machine that is building Avila. That
folder is meant for validation, smoke testing, and launcher use. It is not
required in source control.

## Release Identity

Avila uses a calendar-based release marker. The current official patch release is:

```txt
26.0.2 Release
```

That value is stored in `Versionate.txt` and is resolved by the CLI and
packaging pipeline.

## Recommended Flow

1. Update code and docs.
2. Run `dotnet build` and `dotnet test`.
3. Run `avila upcheck` to verify the local engine marker.
4. Run `avila upgrade` when you want the latest compiled release installed locally.
5. Run `avila build` on the target project or template.
6. Run `avila package` to produce the clean EXE output for apps.
7. Publish the CLI once to a bootstrap folder, then run `avila publish` from that compiled executable.
8. Inspect the generated package, secure bundle, release bundle, and the local `buildclear/dist` snapshot.
9. Commit the source changes only.
10. Push the branch and tag to GitHub.

Example release bootstrap:

```powershell
dotnet publish .\src\Avila.CLI\Avila.CLI.csproj -c Release -r win-x64 -o .\buildclear\bootstrap-cli --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -p:PublishReadyToRun=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
.\buildclear\bootstrap-cli\avila.exe publish --output .\buildclear\release
```

## Safety Rules

- Do not commit secrets, credentials, private keys, or local machine data.
- Do not commit `bin/`, `obj/`, or generated package outputs.
- Keep app identity owned by the consumer project.
- Keep Avila branding out of generated apps by default.
- Prefer explicit allowlists for bridge, network, file system, and process APIs.

## Why This Structure

Avila is meant to feel like an engine, not a pre-branded app.

- The public repo should describe the platform clearly.
- Consumer apps should stay visually and operationally independent.
- Release artifacts should be reproducible from source.
- Local build snapshots should remain available without bloating the repo.

## If You Need a Shipping Build

Use the packaging pipeline locally and publish the source repo separately from
any installer or binary release. The GitHub Release should carry the compiled
bundle so developers can download Avila ready to run without building from
source. For source-tree release generation, use a compiled bootstrap CLI first
so the running process does not lock its own build output.

For production distribution, prefer the secure bundle path:

- `avila package --secure` seals frontend assets into an integrity-checked bundle.
- `avila verify` validates the bundle before shipment.
- `avila audit --production` checks that secure bundle enforcement is active.
- `avila dev` remains the fast edit loop, while `avila upgrade` is the user-facing installer path.
