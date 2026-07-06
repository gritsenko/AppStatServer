"use strict";

const view = document.getElementById("view");
const userEl = document.getElementById("user");
const modalBackdrop = document.getElementById("modal-backdrop");
const modalTitle = document.getElementById("modal-title");
const modalBody = document.getElementById("modal-body");

const cache = {};
let analyticsDays = 30;
let eventsDays = 30;

// ---------- data ----------
async function fetchJson(url) {
  const res = await fetch(url, { headers: { Accept: "application/json" } });
  if (res.status === 401) {
    window.location.href = "/login.html";
    throw new Error("unauthorized");
  }
  if (!res.ok) throw new Error("HTTP " + res.status);
  return res.json();
}

// ---------- routing ----------
const views = {
  overview: renderOverview,
  analytics: renderAnalytics,
  events: renderEvents,
  logs: renderLogs,
  crashes: renderCrashes,
};

function currentRoute() {
  const r = location.hash.replace(/^#\/?/, "") || "overview";
  return views[r] ? r : "overview";
}

function setActiveNav(route) {
  document.querySelectorAll("#nav a").forEach((a) => {
    a.classList.toggle("active", a.dataset.route === route);
  });
}

async function route() {
  const r = currentRoute();
  setActiveNav(r);
  try {
    await views[r]();
  } catch (e) {
    if (e.message !== "unauthorized") view.innerHTML = '<div class="empty">Failed to load data.</div>';
  }
}

// ---------- Overview ----------
async function renderOverview() {
  if (!cache.overview) {
    const [stats, events, sessions, dsn] = await Promise.all([
      fetchJson("/api/stats"),
      fetchJson("/api/events"),
      fetchJson("/api/sessions"),
      fetchJson("/api/dsn"),
    ]);
    cache.overview = {
      stats,
      dsn: dsn.dsn,
      events: events.slice().sort((a, b) => new Date(b.timestamp) - new Date(a.timestamp)),
      sessions: sessions.slice().sort((a, b) => new Date(b.started) - new Date(a.started)),
    };
  }
  const { stats } = cache.overview;

  view.innerHTML = `
    <section class="dsn-panel">
      <div class="dsn-info">
        <span class="dsn-label">Sentry DSN</span>
        <p class="dsn-hint">Point your app's Sentry SDK at this DSN to start sending events.</p>
      </div>
      <div class="dsn-row">
        <code class="dsn-value" id="dsn-value">${escapeHtml(cache.overview.dsn)}</code>
        <button id="dsn-copy" class="primary" type="button">Copy</button>
      </div>
    </section>
    <section class="cards">
      ${statTile("Events", stats.totalEvents)}
      ${statTile("Errors", stats.errors, "errors")}
      ${statTile("Crashes", stats.crashes, "crashes")}
      ${statTile("Sessions", stats.totalSessions, "sessions")}
    </section>
    <section class="panels">
      <div class="panel"><h2>Events per day (last 14 days)</h2><div id="ov-chart"></div></div>
      <div class="panel"><h2>By level</h2><div class="breakdown" id="ov-levels"></div></div>
    </section>
    <div class="tabs">
      <button class="tab active" data-tab="events">Recent events</button>
      <button class="tab" data-tab="sessions">Sessions</button>
    </div>
    <div id="ov-tab-events">
      <div class="toolbar">
        <input class="search" id="ov-search" type="search" placeholder="Search message, release, user, OS…" />
        <select id="ov-level"><option value="">All levels</option></select>
      </div>
      <div class="table-wrap" id="ov-events"></div>
    </div>
    <div id="ov-tab-sessions" hidden><div class="table-wrap" id="ov-sessions"></div></div>`;

  renderBarChart(
    document.getElementById("ov-chart"),
    (stats.eventsPerDay || []).map((d) => ({ label: d.date.slice(5), value: d.count }))
  );
  renderHBars(document.getElementById("ov-levels"), stats.eventsByLevel || [], { colorByLevel: true });

  const levelSel = document.getElementById("ov-level");
  (stats.eventsByLevel || []).forEach((l) => {
    if (l.key === "-") return;
    const o = document.createElement("option");
    o.value = l.key;
    o.textContent = `${l.key} (${l.count})`;
    levelSel.appendChild(o);
  });

  const search = document.getElementById("ov-search");
  search.addEventListener("input", renderOverviewEvents);
  levelSel.addEventListener("change", renderOverviewEvents);
  renderOverviewEvents();
  renderSessionsTable(document.getElementById("ov-sessions"), cache.overview.sessions);

  document.querySelectorAll("#view .tab").forEach((tab) => {
    tab.addEventListener("click", () => {
      document.querySelectorAll("#view .tab").forEach((t) => t.classList.toggle("active", t === tab));
      document.getElementById("ov-tab-events").hidden = tab.dataset.tab !== "events";
      document.getElementById("ov-tab-sessions").hidden = tab.dataset.tab !== "sessions";
    });
  });

  const copyBtn = document.getElementById("dsn-copy");
  copyBtn.addEventListener("click", async () => {
    try {
      await navigator.clipboard.writeText(cache.overview.dsn);
      copyBtn.textContent = "✓ Copied";
      copyBtn.classList.add("copied");
      setTimeout(() => {
        copyBtn.textContent = "Copy";
        copyBtn.classList.remove("copied");
      }, 1500);
    } catch {
      // Clipboard API needs a secure context (https/localhost); fall back to selecting the text.
      const range = document.createRange();
      range.selectNodeContents(document.getElementById("dsn-value"));
      const sel = window.getSelection();
      sel.removeAllRanges();
      sel.addRange(range);
    }
  });
}

function renderOverviewEvents() {
  const q = document.getElementById("ov-search").value.trim().toLowerCase();
  const lvl = document.getElementById("ov-level").value;
  const rows = cache.overview.events.filter((e) => {
    if (lvl && (e.level || "") !== lvl) return false;
    if (!q) return true;
    return [e.message, e.release, e.userId, e.os, e.id].some((v) => String(v ?? "").toLowerCase().includes(q));
  });
  const host = document.getElementById("ov-events");
  if (!rows.length) {
    host.innerHTML = '<div class="empty">No matching events.</div>';
    return;
  }
  host.innerHTML =
    `<table><thead><tr><th>Time</th><th>Level</th><th>Message</th><th>Release</th><th>OS</th><th>User</th></tr></thead><tbody>` +
    rows
      .map((e) => {
        const tags =
          (e.isCrash ? '<span class="tag crash">crash</span>' : "") +
          (e.isError && !e.isCrash ? '<span class="tag error">error</span>' : "");
        return `<tr class="clickable" data-id="${escapeHtml(e.id)}">
          <td class="time">${fmtTime(e.timestamp)}</td>
          <td><span class="badge ${levelClass(e.level)}">${escapeHtml(e.level || "-")}</span></td>
          <td class="msg"><div class="text">${escapeHtml(e.message)}${tags}</div></td>
          <td>${escapeHtml(e.release || "-")}</td>
          <td>${escapeHtml(e.os || "-")}</td>
          <td class="mono">${escapeHtml(shortId(e.userId))}</td></tr>`;
      })
      .join("") +
    `</tbody></table>`;
  host.querySelectorAll("tr.clickable").forEach((tr) => {
    tr.addEventListener("click", () => {
      const ev = cache.overview.events.find((x) => x.id === tr.dataset.id);
      if (ev) openEventModal(ev);
    });
  });
}

function renderSessionsTable(host, sessions) {
  if (!sessions.length) {
    host.innerHTML = '<div class="empty">No sessions yet.</div>';
    return;
  }
  host.innerHTML =
    `<table><thead><tr><th>Session</th><th>Device</th><th>Started</th><th>Duration</th><th>Errors</th><th>Release</th><th>Env</th></tr></thead><tbody>` +
    sessions
      .map(
        (s) => `<tr>
        <td class="mono">${escapeHtml(shortId(s.id))}</td>
        <td class="mono">${escapeHtml(shortId(s.deviceId))}</td>
        <td class="time">${fmtTime(s.started)}</td>
        <td class="num">${fmtDuration(s.duration)}</td>
        <td class="num">${escapeHtml(String(s.errors ?? 0))}</td>
        <td>${escapeHtml(s.release || "-")}</td>
        <td>${escapeHtml(s.environment || "-")}</td></tr>`
      )
      .join("") +
    `</tbody></table>`;
}

// ---------- Analytics ----------
async function renderAnalytics() {
  if (!cache.analytics || cache.analytics.days !== analyticsDays) {
    cache.analytics = { days: analyticsDays, data: await fetchJson("/api/analytics?days=" + analyticsDays) };
  }
  const a = cache.analytics.data;
  const series = [
    { name: "Active users", color: "var(--series-1)", values: a.usersPerDay.map((d) => d.active) },
    { name: "New users", color: "var(--series-2)", values: a.usersPerDay.map((d) => d.newUsers) },
  ];
  const labels = a.usersPerDay.map((d) => d.date);

  view.innerHTML = `
    <div class="toolbar">
      <div class="range">
        ${[7, 14, 30, 90].map((d) => `<button class="range-btn ${d === analyticsDays ? "active" : ""}" data-days="${d}">${d}d</button>`).join("")}
      </div>
    </div>
    <section class="cards tiles">
      ${statTile("Monthly active", a.mau)}
      ${statTile("Weekly active", a.wau)}
      ${statTile("Daily active", a.dau)}
      ${statTile("New users", a.newUsers, "sessions")}
      ${statTile("Total sessions", a.totalSessions)}
      ${statTile("Avg. session", fmtDuration(a.avgSessionSeconds))}
      ${statTile("Sessions / user", a.sessionsPerUser.toFixed(2))}
    </section>
    <section class="panel wide">
      <h2>Active &amp; new users (last ${a.days} days)</h2>
      ${legendHtml(series)}
      <div id="an-users"></div>
    </section>
    <section class="panels">
      <div class="panel"><h2>Sessions per day</h2><div id="an-sessions"></div></div>
      <div class="panel"><h2>Session duration</h2><div id="an-duration"></div></div>
    </section>
    <section class="panels">
      <div class="panel"><h2>Active users by app version</h2><div class="breakdown" id="an-versions"></div></div>
      <div class="panel"><h2>OS distribution</h2><div class="breakdown" id="an-os"></div></div>
    </section>
    <section class="panel wide"><h2>Top devices</h2><div class="breakdown" id="an-devices"></div></section>`;

  renderLineChart(document.getElementById("an-users"), labels, series);
  renderBarChart(
    document.getElementById("an-sessions"),
    a.sessionsPerDay.map((d) => ({ label: d.date.slice(5), value: d.count }))
  );
  renderBarChart(
    document.getElementById("an-duration"),
    a.durationBuckets.map((b) => ({ label: b.key, value: b.count }))
  );
  renderHBars(document.getElementById("an-versions"), a.versionDistribution);
  renderHBars(document.getElementById("an-os"), a.osDistribution);
  renderHBars(document.getElementById("an-devices"), a.deviceDistribution);

  document.querySelectorAll(".range-btn").forEach((b) => {
    b.addEventListener("click", () => {
      analyticsDays = Number(b.dataset.days);
      renderAnalytics();
    });
  });
}

// ---------- Events / Crashes (grouped, server-filtered by version/OS) ----------
const eventsFilter = { release: "", os: "" };
const crashesFilter = { release: "", os: "" };

// Custom product events (sent to /api/track), aggregated into a report.
async function renderEvents() {
  if (!cache.eventsReport || cache.eventsReport.days !== eventsDays) {
    cache.eventsReport = { days: eventsDays, data: await fetchJson("/api/events-report?days=" + eventsDays) };
  }
  const r = cache.eventsReport.data;

  view.innerHTML = `
    <div class="page-head">
      <h1>Events</h1>
      <span class="page-sub">Custom product events · last ${r.days} days</span>
    </div>
    <div class="toolbar">
      <div class="range">
        ${[7, 14, 30, 90].map((d) => `<button class="range-btn ${d === eventsDays ? "active" : ""}" data-days="${d}">${d}d</button>`).join("")}
      </div>
    </div>
    <section class="cards tiles">
      ${statTile("Total events", r.totalEvents)}
      ${statTile("Event types", r.distinctNames)}
      ${statTile("Users", r.users, "sessions")}
    </section>
    <section class="panel wide"><h2>Events per day</h2><div id="ev-chart"></div></section>
    <div class="toolbar"><input class="search" id="ev-search" type="search" placeholder="Search event name…" /></div>
    <div class="table-wrap" id="ev-table"></div>`;

  renderBarChart(
    document.getElementById("ev-chart"),
    (r.eventsPerDay || []).map((d) => ({ label: d.date.slice(5), value: d.count }))
  );

  document.getElementById("ev-search").addEventListener("input", drawEventsTable);
  drawEventsTable();

  document.querySelectorAll(".range-btn").forEach((b) => {
    b.addEventListener("click", () => {
      eventsDays = Number(b.dataset.days);
      renderEvents();
    });
  });
}

function drawEventsTable() {
  const r = cache.eventsReport.data;
  const q = document.getElementById("ev-search").value.trim().toLowerCase();
  const rows = (r.events || []).filter((e) => !q || e.name.toLowerCase().includes(q));
  const host = document.getElementById("ev-table");
  if (!rows.length) {
    host.innerHTML = r.totalEvents
      ? '<div class="empty">No matching events.</div>'
      : '<div class="empty">No custom events yet. Send them with <code>POST /api/track</code> — see AppStatTrackingClient.cs.</div>';
    return;
  }
  host.innerHTML =
    `<table><thead><tr><th>Event</th><th class="num">Count</th><th class="num">Users</th><th>Last seen</th></tr></thead><tbody>` +
    rows
      .map(
        (e, i) => `<tr class="clickable" data-i="${i}">
          <td class="msg"><div class="text">${escapeHtml(e.name)}</div></td>
          <td class="num"><b>${e.count}</b></td>
          <td class="num">${e.users}</td>
          <td class="time" title="${fmtTime(e.lastSeen)}">${timeAgo(e.lastSeen)}</td></tr>`
      )
      .join("") +
    `</tbody></table>`;
  host.querySelectorAll("tr.clickable").forEach((tr) => {
    tr.addEventListener("click", () => openTrackEventModal(rows[Number(tr.dataset.i)]));
  });
}

// Non-crash Sentry log messages, grouped (the view "Events" used to show).
function renderLogs() {
  return renderGroupsPage({
    endpoint: "/api/event-groups",
    title: "Logs",
    subtitle: "Non-crash Sentry events grouped by message",
    withLevel: true,
    countHeader: "Events",
    filter: eventsFilter,
  });
}

function renderCrashes() {
  return renderGroupsPage({
    endpoint: "/api/crash-groups",
    title: "Crashes",
    subtitle: "Grouped by signature",
    withLevel: false,
    countHeader: "Crashes",
    filter: crashesFilter,
  });
}

function optionList(allLabel, values, selected) {
  return (
    `<option value="">${escapeHtml(allLabel)}</option>` +
    values.map((v) => `<option value="${escapeHtml(v)}"${v === selected ? " selected" : ""}>${escapeHtml(v)}</option>`).join("")
  );
}

async function renderGroupsPage(cfg) {
  if (!cache.facets) cache.facets = await fetchJson("/api/facets");
  const facets = cache.facets;

  view.innerHTML = `
    <div class="page-head">
      <h1>${escapeHtml(cfg.title)}</h1>
      <span class="page-sub" id="grp-sub"></span>
    </div>
    <div class="toolbar">
      <input class="search" id="grp-search" type="search" placeholder="Search…" />
      ${cfg.withLevel ? '<select id="grp-level"><option value="">All levels</option></select>' : ""}
      <select id="grp-release">${optionList("All versions", facets.releases, cfg.filter.release)}</select>
      <select id="grp-os">${optionList("All OS", facets.oses, cfg.filter.os)}</select>
    </div>
    <div class="table-wrap" id="grp-table"><div class="empty">Loading…</div></div>`;

  const relSel = document.getElementById("grp-release");
  const osSel = document.getElementById("grp-os");
  relSel.addEventListener("change", () => { cfg.filter.release = relSel.value; load(); });
  osSel.addEventListener("change", () => { cfg.filter.os = osSel.value; load(); });
  document.getElementById("grp-search").addEventListener("input", draw);

  let groups = [];
  await load();

  async function load() {
    const qs = new URLSearchParams();
    if (cfg.filter.release) qs.set("release", cfg.filter.release);
    if (cfg.filter.os) qs.set("os", cfg.filter.os);
    groups = await fetchJson(cfg.endpoint + (qs.toString() ? "?" + qs : ""));

    if (cfg.withLevel) {
      const sel = document.getElementById("grp-level");
      const cur = sel.value;
      sel.innerHTML =
        '<option value="">All levels</option>' +
        [...new Set(groups.map((g) => g.level).filter(Boolean))]
          .map((l) => `<option${l === cur ? " selected" : ""}>${escapeHtml(l)}</option>`)
          .join("");
      sel.onchange = draw;
    }
    draw();
  }

  function draw() {
    const q = document.getElementById("grp-search").value.trim().toLowerCase();
    const lvl = cfg.withLevel ? document.getElementById("grp-level").value : "";
    const rows = groups.filter((g) => {
      if (lvl && g.level !== lvl) return false;
      if (!q) return true;
      return [g.title, g.release].some((v) => String(v ?? "").toLowerCase().includes(q));
    });
    document.getElementById("grp-sub").textContent =
      `${cfg.subtitle} · ${rows.length} group${rows.length === 1 ? "" : "s"}`;

    const table = document.getElementById("grp-table");
    if (!rows.length) {
      table.innerHTML = '<div class="empty">Nothing to show.</div>';
      return;
    }
    const head = cfg.withLevel
      ? `<th>Level</th><th>Message</th><th class="num">${cfg.countHeader}</th><th class="num">Users</th><th>Last seen</th><th>Release</th>`
      : `<th>Message</th><th class="num">${cfg.countHeader}</th><th class="num">Users</th><th>Last seen</th><th>Release</th>`;
    table.innerHTML =
      `<table><thead><tr>${head}</tr></thead><tbody>` +
      rows
        .map((g, i) => {
          const lvlCell = cfg.withLevel ? `<td><span class="badge ${levelClass(g.level)}">${escapeHtml(g.level || "-")}</span></td>` : "";
          return `<tr class="clickable" data-i="${i}">
            ${lvlCell}
            <td class="msg"><div class="text">${escapeHtml(g.title)}</div></td>
            <td class="num"><b>${g.count}</b></td>
            <td class="num">${g.users}</td>
            <td class="time" title="${fmtTime(g.lastSeen)}">${timeAgo(g.lastSeen)}</td>
            <td>${escapeHtml(g.release || "-")}</td></tr>`;
        })
        .join("") +
      `</tbody></table>`;
    table.querySelectorAll("tr.clickable").forEach((tr) => {
      tr.addEventListener("click", () => openEventModal(rows[Number(tr.dataset.i)].sample, rows[Number(tr.dataset.i)]));
    });
  }
}

// ---------- shared bits ----------
function statTile(label, value, cls = "") {
  return `<div class="card ${cls}"><div class="label">${escapeHtml(label)}</div><div class="value">${escapeHtml(String(value))}</div></div>`;
}

function openEventModal(ev, group) {
  modalTitle.innerHTML = `<span class="badge ${levelClass(ev.level)}">${escapeHtml(ev.level || "-")}</span> ${escapeHtml(ev.message)}`;

  const groupRows = group
    ? [
        ["Occurrences", String(group.count)],
        ["Affected users", String(group.users)],
        ["First seen", fmtTime(group.firstSeen)],
        ["Last seen", fmtTime(group.lastSeen)],
      ]
    : [];

  const rows = [
    ...groupRows,
    ["Id", ev.id],
    ["Time", fmtTime(ev.timestamp)],
    ["Release", ev.release],
    ["OS", ev.os],
    ["Device", ev.deviceModel],
    ["User", ev.userId],
    ["Session", ev.sessionId],
    ["Trace", ev.traceId],
    ["Span", ev.spanId],
    ["Flags", [ev.isCrash ? "crash" : null, ev.isError ? "error" : null].filter(Boolean).join(", ") || "—"],
  ]
    .map(([k, v]) => `<dt>${escapeHtml(k)}</dt><dd class="mono">${escapeHtml(v || "—")}</dd>`)
    .join("");

  const stack = ev.stackTrace
    ? `<pre class="stack">${escapeHtml(ev.stackTrace)}</pre>`
    : '<p style="color:var(--muted)">No stack trace.</p>';

  modalBody.innerHTML = `<dl class="kv">${rows}</dl>${stack}`;
  modalBackdrop.classList.add("open");
}

// Detail for a custom event: totals + a value breakdown per property key.
function openTrackEventModal(stat) {
  modalTitle.innerHTML = `<span class="badge lvl-info">event</span> ${escapeHtml(stat.name)}`;

  const meta = [
    ["Occurrences", String(stat.count)],
    ["Users", String(stat.users)],
    ["First seen", fmtTime(stat.firstSeen)],
    ["Last seen", fmtTime(stat.lastSeen)],
  ]
    .map(([k, v]) => `<dt>${escapeHtml(k)}</dt><dd class="mono">${escapeHtml(v)}</dd>`)
    .join("");

  const props = stat.properties || [];
  const propsHtml = props.length
    ? `<h3 class="prop-title">Properties</h3>` +
      props.map((p) => `<div class="prop-block"><h4>${escapeHtml(p.key)}</h4><div class="breakdown"></div></div>`).join("")
    : '<p style="color:var(--muted)">No properties on this event.</p>';

  modalBody.innerHTML = `<dl class="kv">${meta}</dl>${propsHtml}`;

  const blocks = modalBody.querySelectorAll(".prop-block .breakdown");
  props.forEach((p, i) => renderHBars(blocks[i], (p.values || []).map((v) => ({ key: v.key, count: v.count }))));

  modalBackdrop.classList.add("open");
}

function closeModal() {
  modalBackdrop.classList.remove("open");
}

// ---------- wire up ----------
document.getElementById("refresh").addEventListener("click", () => {
  for (const k of Object.keys(cache)) delete cache[k];
  route();
});
document.getElementById("logout").addEventListener("click", async () => {
  await fetch("/logout", { method: "POST" });
  window.location.href = "/login.html";
});
document.getElementById("modal-close").addEventListener("click", closeModal);
modalBackdrop.addEventListener("click", (e) => {
  if (e.target === modalBackdrop) closeModal();
});
document.addEventListener("keydown", (e) => {
  if (e.key === "Escape") closeModal();
});

let resizeTimer;
window.addEventListener("resize", () => {
  clearTimeout(resizeTimer);
  resizeTimer = setTimeout(route, 200);
});

window.addEventListener("hashchange", route);

(async function init() {
  try {
    const me = await fetchJson("/api/me");
    userEl.textContent = me.username ? "Signed in as " + me.username : "";
  } catch (e) {
    if (e.message === "unauthorized") return;
  }
  if (!location.hash) location.hash = "#/overview";
  route();
})();
