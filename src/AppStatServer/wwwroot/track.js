/*
 * AppStatServer website snippet. Drop this on any site to count visits and clicks in the
 * same dashboard as the app's own analytics:
 *
 *   <script async src="https://YOUR-APPSTAT-HOST/track.js" data-release="website@1.0.0"></script>
 *
 * What it does:
 *   - assigns the visitor a stable anonymous id (localStorage) and a per-tab session id,
 *   - sends a `site_visit` event on load with the page path, external referrer and UTM tags,
 *   - sends a `site_click` event for any element carrying data-track="event_name",
 *   - exposes window.appstat.track(name, props) for custom events, and
 *     window.appstat.visitorId so a download flow can carry the id into the app if desired.
 *
 * Events land on POST /api/track of the host this script was loaded from (override with
 * data-endpoint). No cookies, no third parties, ~1 KB of logic.
 */
(function () {
  "use strict";

  var script = document.currentScript;
  if (!script) return; // loaded in a non-standard way (e.g. eval) — nothing to attach to

  var endpoint =
    (script.getAttribute("data-endpoint") || new URL(script.src, location.href).origin) + "/api/track";
  var release = script.getAttribute("data-release") || "website";

  function uuid() {
    if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
    return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, function (c) {
      var r = (Math.random() * 16) | 0;
      return (c === "x" ? r : (r & 0x3) | 0x8).toString(16);
    });
  }

  // Stable anonymous visitor id; falls back to a per-page id when storage is unavailable
  // (private mode with storage disabled, cookie-blocking policies).
  function persistentId(storage, key) {
    try {
      var id = storage.getItem(key);
      if (!id) {
        id = uuid();
        storage.setItem(key, id);
      }
      return id;
    } catch (e) {
      return uuid();
    }
  }

  var visitorId = persistentId(window.localStorage, "appstat_uid");
  var sessionId = persistentId(window.sessionStorage, "appstat_sid");

  function track(name, props) {
    if (!name) return;
    var body = JSON.stringify({
      userId: visitorId,
      sessionId: sessionId,
      release: release,
      os: navigator.userAgent,
      events: [{ name: String(name), timestamp: new Date().toISOString(), properties: props || {} }],
    });
    // keepalive lets the request survive the page unloading (e.g. a download-link click).
    try {
      fetch(endpoint, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: body,
        keepalive: true,
      });
    } catch (e) {
      /* never break the host page over analytics */
    }
  }

  // --- page view, with acquisition context ---
  var props = { path: location.pathname };
  try {
    if (document.referrer) {
      var ref = new URL(document.referrer);
      if (ref.host !== location.host) props.referrer = ref.host;
    }
    var qs = new URLSearchParams(location.search);
    ["utm_source", "utm_medium", "utm_campaign"].forEach(function (k) {
      if (qs.get(k)) props[k] = qs.get(k);
    });
  } catch (e) {
    /* URL/URLSearchParams unavailable — send the bare visit */
  }
  track("site_visit", props);

  // --- click tracking: <a data-track="download_click" href="…"> ---
  document.addEventListener(
    "click",
    function (e) {
      var el = e.target && e.target.closest && e.target.closest("[data-track]");
      if (!el) return;
      track("site_click", {
        target: el.getAttribute("data-track"),
        path: location.pathname,
        href: el.getAttribute("href") || "",
      });
    },
    true
  );

  window.appstat = { track: track, visitorId: visitorId };
})();
