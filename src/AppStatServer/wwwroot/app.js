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
  maintenance: renderMaintenance,
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
    const [stats, analytics, crashGroups, logGroups, events, sessions, dsn] = await Promise.all([
      fetchJson("/api/stats"),
      fetchJson("/api/analytics?days=30"),
      fetchJson("/api/crash-groups"),
      fetchJson("/api/event-groups"),
      fetchJson("/api/events"),
      fetchJson("/api/sessions"),
      fetchJson("/api/dsn"),
    ]);
    cache.overview = {
      stats,
      analytics,
      crashGroups,
      logGroups,
      dsn: dsn.dsn,
      events: events.slice().sort((a, b) => new Date(b.timestamp) - new Date(a.timestamp)),
      sessions: sessions.slice().sort((a, b) => new Date(b.started) - new Date(a.started)),
    };
  }
  const { stats, analytics: a } = cache.overview;

  // No data at all yet → the page's job is onboarding, not metrics.
  if (!stats.totalEvents && !stats.customEvents && !stats.totalSessions) {
    renderOverviewOnboarding();
    return;
  }

  const crashFree =
    stats.crashFreeSessionsPct == null
      ? `<span class="value-muted">n/a</span>`
      : `<span class="${pctClass(stats.crashFreeSessionsPct)}">${fmtPct(stats.crashFreeSessionsPct)}</span>`;

  const userSeries = [
    { name: "Active users", color: "var(--series-1)", values: a.usersPerDay.map((d) => d.active) },
    { name: "New users", color: "var(--series-2)", values: a.usersPerDay.map((d) => d.newUsers) },
  ];

  view.innerHTML = `
    <div class="page-head">
      <h1>Overview</h1>
      <div class="dsn-chip" title="Point your app's Sentry SDK at this DSN">
        <span class="dsn-chip-label">DSN</span>
        <code id="dsn-value">${escapeHtml(cache.overview.dsn)}</code>
        <button id="dsn-copy" type="button">Copy</button>
      </div>
    </div>
    <h2 class="section-title">App health · last 7 days</h2>
    <section class="cards tiles">
      ${kpiTile("Crash-free sessions", crashFree, {
        cls: "sessions",
        sub: stats.crashFreeSessionsPct == null ? "no sessions in the last 7 days" : `across ${stats.sessionsLast7Days} sessions`,
      })}
      ${kpiTile("Crashes", stats.crashesLast7Days, { cls: "crashes", sub: trendHtml(stats.crashesLast7Days, stats.crashesPrev7Days) })}
      ${kpiTile("Errors", stats.errorsLast7Days, { cls: "errors", sub: trendHtml(stats.errorsLast7Days, stats.errorsPrev7Days) })}
      ${kpiTile("Events today", stats.eventsToday, { sub: `${stats.totalEvents} all-time · ${stats.customEvents} custom` })}
    </section>
    <h2 class="section-title">Audience · last 30 days</h2>
    <section class="cards tiles">
      ${kpiTile("Daily active users", a.dau, { sub: `WAU ${a.wau} · MAU ${a.mau}` })}
      ${kpiTile("New users", a.newUsers, { cls: "custom-events" })}
      ${kpiTile("Sessions", a.totalSessions, { cls: "sessions", sub: `${a.sessionsPerUser.toFixed(1)} per user` })}
      ${kpiTile("Avg. session", fmtDuration(a.avgSessionSeconds))}
    </section>
    <section class="panels">
      <div class="panel">
        <h2>Active &amp; new users (last 30 days)</h2>
        ${legendHtml(userSeries)}
        <div id="ov-users"></div>
      </div>
      <div class="panel">
        <h2>Top issues</h2>
        <div class="issues" id="ov-issues"></div>
        <div class="panel-foot"><a href="#/crashes">All crashes →</a><a href="#/logs">All logs →</a></div>
      </div>
    </section>
    <section class="panels">
      <div class="panel"><h2>Events per day (last 14 days)</h2><div id="ov-chart"></div></div>
      <div class="panel"><h2>Version adoption (active users, 30d)</h2><div class="breakdown" id="ov-versions"></div></div>
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

  renderLineChart(document.getElementById("ov-users"), a.usersPerDay.map((d) => d.date), userSeries);
  renderOverviewIssues();
  renderBarChart(
    document.getElementById("ov-chart"),
    (stats.eventsPerDay || []).map((d) => ({ label: d.date.slice(5), value: d.count }))
  );
  renderHBars(document.getElementById("ov-versions"), a.versionDistribution || []);

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
  renderSessionsTable(document.getElementById("ov-sessions"), cache.overview.sessions.slice(0, 20));

  document.querySelectorAll("#view .tab").forEach((tab) => {
    tab.addEventListener("click", () => {
      document.querySelectorAll("#view .tab").forEach((t) => t.classList.toggle("active", t === tab));
      document.getElementById("ov-tab-events").hidden = tab.dataset.tab !== "events";
      document.getElementById("ov-tab-sessions").hidden = tab.dataset.tab !== "sessions";
    });
  });

  wireDsnCopy();
}

// First-run state: no data yet, so lead with the DSN and the ways to send something.
function renderOverviewOnboarding() {
  view.innerHTML = `
    <section class="dsn-panel">
      <div class="dsn-info">
        <span class="dsn-label">Sentry DSN</span>
        <p class="dsn-hint">No data yet. Point your app's Sentry SDK at this DSN to start sending events.</p>
      </div>
      <div class="dsn-row">
        <code class="dsn-value" id="dsn-value">${escapeHtml(cache.overview.dsn)}</code>
        <button id="dsn-copy" class="primary" type="button">Copy</button>
      </div>
    </section>
    <section class="maint-grid">
      <div class="maint-card">
        <h2>Crashes &amp; logs</h2>
        <p class="maint-desc">Initialize a Sentry SDK with the DSN above — crashes, errors and log events will show up under Crashes and Logs.</p>
      </div>
      <div class="maint-card">
        <h2>Custom events</h2>
        <p class="maint-desc">Send product-analytics events with <code>POST /api/track</code> (see AppStatTrackingClient.cs) — they appear under Events and drive the audience metrics.</p>
      </div>
      <div class="maint-card">
        <h2>Try it now</h2>
        <p class="maint-desc">The Maintenance page can post synthetic test data to the live ingest endpoints so you can see the dashboard working.</p>
        <a href="#/maintenance">Open Maintenance →</a>
      </div>
    </section>`;
  wireDsnCopy();
}

// Worst offenders right now: crash groups plus error/fatal log groups, most frequent first.
function renderOverviewIssues() {
  const { crashGroups, logGroups } = cache.overview;
  const issues = crashGroups
    .concat(logGroups.filter((g) => ["error", "fatal"].includes(String(g.level).toLowerCase())))
    .sort((x, y) => y.count - x.count)
    .slice(0, 7);

  const host = document.getElementById("ov-issues");
  if (!issues.length) {
    host.innerHTML = '<div class="empty">No crashes or errors. 🎉</div>';
    return;
  }
  host.innerHTML = issues
    .map(
      (g, i) => `<div class="issue-row" data-i="${i}">
        <span class="badge ${g.isCrash ? "lvl-fatal" : levelClass(g.level)}">${g.isCrash ? "crash" : escapeHtml(g.level || "error")}</span>
        <div class="issue-main">
          <div class="issue-title" title="${escapeHtml(g.title)}">${escapeHtml(g.title)}</div>
          <div class="issue-meta">${g.count}× · ${g.users} user${g.users === 1 ? "" : "s"} · ${timeAgo(g.lastSeen)}</div>
        </div>
      </div>`
    )
    .join("");
  host.querySelectorAll(".issue-row").forEach((row) => {
    row.addEventListener("click", () => {
      const g = issues[Number(row.dataset.i)];
      openEventModal(g.sample, g);
    });
  });
}

function wireDsnCopy() {
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
  // The overview is a summary page — cap the table so it doesn't dwarf the KPIs above.
  const rows = cache.overview.events
    .filter((e) => {
      if (lvl && (e.level || "") !== lvl) return false;
      if (!q) return true;
      return [e.message, e.release, e.userId, e.os, e.id].some((v) => String(v ?? "").toLowerCase().includes(q));
    })
    .slice(0, 20);
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

// Server-side platform (os) / app version (release) filters for the Events page.
const trackFilter = { release: "", os: "" };

// Custom product events (sent to /api/track), aggregated into a report.
async function renderEvents() {
  const qs = new URLSearchParams({ days: String(eventsDays) });
  if (trackFilter.release) qs.set("release", trackFilter.release);
  if (trackFilter.os) qs.set("os", trackFilter.os);
  const r = await fetchJson("/api/events-report?" + qs);
  cache.eventsReport = { days: eventsDays, data: r };

  view.innerHTML = `
    <div class="page-head">
      <h1>Events</h1>
      <span class="page-sub">Custom product events · last ${r.days} days</span>
    </div>
    <div class="toolbar">
      <div class="range">
        ${[7, 14, 30, 90].map((d) => `<button class="range-btn ${d === eventsDays ? "active" : ""}" data-days="${d}">${d}d</button>`).join("")}
      </div>
      <select id="ev-os">${optionList("All platforms", r.oses || [], trackFilter.os)}</select>
      <select id="ev-release">${optionList("All versions", r.releases || [], trackFilter.release)}</select>
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

  document.getElementById("ev-os").addEventListener("change", (e) => {
    trackFilter.os = e.target.value;
    renderEvents();
  });
  document.getElementById("ev-release").addEventListener("change", (e) => {
    trackFilter.release = e.target.value;
    renderEvents();
  });

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

