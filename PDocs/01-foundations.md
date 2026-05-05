# 01 - Foundations

## Why Avila Exists

Avila exists to solve a specific problem: shipping web frontends as serious Windows desktop applications without turning the app into a browser-shaped compromise.

The usual options each leave something behind:

- a plain website has no native packaging or desktop identity
- a browser wrapper often exposes too much and feels generic
- a loosely coupled desktop shell can become a security and memory mess

Avila takes the opposite approach:

- Windows is the target first
- WebView2 is the renderer, not the product
- the engine owns the runtime rules
- the app owns its own identity
- the frontend is a consumer, not the authority

## What Avila Is

Avila is a Windows desktop app engine for HTML, CSS, and JavaScript apps.

It provides:

- a CLI for creating, running, validating, packaging, and releasing apps
- a runtime host built on .NET 8 and WebView2
- a bridge between JavaScript and native features
- a manifest-driven security and permission model
- a packaging pipeline that can produce a normal EXE or a secure bundle

## What Avila Is Not

Avila is not:

- a general browser
- a loose Electron clone
- a Node.js sandbox with allowlists and runtime control
- a frontend framework
- a static site host

The engine does not exist to make the browser bigger.
It exists to make app shipping stricter, safer, and more predictable.

## Core Terms

### AppView

`AppView` is the local application mode.

- it loads local app assets
- it injects `window.avila` only into the local shell
- it is meant for real desktop apps
- it is the default mode for serious product work

### BrowserApp

`BrowserApp` is a separate mode for opening a remote URL inside an Avila window.

- it starts from `browser.url`
- it does not inject the bridge into remote pages by default
- it keeps APIs off unless explicitly enabled
- it is useful for wrapping a controlled website into a desktop experience

### Bridge

The bridge is the structured channel between JavaScript and the runtime.

It is message based, permission aware, origin aware, and capability aware.

### SDK

`avila.js` is the frontend SDK.

It is injected into the local app shell and is the only supported way for the frontend to ask Avila for native actions.

### NodeHost

NodeHost is controlled Node.js execution inside the Avila boundary.

It is not "Node everywhere".
It is "Node when Avila allows it".

### Secure Bundle

A secure bundle is a packaged app sealed with a manifest and signature so the runtime can detect tampering before startup.

## The Trusted Boundary

Avila draws a hard line between trusted and untrusted parts.

Trusted:

- runtime policy
- manifest validation
- capability generation
- command allowlists
- secure bundle verification

Untrusted:

- page HTML
- page JavaScript
- page CSS
- remote content
- user-provided frontend code
- disk state that is not verified

## Execution Chain

The basic chain looks like this:

```txt
1. CLI reads avila.json
2. Packager resolves build, permissions, and package mode
3. Runtime boots the host
4. WebView2 opens a local origin or browser-app origin
5. SDK injects the session capability
6. Frontend requests an action through the bridge
7. Runtime validates the request
8. Runtime executes only what the manifest allows
```

## Identity Model

Consumer apps should own their own:

- window title
- process name
- icon
- taskbar presence
- product name
- package name

Avila should stay neutral by default.
That is how the engine avoids branding every app that uses it.

## Performance Model

The performance philosophy is simple:

- keep the host thin
- keep Node controlled
- keep the renderer isolated
- keep the permission surface small
- avoid loading unnecessary browser features
- avoid exposing a full browser when only a page shell is needed

This is why bundle sealing, local origins, and strict policy resolution matter.
The engine is trying to protect both the runtime and the user machine.

## A Concrete Boot Example

1. The user opens the EXE.
2. The runtime reads the manifest or secure bundle.
3. The policy resolver computes the effective runtime policy.
4. The window is created with the app's own identity.
5. WebView2 is initialized.
6. The local shell is loaded.
7. `avila.js` is injected.
8. The bridge comes online.
9. The first page paint and navigation events are tracked.
10. The app enters normal runtime mode.

That sequence is what makes Avila feel like a desktop engine instead of a generic web wrapper.
