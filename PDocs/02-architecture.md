# 02 - Architecture

## Layer Map

```txt
User app: HTML / CSS / JS
Avila.SDK: avila.js
Avila.Bridge: structured JS-native calls
Avila.Security: manifest, permissions, capabilities, policy graph
Avila.Host: WebView2 host, window lifecycle, secure bundle serving
Avila.Windowing: native window and DWM integration
Avila.Workers: bounded async background work
Avila.Diagnostics: safe logs and runtime metrics
Avila.Tooling / Avila.Packager: create, build, package, verify, audit, release
NodeHost: controlled Node.js execution
```

The engine is split this way so that one layer can evolve without collapsing the others into a monolith.

## Responsibilities By Layer

### Avila.Security

This layer owns the contract:

- manifest loading
- policy resolution
- permission defaults
- allowed origins
- path rules
- capability generation
- secure bundle verification

If something changes the trust model, it belongs here first.

### Avila.Bridge

The bridge owns the message protocol:

- command registry
- payload validation
- timestamp validation
- capability validation
- origin validation
- error mapping back to JS

The bridge is not a raw function escape hatch.
It is a controlled protocol.

### Avila.Runtime / Avila.Host

This layer owns process and UI lifecycle:

- window creation
- WebView2 initialization
- navigation policy
- SDK injection
- local origin handling
- secure bundle serving
- dev reload behavior

### Avila.Packager

This layer owns shipping output:

- template initialization
- build preparation
- app file copy or bundle sealing
- runtime publish
- report writing
- release snapshot mirroring into `buildclear`

### Avila.SDK

This layer is the frontend-facing API surface.

The SDK is intentionally narrow so the app asks for what it needs instead of receiving a huge native surface by default.

### NodeHost

NodeHost is the controlled Node runtime.

It exists for:

- build steps
- trusted scripts
- adapter helpers
- controlled runtime tasks

It does not exist to give the frontend unrestricted shell access.

## AppView Versus BrowserApp

### AppView

AppView is the normal desktop app shape.

- local assets are loaded from the app
- `window.avila` is available
- bridge commands are the primary native surface
- the app should feel like a native product with web rendering

### BrowserApp

BrowserApp is the controlled website wrapper shape.

- it starts from `browser.url`
- it keeps remote pages isolated
- it avoids injecting native APIs into remote content
- it can hand off external origins to the system browser

The two modes are related but deliberately not the same thing.

## Bridge Message Flow

The bridge message flow is:

```txt
JS -> avila.js -> WebView2 message -> BridgeHost -> CommandRegistry
   -> Security checks -> Runtime gateway / NodeHost / Windowing / Native services
   -> result or sanitized error -> JS
```

Every hop exists for a reason:

- `avila.js` gives a stable app API
- the WebView2 message channel carries data to the host
- the bridge validates shape and intent
- security resolves whether the call is allowed
- the runtime performs the action
- the result returns in a controlled format

## NodeHost Model

The NodeHost contract should always feel like this:

```js
await avila.node.run("build");
```

But the real rule is:

- the frontend asks
- Avila decides
- NodeHost executes only the allowed script

That keeps Node useful without letting it become an unbounded escape hatch.

## Framework Adapter Model

Framework adapters are a translation layer between frontend ecosystems and the Avila packaging model.

Their job is to answer:

- which dev command runs?
- which build command runs?
- where is the dist directory?
- what is the entry HTML?
- what files are safe to package?

That is enough to support ecosystems like Vite, React, Angular, Vue, Svelte, Solid, and Next.js-style frontends without changing the host model.

The runtime should not care about framework religion.
It should care about the final HTML/CSS/JS output and the manifest that describes how to serve it.

## Secure Bundle Serving

Secure bundle mode changes the storage shape:

- the app folder is not exposed as a casual editable surface
- the runtime verifies the bundle first
- verified files are served from a private extraction root
- the WebView2 origin behaves like a controlled local app origin

This is the architectural reason the bundle exists.
It is not just packaging.
It is a runtime trust boundary.

## Dev Reload

Development mode should be fast and obvious:

- file changes should reload the app
- output folders should not retrigger themselves
- the host should not need a browser restart for simple edits
- reload should stay local and predictable

The point is to shorten the edit-test cycle without weakening the production model.