// ---------- Maintenance (self-test: post synthetic data to the live ingest endpoints) ----------
const MAINT_RELEASE = "appstatserver-maintenance@1.0.0";
const maintLog = []; // in-memory activity log, newest first; survives navigation within the session

let _maintUser;
// One stable user id per page load, so repeated test sends group under a single user.
function maintUserId() {
  if (!_maintUser) _maintUser = uuid();
  return _maintUser;
}

function uuid() {
  if (crypto.randomUUID) return crypto.randomUUID();
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    return (c === "x" ? r : (r & 0x3) | 0x8).toString(16);
  });
}

// Sentry event ids are 32 hex chars (a UUID without the dashes).
function hexId() {
  return uuid().replace(/-/g, "");
}

function postRaw(url, body, contentType) {
  return fetch(url, {
    method: "POST",
    headers: { "Content-Type": contentType, Accept: "application/json" },
    body,
  });
}

// A newline-delimited Sentry envelope: header line, item header, item payload.
// The event object is built with event_id first so the server's line-prefix check matches.
function buildEventEnvelope(ev) {
  const header = { sdk: { name: "appstatserver.maintenance", version: "1.0.0" }, event_id: ev.event_id, sent_at: new Date().toISOString() };
  return JSON.stringify(header) + "\n" + JSON.stringify({ type: "event" }) + "\n" + JSON.stringify(ev);
}

