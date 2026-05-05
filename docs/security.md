# Security

Avila uses a zero-trust frontend model. Local HTML and JavaScript are treated as untrusted input because local apps can still load compromised assets, plugins, or remote content.

## Secure Bundle

`avila package --secure` seals the frontend into `app.avila.bundle` instead of exposing `dist/app` directly.

The runtime verifies:

- bundle manifest signature
- per-file SHA256 hashes
- bundle archive integrity

If verification fails, startup is blocked.

The secure bundle flow is for integrity and tamper detection, not for hiding secrets forever. The goal is to stop casual post-build edits, preserve distribution trust, and keep the runtime serving only verified assets.

Recommended commands:

```powershell
avila package --secure
avila verify .\dist\app.avila.bundle
avila audit --production
```

Runtime serving uses a verified local origin such as `https://app.avila.local`. The WebView2 host answers requests from the verified extraction root after bundle verification, instead of reading an open folder from disk.

## Bridge Controls

Every bridge request must contain:

- `id`
- `type: "avila.invoke"`
- `command`
- `payload`
- `timestamp`
- `capability`

The runtime validates:

- source origin
- command name schema
- payload byte limit
- timestamp window
- session capability
- manifest permission
- command timeout
- safe error response

## Deny by Default

`security.defaultPolicy` should stay `deny`. A command only runs when it is explicitly enabled in `permissions`.

Dangerous commands are disabled in the template:

- `os.exec`
- `network.fetch`
- `process.spawn`
- `process.execFile`
- `fs.readFile`
- `fs.writeFile`
- `clipboard.readText`
- `secrets.get`
- `secrets.set`

`os.exec` remains a guarded stub. Use `process.spawn` or `process.execFile` instead; they require a command allowlist, argument arrays, cwd roots, environment allowlists, timeout, and output limits. `network.fetch` is implemented with origin allowlists, redirect limits, response caps, blocked dangerous headers, cookies off, and LAN blocked by default.

`browser-app` is separate from `AppView`:

- remote navigation is controlled by `browser.url`, `browser.allowedOrigins`, and `browser.navigation`
- remote pages do not receive `window.avila`
- native APIs stay off by default
- external origins can fall back to the system browser

## Capabilities

The runtime creates a random session capability at startup. It is injected into `avila.js` inside a closure, never written to disk, and never logged.

## Logs

`Avila.Diagnostics.SafeLogger` redacts common secrets:

- passwords
- tokens
- cookies
- authorization headers
- API keys

Production bridge errors are sanitized and do not expose stack traces to JavaScript.

## File System

File APIs resolve paths through manifest-approved roots. Paths must be relative, cannot contain traversal segments, and must stay inside the selected root.
