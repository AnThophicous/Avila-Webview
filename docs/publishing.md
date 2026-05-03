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

Avila uses a calendar-based release marker. The first official release is:

```txt
26.0 Release
```

That value is stored in `Versionate.txt` and is resolved by the CLI and
packaging pipeline.

## Recommended Flow

1. Update code and docs.
2. Run `dotnet build` and `dotnet test`.
3. Run `avila build` on the target project or template.
4. Run `avila package` to produce the clean EXE output.
5. Inspect the generated package and the local `buildclear/dist` snapshot.
6. Commit the source changes only.
7. Push to GitHub.

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
any installer or binary release. If a binary release is needed later, create it
from the clean package output and attach it as a GitHub Release artifact rather
than checking the build output into source control.