async function renderMaintenance() {
  view.innerHTML = `
    <div class="page-head">
      <h1>Maintenance</h1>
      <span class="page-sub">Send synthetic data to the live ingest endpoints and verify it lands</span>
    </div>
    <section class="dsn-panel">
      <div class="dsn-info">
        <span class="dsn-label">Self-test</span>
        <p class="dsn-hint">These buttons POST to the real anonymous ingest endpoints (<code>/api/1/envelope</code>, <code>/api/track</code>) exactly like a client SDK would, then let you confirm the data was written. Test records use release <code>${escapeHtml(MAINT_RELEASE)}</code> and are saved to the live database.</p>
      </div>
    </section>
    <section class="maint-grid">
      <div class="maint-card">
        <h2>Test log event</h2>
        <p class="maint-desc">A non-crash Sentry event → <code>/api/1/envelope</code>. Appears under Logs &amp; Overview.</p>
        <div class="maint-controls">
          <select id="maint-level">
            <option value="info">info</option>
            <option value="warning">warning</option>
            <option value="error">error</option>
            <option value="fatal">fatal</option>
          </select>
          <input id="maint-msg" type="text" placeholder="Custom message (optional)" />
        </div>
        <button class="primary" data-action="event">Send event</button>
      </div>
      <div class="maint-card">
        <h2>Test crash</h2>
        <p class="maint-desc">An exception with a crashed thread &amp; stack trace → <code>/api/1/envelope</code>. Appears under Crashes.</p>
        <button class="primary" data-action="crash">Send crash</button>
      </div>
      <div class="maint-card">
        <h2>Test session</h2>
        <p class="maint-desc">A session record → <code>/api/1/envelope</code>. Appears under Overview → Sessions.</p>
        <button class="primary" data-action="session">Send session</button>
      </div>
      <div class="maint-card">
        <h2>Test custom event</h2>
        <p class="maint-desc">A product-analytics event → <code>/api/track</code>. Appears under Events.</p>
        <button class="primary" data-action="track">Send track event</button>
      </div>
    </section>
    <section class="panel wide">
      <h2>Activity log</h2>
      <div class="maint-log" id="maint-log"></div>
    </section>`;

  view.querySelectorAll(".maint-card button[data-action]").forEach((btn) => {
    btn.addEventListener("click", () => maintSend(btn.dataset.action, btn));
  });

  drawMaintLog();
}

