# PDocs

PDocs is the deep technical manual for Avila.

It is written for developers who want the full mental model behind the engine:

- how the runtime grew from a simple WebView2 host into a layered desktop engine
- how the bridge, policy system, NodeHost, and bundle sealing work together
- how AppView and BrowserApp differ
- how the build, package, verify, audit, and release flow is supposed to behave
- how to ship web frontends as secure Windows executables without exposing the app folder

This folder is intentionally more technical than `docs/`.
Use it when you want the "why", the "how", and the "what happens at runtime" in one place.

## Reading Order

1. [01-foundations.md](./01-foundations.md)
2. [02-architecture.md](./02-architecture.md)
3. [03-manifest-and-security.md](./03-manifest-and-security.md)
4. [04-dev-build-package-release.md](./04-dev-build-package-release.md)

## Scope

This guide covers:

- Avila as a Windows desktop app engine for HTML, CSS, and JavaScript
- WebView2 hosting and local origin control
- SDK injection and bridge security
- manifest design and policy resolution
- NodeHost isolation
- secure bundle packaging and tamper detection
- day-to-day development, build, package, verify, and release usage

## Audience

- app developers building serious desktop software
- engineers maintaining Avila itself
- contributors adding templates, adapters, or runtime features
- anyone trying to understand the engine before shipping a product on top of it

## Mental Model

Think of the platform in this order:

```txt
Developer code
  -> avila.json
  -> avila.js SDK
  -> Avila.Bridge
  -> Avila.Security
  -> Avila.Runtime / Avila.Host
  -> WebView2 renderer
```

The frontend is never trusted by default.
The runtime is the authority.
The manifest is the contract.
The package is the shipping artifact.
