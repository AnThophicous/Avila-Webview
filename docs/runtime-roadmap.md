# Runtime Roadmap

This roadmap keeps Avila focused on becoming a real desktop runtime first. The browser shell should sit on top of that runtime instead of becoming the center of the project.

## Stage 1 - Desktop Runtime Base

1. Core app/runtime APIs: `app.info`, lifecycle, app paths, API version, feature detection, diagnostics, external URL/path integration.
2. Window APIs: full main-window control, custom titlebar support, drag regions, resize/move/focus/blur/close events.
3. Safe local APIs: scoped FS roots, atomic text/byte writes, stat/list/copy/move/hash, native dialogs, and clipboard text/HTML.

## Stage 2 - Native OS Surface

1. Tray, native menus, context menus, notifications, local/global shortcuts, and taskbar integration.
2. Local storage, encrypted secrets, SQLite, migrations, backup/export/import, and profile-aware storage.
3. Safe process and network APIs with command/origin allowlists, timeouts, size limits, streaming, and LAN/proxy policy.

Implemented in the current runtime: tray/menu/context menu, notifications, shortcuts, taskbar progress, JSON stores, DPAPI secrets, store import/export, process spawn/exec/kill, and bounded network fetch. SQLite and richer profile workflows remain future work.

## Stage 3 - Browser Layer On Top

1. Separate remote WebView objects with navigation, screenshots, print/PDF, request policy, cookies/cache/downloads, crash recovery, and per-origin permissions.
2. Browser app APIs: tabs, tab strip, sessions, history, favorites, omnibox, profiles, private mode, site permissions, zoom/mute/pin/duplicate/reopen.
3. Developer and packaging finish: `avila inspect`, manifest schema/autocomplete, security/build reports, clean portable package validation, hashes, and no-dev-junk checks.

Implemented in the current runtime: separate remote WebView controls, `webview.*`, `tabs.*`, screenshot, controlled script/CSS hooks, popup-to-tab routing, crash events, `avila inspect apis`, `avila inspect permissions`, `avila inspect package`, and package cleanup/blocked-file validation. Print/PDF, cookies, downloads, profiles, history, favorites, omnibox, and private mode remain future browser-shell work.