async function maintSend(action, btn) {
  const original = btn.textContent;
  btn.disabled = true;
  btn.textContent = "Sending…";

  try {
    let res, kind, route, detail;
    const now = Date.now();

    if (action === "event") {
      const level = document.getElementById("maint-level").value;
      const msg = document.getElementById("maint-msg").value.trim() || `Test ${level} event from maintenance page`;
      const eventId = hexId();
      res = await postRaw("/api/1/envelope", buildEventEnvelope({
        event_id: eventId,
        timestamp: new Date(now).toISOString(),
        level,
        release: MAINT_RELEASE,
        logentry: { message: msg },
        contexts: { os: { raw_description: navigator.userAgent } },
        user: { id: maintUserId() },
      }), "application/x-sentry-envelope");
      kind = "log event";
      route = "logs";
      detail = `${level} · id ${shortId(eventId)}`;
    } else if (action === "crash") {
      const eventId = hexId();
      res = await postRaw("/api/1/envelope", buildEventEnvelope({
        event_id: eventId,
        timestamp: new Date(now).toISOString(),
        level: "fatal",
        release: MAINT_RELEASE,
        exception: { values: [{ type: "AppStatServer.MaintenanceTestException", value: "Test crash from maintenance page" }] },
        threads: { values: [{ id: 1, crashed: true, stacktrace: { frames: [
          { function: "Program.Main", filename: "Program.cs", lineno: 12, in_app: true },
          { function: "MaintenancePage.TriggerTestCrash", filename: "Maintenance.cs", lineno: 42, in_app: true },
        ] } }] },
        contexts: { os: { raw_description: navigator.userAgent } },
        user: { id: maintUserId() },
      }), "application/x-sentry-envelope");
      kind = "crash";
      route = "crashes";
      detail = `id ${shortId(eventId)}`;
    } else if (action === "session") {
      const sid = uuid();
      const header = JSON.stringify({ sdk: { name: "appstatserver.maintenance", version: "1.0.0" }, sent_at: new Date(now).toISOString() });
      const session = {
        sid,
        did: maintUserId(),
        init: true,
        started: new Date(now - 60000).toISOString(),
        timestamp: new Date(now).toISOString(),
        seq: 1,
        duration: 60,
        errors: 0,
        attrs: { release: MAINT_RELEASE, environment: "maintenance" },
      };
      res = await postRaw("/api/1/envelope",
        header + "\n" + JSON.stringify({ type: "session" }) + "\n" + JSON.stringify(session),
        "application/x-sentry-envelope");
      kind = "session";
      route = "overview";
      detail = `sid ${shortId(sid)}`;
    } else if (action === "track") {
      res = await postRaw("/api/track", JSON.stringify({
        userId: maintUserId(),
        sessionId: uuid(),
        release: MAINT_RELEASE,
        os: navigator.userAgent,
        events: [{
          name: "maintenance_test_event",
          timestamp: new Date(now).toISOString(),
          properties: { source: "maintenance-page", value: 42, ok: true },
        }],
      }), "application/json");
      kind = "custom event";
      route = "events";
      detail = "maintenance_test_event";
    } else {
      return;
    }

    let extra = "";
    try {
      const data = await res.json();
      if (data && typeof data.accepted === "number") extra = `accepted ${data.accepted}`;
    } catch {
      // response body is optional / non-JSON — the status code is what matters
    }

    maintLog.unshift({
      time: new Date().toISOString(),
      ok: res.ok,
      text: `${res.ok ? "Sent" : "Failed to send"} ${kind} — HTTP ${res.status}`,
      detail: [detail, extra].filter(Boolean).join(" · "),
      route: res.ok ? route : null,
    });
  } catch (e) {
    maintLog.unshift({ time: new Date().toISOString(), ok: false, text: "Request failed: " + e.message, detail: "", route: null });
  } finally {
    btn.disabled = false;
    btn.textContent = original;
    drawMaintLog();
  }
}

