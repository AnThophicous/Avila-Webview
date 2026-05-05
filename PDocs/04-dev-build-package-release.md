# 04 - Dev, Build, Package, Release

## Daily Developer Loop

The recommended loop is simple:

```powershell
avila create app my-app
cd my-app
avila upcheck
avila upgrade
avila dev
avila dev --debug
avila build
avila package
```

For a website-style wrapper:

```powershell
avila create my-site --url https://example.com
avila dev
```

## CLI Surface

The CLI should feel like a real tool, not a dashboard.

The main commands are:

- `avila create`
- `avila dev`
- `avila build`
- `avila package`
- `avila verify`
- `avila check`
- `avila audit`
- `avila publish`
- `avila doctor`
- `avila inspect`
- `avila benchmark`
- `avila version`
- `avila upcheck`
- `avila upgrade`
- `avila publish --cert <pfx> --cert-password <secret>`

## Scaffolding

The scaffold should generate a project that is immediately usable.

The starter should define:

- app identity
- mode
- frontend adapter metadata
- browser URL if needed
- permissions defaults
- package defaults

The right scaffold for a production-minded project should not force Avila branding into the app identity.

## Dev Mode

Dev mode should optimize for iteration:

- WebView2 opens the local app shell
- the bridge is injected for local apps
- hot reload should react to file changes and preserve state when possible
- `avila dev --debug` should show lifecycle, bridge, and perf logs
- runtime errors and console errors should surface in the inspector console screen
- the app should not need a full rebuild for every edit
- output folders should be ignored to avoid reload loops
- sandbox and context isolation should stay enabled by default

The goal is to make the edit/save/test loop fast while keeping the production rules intact.

## Build Mode

Build mode should do validation first, not last.

Typical responsibilities:

- load the manifest
- validate schema and safe paths
- resolve policy
- write build reports
- prepare packaging metadata

Build should be cheap enough to run often and strict enough to catch mistakes early.

## Normal Package Mode

Normal package mode produces a standard distributable EXE and an exposed app folder.

Typical output:

```txt
dist/
  MyApp.exe
  MyApp.avwmeta.json
  Versionate.txt
  build-report.txt
  logs/
  app/
    avila.json
    src/
```

This mode is useful for development, internal testing, or relaxed distribution.

## Secure Package Mode

Secure package mode is the production option for serious apps.

It should:

- seal the frontend into `app.avila.bundle`
- generate a manifest
- hash every file
- sign the manifest or bundle metadata
- verify at boot
- block startup if tampering is detected
- avoid exposing `dist/app` as a casual editable folder

Typical output:

```txt
dist/
  MyApp.exe
  MyApp.avwmeta.json
  Versionate.txt
  build-report.txt
  logs/
  app.avila.bundle
  app.avila.bundle.manifest.json
  app.avila.bundle.sig
  app.avila.bundle.publickey.txt
  app.avila.bundle.sha256.txt
```

## Verify And Audit

`avila verify` should answer one question:

- is this bundle intact?

`avila audit` should answer a bigger question:

- is this package safe for production?

Production audit should check for:

- blocked files
- secret-like artifacts
- source map exposure
- bundle verification
- policy conflicts

## Buildclear

`buildclear` is the clean release snapshot on the build machine.

It exists so the newest packaged output is easy to validate, smoke test, and ship from without mixing it with source tree noise.

It should stay clean:

- no leftover source clutter
- no stray debug artifacts
- no accidental developer junk
- only the current release snapshot and its marker files

## Versioning

Avila uses calendar-based major versioning.

The version marker lives in `Versionate.txt`.

That file should stay the single source of truth for the release line.

## Release Flow

A good release flow looks like this:

1. finish code and docs
2. run build and test
3. package the app
4. verify the secure bundle if enabled
5. audit the package in production mode
6. publish the compiled release artifact
7. inspect the generated `buildclear` snapshot
8. tag and push the release

## SmartScreen And Signing

Secure bundle verification is not a substitute for proper code signing.

If the goal is to reduce SmartScreen friction for end users, the right answer is:

- sign the EXE
- keep release hygiene clean
- avoid unnecessary suspicious behavior
- ship a consistent package identity

Do not rely on "warning generation" as a security feature.
The secure approach is proper signing and integrity checks.

## Troubleshooting

Common issues to watch:

- WebView2 runtime missing
- file locks during publish
- stale `dist` leftovers
- bad manifest paths
- blocked permissions
- missing secure bundle sidecars
- source maps accidentally shipped
- output names that do not match the package identity
- window chrome settings that exceed safe blur or radius limits

## What A Strong Production Release Should Feel Like

A good Avila release should feel like this:

- the app opens quickly
- the window identity belongs to the app, not the engine
- the frontend loads from verified assets
- the bridge is strict but usable
- the build output is clean
- the package is reproducible
- the dev console tells the truth when the app crashes immediately
- tampering is detected at startup
- the developer knows exactly what is allowed and why
