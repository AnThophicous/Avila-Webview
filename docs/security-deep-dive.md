# Security Deep Dive

Avila must treat local apps, browser-app pages, plugins, and remote content as different trust zones.

## Trust Zones

```txt
Zone 1: Avila runtime native code
  highest trust
  validates every request

Zone 2: avila://local app shell
  receives avila.js
  still treated as untrusted input

Zone 3: remote web content
  never receives window.avila
  cannot call native APIs

Zone 4: native plugins
  must be signed/trusted in future
  should run with explicit capabilities
```

## Bridge Threat Model

The bridge defends against:

- compromised local JavaScript
- malicious remote content
- payload flooding
- command spoofing
- stale/replayed messages
- unauthorized native calls
- path traversal
- unsafe error leaks

The bridge does not trust:

- `window.location`
- caller-provided command names
- caller-provided file paths
- frontend-side permission checks
- local HTML just because it ships with the app

## Session Capability

At startup the runtime generates a random session capability.

Rules:

- never store it on disk
- never log it
- never reuse it across launches
- compare using fixed-time comparison
- inject only into the local SDK closure

The capability is not a replacement for permissions. It proves the request came through the injected SDK for the current session, then permissions still decide whether the command runs.

## Remote Content Isolation

Remote pages must not get `window.avila`.

Current guard:

- SDK injection exits unless `location.hostname` is the internal Avila virtual host.
- Native bridge still validates source origin.
- Remote-origin bridge calls are denied unless a command explicitly allows remote usage.
- Remote WebViews are separate WebView2 controls and do not get the SDK injection.
- AppView remote navigation is blocked unless `security.allowRemoteContent` and `security.allowedOrigins` allow the target.
- BrowserApp remote navigation is controlled by `browser.url`, `browser.allowedOrigins`, and `browser.navigation`.

The trusted shell remains loaded while remote tabs navigate independently.

## Permission Scopes

The current manifest uses command-level booleans:

```json
{
  "permissions": {
    "dialog.openFile": true,
    "fs.readFile": false
  }
}
```

Next version should support scoped permissions:

```json
{
  "permissions": {
    "fs.readFile": {
      "allow": true,
      "roots": ["assets", "data"],
      "maxBytes": 1048576
    },
    "browser.navigate": {
      "allow": true,
      "origins": ["https://docs.example.com"]
    }
  }
}
```

## File-System Rules

Current rules:

- path must be relative
- no empty path segments
- no `..`
- final path must stay inside a manifest-approved root
- read/write size is limited by manifest
- writes are atomic by default
- simple MIME detection and SHA-256 hashing are available

Required future rules:

- separate read/write roots
- user-selected grants
- content hash verification for packaged assets
- richer file grants for user-selected external paths

## `network.fetch`

`network.fetch` is implemented with a deny-by-default network policy:

- require origin allowlist
- block protected headers by default
- strip cookies unless explicitly allowed
- set timeout
- limit response size
- restrict redirect count
- disable local-network access unless permitted
- redact URLs and headers in logs

## Process Execution

`os.exec` remains intentionally unavailable. `process.spawn` and `process.execFile` are the supported process APIs.

Current rules:

- no shell strings
- command allowlist only
- argument array only
- timeout required
- output byte limit
- working directory sandbox
- environment allowlist
- no inherited secrets by default

## Storage and Secrets

JSON stores are scoped to `data`, `config`, and `cache` roots. Secret values are protected with Windows DPAPI before being stored under app data. Namespace and key names are restricted to simple identifier characters to avoid path tricks.

## Logs and Diagnostics

Allowed:

- command names
- timing
- error categories
- worker queue metrics
- memory estimates

Not allowed:

- tokens
- cookies
- Authorization headers
- passwords
- API keys
- file contents
- private paths when path redaction is enabled