function drawMaintLog() {
  const host = document.getElementById("maint-log");
  if (!host) return;
  if (!maintLog.length) {
    host.innerHTML = '<div class="empty">No test requests sent yet.</div>';
    return;
  }
  host.innerHTML = maintLog
    .map((e) => `
      <div class="maint-log-row">
        <span class="badge ${e.ok ? "lvl-info" : "lvl-error"}">${e.ok ? "ok" : "fail"}</span>
        <span class="maint-log-time mono">${fmtTime(e.time)}</span>
        <span class="maint-log-text">${escapeHtml(e.text)}${e.detail ? ` <span class="maint-log-detail">${escapeHtml(e.detail)}</span>` : ""}</span>
        ${e.route ? `<a href="#" class="maint-view" data-route="${escapeHtml(e.route)}">View →</a>` : ""}
      </div>`)
    .join("");
  host.querySelectorAll(".maint-view").forEach((a) => {
    a.addEventListener("click", (ev) => {
      ev.preventDefault();
      // Drop the SPA cache so the freshly written test data shows up on the target page.
      for (const k of Object.keys(cache)) delete cache[k];
      location.hash = "#/" + a.dataset.route;
    });
  });
}

// ---------- shared bits ----------
function statTile(label, value, cls = "") {
  return `<div class="card ${cls}"><div class="label">${escapeHtml(label)}</div><div class="value">${escapeHtml(String(value))}</div></div>`;
}

// Like statTile, but the value may carry markup and an optional sub-line (trend, context).
function kpiTile(label, valueHtml, opts = {}) {
  const sub = opts.sub ? `<div class="kpi-sub">${opts.sub}</div>` : "";
  return `<div class="card ${opts.cls || ""}"><div class="label">${escapeHtml(label)}</div><div class="value">${valueHtml}</div>${sub}</div>`;
}

// Week-over-week delta for "bad" counters (crashes, errors): growth is red, decline is green.
function trendHtml(cur, prev) {
  const diff = cur - prev;
  if (diff === 0) return `<span class="trend flat">no change vs prev 7 days</span>`;
  const cls = diff > 0 ? "bad" : "good";
  return `<span class="trend ${cls}">${diff > 0 ? "▲" : "▼"} ${Math.abs(diff)} vs prev 7 days</span>`;
}

function fmtPct(v) {
  return (v >= 99.95 ? "100" : v.toFixed(1)) + "%";
}

function pctClass(v) {
  return v >= 99 ? "pct-good" : v >= 95 ? "pct-warn" : "pct-bad";
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
