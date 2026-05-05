(function installAvilaSdk(global) {
  "use strict";

  const bootstrap = global.__AVILA_BOOTSTRAP__ || {};
  try {
    delete global.__AVILA_BOOTSTRAP__;
  } catch (_) {
    global.__AVILA_BOOTSTRAP__ = undefined;
  }

  const capability = String(bootstrap.capability || "");
  const maxPayloadBytes = Number(bootstrap.maxPayloadBytes || 1048576);
  const defaultTimeoutMs = Number(bootstrap.bridgeTimeoutMs || 5000);
  const pending = new Map();
  const eventHandlers = new Map();
  const encoder = new TextEncoder();

  function createId() {
    if (global.crypto && typeof global.crypto.randomUUID === "function") {
      return global.crypto.randomUUID();
    }

    const bytes = new Uint8Array(16);
    global.crypto.getRandomValues(bytes);
    return Array.from(bytes, byte => byte.toString(16).padStart(2, "0")).join("");
  }

  function ensureBridge() {
    if (!global.chrome || !global.chrome.webview || typeof global.chrome.webview.postMessage !== "function") {
      throw new Error("Avila bridge is not available.");
    }
  }

  function byteLength(value) {
    return encoder.encode(JSON.stringify(value)).byteLength;
  }

  function normalizeResponse(data) {
    if (typeof data === "string") {
      return JSON.parse(data);
    }

    return data;
  }

  function invoke(command, payload = {}, options = {}) {
    ensureBridge();

    if (typeof command !== "string" || !/^[a-zA-Z0-9_.-]{1,96}$/.test(command)) {
      return Promise.reject(new Error("Invalid Avila command."));
    }

    const request = {
      id: createId(),
      type: "avila.invoke",
      command,
      payload: payload || {},
      timestamp: Date.now(),
      capability
    };

    if (byteLength(request) > maxPayloadBytes) {
      return Promise.reject(new Error("Avila payload is too large."));
    }

    const timeoutMs = Math.max(100, Number(options.timeoutMs || defaultTimeoutMs));

    return new Promise((resolve, reject) => {
      const timeout = global.setTimeout(() => {
        pending.delete(request.id);
        reject(new Error(`Avila command timed out: ${command}`));
      }, timeoutMs);

      pending.set(request.id, { resolve, reject, timeout, command });
      global.chrome.webview.postMessage(request);
    });
  }

  function handleMessage(event) {
    const response = normalizeResponse(event.data);
    if (response && response.type === "avila.event" && typeof response.event === "string") {
      emit(response.event, response.payload || {});
      return;
    }

    if (!response || typeof response.id !== "string") {
      return;
    }

    const item = pending.get(response.id);
    if (!item) {
      return;
    }

    pending.delete(response.id);
    global.clearTimeout(item.timeout);

    if (response.ok) {
      item.resolve(response.result);
      return;
    }

    const error = new Error(response.error && response.error.message ? response.error.message : "Avila command failed.");
    error.code = response.error && response.error.code ? response.error.code : "UNKNOWN";
    error.safe = response.error ? Boolean(response.error.safe) : true;
    item.reject(error);
  }

  function on(eventName, handler) {
    if (typeof eventName !== "string" || typeof handler !== "function") {
      throw new Error("Invalid Avila event subscription.");
    }

    const handlers = eventHandlers.get(eventName) || new Set();
    handlers.add(handler);
    eventHandlers.set(eventName, handlers);
    return () => off(eventName, handler);
  }

  function off(eventName, handler) {
    const handlers = eventHandlers.get(eventName);
    if (!handlers) {
      return;
    }

    handlers.delete(handler);
    if (handlers.size === 0) {
      eventHandlers.delete(eventName);
    }
  }

  function emit(eventName, payload) {
    const handlers = eventHandlers.get(eventName);
    if (!handlers) {
      return;
    }

    for (const handler of Array.from(handlers)) {
      queueMicrotask(() => handler(payload));
    }
  }

  function setWindowBoolean(command, enabled) {
    return invoke(command, { enabled: Boolean(enabled) });
  }

  function unwrapValue(result) {
    return Boolean(result && result.value);
  }

  function installDragRegions(api) {
    const dragRegions = Array.isArray(bootstrap.dragRegions) ? bootstrap.dragRegions : ["[data-avila-drag]"];
    const noDragRegions = Array.isArray(bootstrap.noDragRegions)
      ? bootstrap.noDragRegions
      : ["button", "input", "textarea", "select", "a", "[data-avila-no-drag]"];

    function closestAny(target, selectors) {
      if (!target || typeof target.closest !== "function") {
        return false;
      }

      return selectors.some(selector => {
        try {
          return Boolean(target.closest(selector));
        } catch (_) {
          return false;
        }
      });
    }

    global.document.addEventListener("mousedown", event => {
      if (event.button !== 0) {
        return;
      }

      if (closestAny(event.target, noDragRegions) || !closestAny(event.target, dragRegions)) {
        return;
      }

      api.invoke("window.beginDrag", {}, { timeoutMs: 1000 }).catch(() => {});
    });
  }

  const avila = Object.freeze({
    invoke,
    on,
    off,
    has: feature => invoke("app.has", { feature: String(feature) }).then(result => Boolean(result.available)),
    app: Object.freeze({
      info: () => invoke("app.info"),
      apiVersion: () => invoke("app.apiVersion").then(result => result.version),
      has: feature => invoke("app.has", { feature: String(feature) }).then(result => Boolean(result.available)),
      getArgs: () => invoke("app.getArgs").then(result => result.args || []),
      getPath: name => invoke("app.getPath", { name: String(name) }).then(result => result.path),
      diagnostics: () => invoke("app.diagnostics"),
      quit: () => invoke("app.quit"),
      restart: () => invoke("app.restart"),
      openExternal: url => invoke("app.openExternal", { url: String(url) }),
      revealPath: options => {
        if (typeof options === "string") {
          return invoke("app.revealPath", { path: options });
        }

        return invoke("app.revealPath", options || {});
      }
    }),
    system: Object.freeze({
      ping: () => invoke("system.ping"),
      info: () => invoke("system.info")
    }),
    window: Object.freeze({
      close: () => invoke("window.close"),
      show: () => invoke("window.show"),
      hide: () => invoke("window.hide"),
      focus: () => invoke("window.focus"),
      blur: () => invoke("window.blur"),
      minimize: () => invoke("window.minimize"),
      maximize: () => invoke("window.maximize"),
      restore: () => invoke("window.restore"),
      toggleMaximize: () => invoke("window.toggleMaximize"),
      isMaximized: () => invoke("window.isMaximized").then(unwrapValue),
      setFullscreen: enabled => setWindowBoolean("window.setFullscreen", enabled),
      isFullscreen: () => invoke("window.isFullscreen").then(unwrapValue),
      setAlwaysOnTop: enabled => setWindowBoolean("window.setAlwaysOnTop", enabled),
      setTitle: title => invoke("window.setTitle", { title: String(title) }),
      setIcon: path => invoke("window.setIcon", { path: String(path) }),
      setSize: (width, height) => invoke("window.setSize", { width: Number(width), height: Number(height) }),
      setMinSize: (width, height) => invoke("window.setMinSize", { width: Number(width), height: Number(height) }),
      setMaxSize: (width, height) => invoke("window.setMaxSize", { width: Number(width), height: Number(height) }),
      setPosition: (x, y) => invoke("window.setPosition", { x: Number(x), y: Number(y) }),
      center: () => invoke("window.center"),
      getBounds: () => invoke("window.getBounds"),
      setResizable: enabled => setWindowBoolean("window.setResizable", enabled),
      setDecorations: enabled => setWindowBoolean("window.setDecorations", enabled),
      setOpacity: opacity => invoke("window.setOpacity", { opacity: Number(opacity) }),
      setDraggable: enabled => setWindowBoolean("window.setDraggable", enabled),
      setMica: enabled => setWindowBoolean("window.setMica", enabled),
      on: (eventName, handler) => on(`window.${eventName}`, handler),
      off: (eventName, handler) => off(`window.${eventName}`, handler)
    }),
    dialog: Object.freeze({
      openFile: options => invoke("dialog.openFile", options || {}),
      saveFile: options => invoke("dialog.saveFile", options || {}),
      selectFolder: options => invoke("dialog.selectFolder", options || {}),
      message: options => invoke("dialog.message", typeof options === "string" ? { message: options } : (options || {})),
      confirm: options => invoke("dialog.confirm", typeof options === "string" ? { message: options } : (options || {}))
        .then(result => Boolean(result.confirmed))
    }),
    tray: Object.freeze({
      show: options => invoke("tray.show", options || {}),
      hide: () => invoke("tray.hide"),
      setTooltip: tooltip => invoke("tray.setTooltip", { tooltip: String(tooltip) }),
      setMenu: items => invoke("tray.setMenu", { items: Array.isArray(items) ? items : [] }),
      on: (eventName, handler) => on(`tray.${eventName}`, handler),
      off: (eventName, handler) => off(`tray.${eventName}`, handler)
    }),
    menu: Object.freeze({
      set: items => invoke("menu.set", { items: Array.isArray(items) ? items : [] }),
      on: (eventName, handler) => on(`menu.${eventName}`, handler),
      off: (eventName, handler) => off(`menu.${eventName}`, handler)
    }),
    contextMenu: Object.freeze({
      show: items => invoke("contextMenu.show", { items: Array.isArray(items) ? items : [] }),
      on: (eventName, handler) => on(`contextMenu.${eventName}`, handler),
      off: (eventName, handler) => off(`contextMenu.${eventName}`, handler)
    }),
    notifications: Object.freeze({
      show: options => invoke("notifications.show", options || {}),
      on: (eventName, handler) => on(`notification.${eventName}`, handler),
      off: (eventName, handler) => off(`notification.${eventName}`, handler)
    }),
    shortcuts: Object.freeze({
      register: options => invoke("shortcuts.register", options || {}),
      unregister: id => invoke("shortcuts.unregister", { id: String(id) }),
      clear: () => invoke("shortcuts.clear"),
      on: (eventName, handler) => on(`shortcuts.${eventName}`, handler),
      off: (eventName, handler) => off(`shortcuts.${eventName}`, handler)
    }),
    taskbar: Object.freeze({
      setProgress: options => invoke("taskbar.setProgress", options || {})
    }),
    browser: Object.freeze({
      navigate: url => invoke("browser.navigate", { url: String(url) }),
      back: () => invoke("browser.back"),
      forward: () => invoke("browser.forward"),
      reload: () => invoke("browser.reload"),
      stop: () => invoke("browser.stop"),
      canGoBack: () => invoke("browser.canGoBack").then(result => Boolean(result.value)),
      canGoForward: () => invoke("browser.canGoForward").then(result => Boolean(result.value)),
      setZoom: level => invoke("browser.setZoom", { level: Number(level) }),
      find: text => invoke("browser.find", { text: String(text) }),
      openDevTools: () => invoke("browser.openDevTools"),
      on: (eventName, handler) => on(`browser.${eventName}`, handler),
      off: (eventName, handler) => off(`browser.${eventName}`, handler)
    }),
    webview: Object.freeze({
      create: options => invoke("webview.create", options || {}),
      destroy: id => invoke("webview.destroy", { id: String(id) }),
      navigate: (id, url) => invoke("webview.navigate", { id: String(id), url: String(url) }),
      back: id => invoke("webview.back", { id: String(id) }),
      forward: id => invoke("webview.forward", { id: String(id) }),
      reload: id => invoke("webview.reload", { id: String(id) }),
      stop: id => invoke("webview.stop", { id: String(id) }),
      show: id => invoke("webview.show", { id: String(id) }),
      hide: id => invoke("webview.hide", { id: String(id) }),
      focus: id => invoke("webview.focus", { id: String(id) }),
      setBounds: (id, bounds) => invoke("webview.setBounds", { ...(bounds || {}), id: String(id) }),
      setZoom: (id, level) => invoke("webview.setZoom", { id: String(id), level: Number(level) }),
      find: (id, text) => invoke("webview.find", { id: String(id), text: String(text) })
        .then(result => Boolean(result.found)),
      executeScript: (id, script) => invoke("webview.executeScript", { id: String(id), script: String(script) })
        .then(result => result.result),
      injectCss: (id, css) => invoke("webview.injectCss", { id: String(id), css: String(css) }),
      screenshot: id => invoke("webview.screenshot", { id: String(id) }),
      openDevTools: id => invoke("webview.openDevTools", { id: String(id) }),
      list: () => invoke("webview.list"),
      on: (eventName, handler) => on(`webview.${eventName}`, handler),
      off: (eventName, handler) => off(`webview.${eventName}`, handler)
    }),
    tabs: Object.freeze({
      create: options => invoke("tabs.create", options || {}),
      close: id => invoke("tabs.close", { id: String(id) }),
      activate: id => invoke("tabs.activate", { id: String(id) }),
      active: () => invoke("tabs.active"),
      list: () => invoke("tabs.list"),
      duplicate: id => invoke("tabs.duplicate", { id: String(id) }),
      reopenClosed: () => invoke("tabs.reopenClosed"),
      on: (eventName, handler) => on(`webview.${eventName}`, handler),
      off: (eventName, handler) => off(`webview.${eventName}`, handler)
    }),
    clipboard: Object.freeze({
      readText: () => invoke("clipboard.readText").then(result => result.text || ""),
      writeText: text => invoke("clipboard.writeText", { text: String(text) }),
      readHtml: () => invoke("clipboard.readHtml").then(result => result.html || ""),
      writeHtml: html => invoke("clipboard.writeHtml", { html: String(html) }),
      clear: () => invoke("clipboard.clear")
    }),
    store: Object.freeze({
      get: (key, options = {}) => invoke("store.get", { ...options, key: String(key) }),
      set: (key, value, options = {}) => invoke("store.set", { ...options, key: String(key), value }),
      delete: (key, options = {}) => invoke("store.delete", { ...options, key: String(key) }),
      clear: (options = {}) => invoke("store.clear", options),
      export: (options = {}) => invoke("store.export", options).then(result => result.data || {}),
      import: (data, options = {}) => invoke("store.import", { ...options, data })
    }),
    secrets: Object.freeze({
      get: (key, options = {}) => invoke("secrets.get", { ...options, key: String(key) }).then(result => result.value || null),
      set: (key, value, options = {}) => invoke("secrets.set", { ...options, key: String(key), value: String(value) }),
      delete: (key, options = {}) => invoke("secrets.delete", { ...options, key: String(key) }),
      clear: (options = {}) => invoke("secrets.clear", options)
    }),
    process: Object.freeze({
      spawn: options => invoke("process.spawn", options || {}),
      execFile: options => invoke("process.execFile", options || {}),
      kill: id => invoke("process.kill", { id: String(id) })
    }),
    node: Object.freeze({
      run: (script, options) => invoke("node.run", {
        script: String(script),
        args: options && Array.isArray(options.args) ? options.args : []
      })
    }),
    network: Object.freeze({
      fetch: options => invoke("network.fetch", options || {})
    }),
    fs: Object.freeze({
      readText: options => invoke("fs.readText", typeof options === "string" ? { path: options } : (options || {}))
        .then(result => result.content || ""),
      writeText: (path, content, options = {}) => invoke("fs.writeText", { ...options, path: String(path), content: String(content) }),
      readBytes: options => invoke("fs.readBytes", typeof options === "string" ? { path: options } : (options || {})),
      writeBytes: (path, base64, options = {}) => invoke("fs.writeBytes", { ...options, path: String(path), base64: String(base64) }),
      exists: options => invoke("fs.exists", typeof options === "string" ? { path: options } : (options || {}))
        .then(result => Boolean(result.exists)),
      stat: options => invoke("fs.stat", typeof options === "string" ? { path: options } : (options || {})),
      listDir: options => invoke("fs.listDir", typeof options === "string" ? { path: options } : (options || {})),
      createDir: options => invoke("fs.createDir", typeof options === "string" ? { path: options } : (options || {})),
      removeFile: options => invoke("fs.removeFile", typeof options === "string" ? { path: options } : (options || {})),
      removeDir: options => invoke("fs.removeDir", typeof options === "string" ? { path: options } : (options || {})),
      copy: options => invoke("fs.copy", options || {}),
      move: options => invoke("fs.move", options || {}),
      rename: (path, name, options = {}) => invoke("fs.rename", { ...options, path: String(path), name: String(name) }),
      hash: options => invoke("fs.hash", typeof options === "string" ? { path: options } : (options || {})),
      readFile: path => invoke("fs.readFile", { path: String(path) }),
      writeFile: (path, content) => invoke("fs.writeFile", { path: String(path), content: String(content) })
    })
  });

  global.chrome.webview.addEventListener("message", handleMessage);
  Object.defineProperty(global, "avila", {
    value: avila,
    configurable: false,
    enumerable: false,
    writable: false
  });

  if (global.document && global.document.readyState === "loading") {
    global.document.addEventListener("DOMContentLoaded", () => installDragRegions(avila), { once: true });
  } else if (global.document) {
    installDragRegions(avila);
  }
})(globalThis);
