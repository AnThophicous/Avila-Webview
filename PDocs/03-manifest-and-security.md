# 03 - Manifest and Security

## The Manifest Is the Contract

`avila.json` is not just config.
It is the contract between the developer, the packager, and the runtime.

If a behavior matters at boot, it should be expressible in the manifest.

## Manifest Areas

The manifest is split into separate areas so policies do not fight each other:

- `app`: identity and entry
- `mode`: `appview` or `browser-app`
- `window`: native window behavior
- `security`: remote content, devtools, payload limits, bridge timing, sandbox, context isolation
- `permissions`: command allow/deny map
- `fs`: root-based file access control
- `storage`: data store access
- `process`: child process control
- `network`: native network access
- `browser`: remote URL mode
- `frontend`: framework adapter metadata
- `node`: controlled Node execution
- `package`: secure bundle and distribution behavior
- `build`: publish and output settings

## Policy Graph

Avila should not silently let one setting cancel another.
That is what the policy graph is for.

The resolver should convert manifest text into an effective runtime policy and report:

- allow
- deny
- warning
- error
- suggestion

Example:

```json
{
  "security": {
    "allowRemoteContent": false
  },
  "network": {
    "allowedOrigins": ["https://api.example.com"]
  },
  "permissions": {
    "network.fetch": true
  }
}
```

This should not fail just because `allowRemoteContent` is false.
Native fetch and WebView navigation are different policies.

Window chrome belongs in the manifest too, but it should stay bounded:

- borderless behavior
- draggable regions
- optional Mica / backdrop hints

The public SDK should not expose border shaping controls. The runtime is
borderless by default, and any legacy chrome hints should be treated as
compatibility-only behavior, not as a primary design surface.

## Separate Policy Buckets

### Remote Content Policy

Controls whether the WebView may navigate to remote pages.

### Network Policy

Controls whether Avila may make native HTTP requests on behalf of the app.

### Bridge Permission Policy

Controls whether frontend code may ask for a feature through the bridge.

### File System Policy

Controls which named roots can be read or written.

### Process Policy

Controls what can be spawned, where, and under which limits.

## Security Invariants

Avila should keep these invariants true:

- default deny
- explicit allowlists
- no shell strings for process execution
- no silent path traversal
- no unbounded payloads
- no unrestricted remote bridge access
- no frontend access to native APIs unless explicitly allowed
- no secure bundle startup when integrity fails

## Bridge Request Shape

Every bridge message should carry enough data to be verified:

```json
{
  "id": "request-id",
  "type": "avila.invoke",
  "command": "window.setTitle",
  "payload": {
    "title": "My App"
  },
  "timestamp": "2026-05-03T13:00:00Z",
  "capability": "session-token"
}
```

The runtime should validate:

- message type
- command schema
- origin
- capability
- timestamp window
- payload size
- permission state

## File System Rules

The file system layer must be strict:

- paths are relative to approved roots
- `..` traversal is rejected
- absolute paths are rejected
- roots must exist or be explicit
- atomic writes should be preferred where possible

This is what keeps a frontend from wandering outside its sandbox.

## Process Rules

`process.spawn()` and `process.execFile()` should be treated as privileged operations.

The runtime should require:

- an allowlisted command
- argument arrays
- allowed cwd roots
- allowed environment variables
- output limits
- timeout limits

No shell strings.
No implicit power.

## Network Rules

Native network requests should be controlled separately from browser navigation.

The runtime should enforce:

- allowed origins
- redirect limits
- response size limits
- blocked dangerous headers
- cookie handling rules
- LAN restrictions unless explicitly enabled

## BrowserApp Security

BrowserApp is the mode for remote sites.

Its rules are different from AppView:

- `browser.url` is the entry point
- remote pages do not get `window.avila` by default
- native APIs stay off unless explicitly designed in
- external origins may open in the system browser
- cookies and cache should stay isolated per app

## Secure Bundle Security

Secure bundle mode is about integrity and tamper detection.

It should:

- compute per-file SHA256
- sign the manifest or bundle metadata
- verify the signature at startup
- refuse to boot if anything changed
- keep source maps out when requested

The goal is to stop casual post-build edits and protect the shipping artifact from trivial modification.

This is not the same as "hiding" source forever.
If the goal is secrecy, use proper product signing and build hygiene, not fake encryption claims.

## Logging and Secrets

Logs should avoid leaking:

- passwords
- cookies
- tokens
- API keys
- authorization headers

Production errors should stay sanitized.
The frontend should not receive raw stack traces for sensitive runtime failures.

## Policy Error Example

The resolver should be explicit when something conflicts.

Example:

```txt
AVILA-POLICY-001
network.fetch is enabled and https://api.example.com is allowlisted,
but remote navigation is still disabled.

Suggestion:
- keep remote navigation disabled if you only need native fetch
- or enable browser navigation if you truly need remote content
```

That style of feedback is better than silent blocking.
