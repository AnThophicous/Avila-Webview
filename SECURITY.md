# Security Policy

Avila is designed around explicit allowlists, sandboxed bridge calls, and
consumer-owned application identity.

## Supported Releases

- Current release line: `26.0.1 Startup | Release`
- Security fixes should land on the active release branch first.

## Reporting Issues

If you find a vulnerability, report it privately before opening a public issue.
Include:

- affected version
- reproduction steps
- exact manifest or bridge payload
- whether the issue is in AppView, BrowserApp, packaging, or the host runtime

## Security Expectations

- Remote content is off by default unless explicitly allowed.
- `window.avila` should not be injected into remote pages by default.
- Native APIs should stay behind manifest permissions.
- Bridge requests must carry a valid timestamp and capability.
- Package output must not include dev-only, secret-like, or debug files.

## What We Validate

- origin allowlists
- payload limits
- bridge capability checks
- file-system sandbox paths
- process allowlists
- package secret leak scans
- local build snapshot hygiene

## Disclosure

Please give a reasonable grace period for fixes before publishing exploit
details.
