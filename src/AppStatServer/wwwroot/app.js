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

async function postJson(url, body) {
  const res = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify(body),
  });
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
  funnels: renderFunnels,
  logs: renderLogs,
  crashes: renderDiagnostics,
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
      <div class="panel"><h2>Events per day (last 14 days)</h2><div id="ov-chart-legend"></div><div id="ov-chart"></div></div>
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
  const epd = stats.eventsPerDay || [];
  const eventSeries = [
    { name: "Events", color: "var(--series-1)", values: epd.map((d) => d.events) },
    { name: "Custom", color: "var(--series-2)", values: epd.map((d) => d.custom) },
  ];
  document.getElementById("ov-chart-legend").innerHTML = legendHtml(eventSeries);
  renderStackedBarChart(document.getElementById("ov-chart"), epd.map((d) => d.date.slice(5)), eventSeries);
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
    const [data, retention] = await Promise.all([
      fetchJson("/api/analytics?days=" + analyticsDays),
      cache.retention ? Promise.resolve(cache.retention) : fetchJson("/api/retention?weeks=8"),
    ]);
    cache.retention = retention;
    cache.analytics = { days: analyticsDays, data };
  }
  const a = cache.analytics.data;
  const ret = cache.retention;
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
    <section class="panel wide"><h2>Top devices</h2><div class="breakdown" id="an-devices"></div></section>
    <h2 class="section-title">Retention</h2>
    <section class="cards tiles">
      ${retentionTile("Day 1", ret.d1)}
      ${retentionTile("Day 7", ret.d7)}
      ${retentionTile("Day 30", ret.d30)}
    </section>
    <section class="panel wide">
      <h2>Weekly cohorts</h2>
      <p class="panel-hint">Each row is the users who first appeared that week; each cell is the share of them active N weeks later.</p>
      <div id="an-cohorts"></div>
    </section>`;

  renderLineChart(document.getElementById("an-users"), labels, series);
  renderCohortTable(document.getElementById("an-cohorts"), ret.cohorts || []);
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

// A D1/D7/D30 tile: the % plus how many of the eligible users came back.
function retentionTile(label, point) {
  const value = point && point.pct != null ? point.pct + "%" : '<span class="value-muted">n/a</span>';
  const sub = point && point.eligible
    ? `${point.retained} of ${point.eligible} users returned`
    : "no users old enough yet";
  return kpiTile(label + " retention", value, { sub });
}

// The cohort triangle: one row per weekly cohort, cells shaded by % retained.
function renderCohortTable(host, cohorts) {
  const rows = cohorts.filter((c) => c.size > 0);
  if (!rows.length) {
    host.innerHTML = '<div class="empty">Not enough data yet — cohorts appear once users have a first-seen week.</div>';
    return;
  }
  const width = Math.max(...rows.map((c) => c.values.length));
  const head =
    "<tr><th>Cohort week</th><th class=\"num\">Users</th>" +
    Array.from({ length: width }, (_, i) => `<th class="num">W${i}</th>`).join("") +
    "</tr>";
  const body = rows
    .map((c) => {
      const cells = Array.from({ length: width }, (_, i) => {
        const v = c.values[i];
        if (v == null) return '<td class="cohort-cell cohort-future"></td>';
        // Shade tracks the retention share; capped so the label stays readable in both themes.
        const mix = Math.round(Math.min(100, v) * 0.55);
        return `<td class="cohort-cell" style="background:color-mix(in srgb, var(--accent) ${mix}%, transparent)">${Math.round(v)}%</td>`;
      }).join("");
      return `<tr><td class="time">${escapeHtml(c.week)}</td><td class="num">${c.size}</td>${cells}</tr>`;
    })
    .join("");
  host.innerHTML = `<div class="table-wrap"><table class="cohort-table"><thead>${head}</thead><tbody>${body}</tbody></table></div>`;
}

// ---------- Funnels (saved conversion funnels over custom events) ----------
let funnelsDays = 30;

async function renderFunnels() {
  const [funnels, names] = await Promise.all([
    fetchJson("/api/funnels"),
    cache.eventNames
      ? Promise.resolve(cache.eventNames)
      : fetchJson("/api/events-report?days=90").then((r) => (r.events || []).map((e) => e.name)),
  ]);
  cache.eventNames = names;

  view.innerHTML = `
    <div class="page-head">
      <h1>Funnels</h1>
      <span class="page-sub">Ordered conversion over custom events · last ${funnelsDays} days</span>
    </div>
    <div class="toolbar">
      <div class="range">
        ${[7, 14, 30, 90].map((d) => `<button class="range-btn ${d === funnelsDays ? "active" : ""}" data-days="${d}">${d}d</button>`).join("")}
      </div>
    </div>
    <div id="fn-list"></div>
    <section class="panel wide">
      <h2>New funnel</h2>
      <p class="panel-hint">Pick 2–10 event names in the order users should pass them (e.g. <code>site_visit</code> → <code>site_click</code>, or <code>app_started</code> → <code>purchase</code>). A user counts for a step only after passing the previous one.</p>
      <datalist id="fn-names">${names.map((n) => `<option value="${escapeHtml(n)}"></option>`).join("")}</datalist>
      <div class="fn-form">
        <input id="fn-name" type="text" placeholder="Funnel name" />
        <div id="fn-steps"></div>
        <div class="fn-form-actions">
          <button id="fn-add-step" type="button">+ Add step</button>
          <button id="fn-create" class="primary" type="button">Create funnel</button>
        </div>
        <div class="fn-error" id="fn-error" hidden></div>
      </div>
    </section>`;

  document.querySelectorAll(".range-btn").forEach((b) => {
    b.addEventListener("click", () => {
      funnelsDays = Number(b.dataset.days);
      renderFunnels();
    });
  });

  // --- builder: a list of step inputs backed by the datalist of known event names ---
  const stepsHost = document.getElementById("fn-steps");
  function addStepInput(value = "") {
    const row = document.createElement("div");
    row.className = "fn-step-row";
    row.innerHTML = `
      <span class="fn-step-n">${stepsHost.children.length + 1}</span>
      <input type="text" list="fn-names" placeholder="Event name…" value="${escapeHtml(value)}" />
      <button type="button" class="fn-remove" title="Remove step">×</button>`;
    row.querySelector(".fn-remove").addEventListener("click", () => {
      row.remove();
      [...stepsHost.children].forEach((r, i) => (r.querySelector(".fn-step-n").textContent = i + 1));
    });
    stepsHost.appendChild(row);
  }
  addStepInput();
  addStepInput();
  document.getElementById("fn-add-step").addEventListener("click", () => addStepInput());

  document.getElementById("fn-create").addEventListener("click", async () => {
    const name = document.getElementById("fn-name").value.trim();
    const steps = [...stepsHost.querySelectorAll("input")].map((i) => i.value.trim()).filter(Boolean);
    const errEl = document.getElementById("fn-error");
    if (!name || steps.length < 2) {
      errEl.textContent = "Give the funnel a name and at least two steps.";
      errEl.hidden = false;
      return;
    }
    try {
      await postJson("/api/funnels", { name, steps });
      renderFunnels();
    } catch {
      errEl.textContent = "Failed to save the funnel.";
      errEl.hidden = false;
    }
  });

  // --- saved funnels, each with its report over the selected window ---
  const list = document.getElementById("fn-list");
  if (!funnels.length) {
    list.innerHTML = `<div class="empty">No funnels yet. Create one below${names.length ? "" : " — send some custom events first (see the Events page)"}.</div>`;
    return;
  }
  list.innerHTML = funnels
    .map(
      (f, i) => `
      <section class="panel wide funnel-panel" data-id="${escapeHtml(f.id)}">
        <div class="funnel-head">
          <h2>${escapeHtml(f.name)}</h2>
          <span class="funnel-overall" id="fn-overall-${i}"></span>
          <button class="fn-delete" data-id="${escapeHtml(f.id)}" title="Delete funnel">Delete</button>
        </div>
        <div class="funnel-body" id="fn-body-${i}"><div class="empty">Loading…</div></div>
      </section>`
    )
    .join("");

  list.querySelectorAll(".fn-delete").forEach((btn) => {
    btn.addEventListener("click", async () => {
      if (!confirm("Delete this funnel? Its events stay — only the definition is removed.")) return;
      await fetch("/api/funnels/" + encodeURIComponent(btn.dataset.id), { method: "DELETE" });
      renderFunnels();
    });
  });

  funnels.forEach(async (f, i) => {
    const body = document.getElementById(`fn-body-${i}`);
    try {
      const r = await fetchJson(`/api/funnels/${encodeURIComponent(f.id)}/report?days=${funnelsDays}`);
      drawFunnel(body, document.getElementById(`fn-overall-${i}`), r);
    } catch {
      body.innerHTML = '<div class="empty">Failed to load this funnel.</div>';
    }
  });
}

function drawFunnel(host, overallEl, report) {
  const steps = report.steps || [];
  if (!steps.length || !steps[0].users) {
    host.innerHTML = '<div class="empty">No users entered this funnel in the window.</div>';
    if (overallEl) overallEl.textContent = "";
    return;
  }
  const last = steps[steps.length - 1];
  if (overallEl) overallEl.textContent = `${last.pctOfFirst}% overall conversion`;

  host.innerHTML = steps
    .map(
      (s, i) => `
      <div class="funnel-step">
        <div class="funnel-step-head">
          <span class="funnel-step-name">${i + 1}. ${escapeHtml(s.name)}</span>
          <span class="funnel-step-meta">${s.users} user${s.users === 1 ? "" : "s"}${
            s.pctOfPrevious != null ? ` · ${s.pctOfPrevious}% of previous` : ""
          }</span>
        </div>
        <div class="funnel-bar">
          <div class="funnel-fill" style="width:${Math.max(s.pctOfFirst, 0.5)}%"></div>
          <span class="funnel-pct">${s.pctOfFirst}%</span>
        </div>
      </div>`
    )
    .join("");
}

// The website tracker embed code, built for whatever host the dashboard is served from.
function siteSnippetCode() {
  return `<script async src="${location.origin}/track.js" data-release="website@1.0.0"><\/script>`;
}
const SITE_CLICK_EXAMPLE = `<a href="/download" data-track="download_button">Download<\/a>`;

// Embed instructions for the website snippet (track.js), mirroring the MCP modal.
function openSiteModal() {
  const copyBtn = document.getElementById("modal-copy");
  copyBtn.hidden = true;
  copyBtn.onclick = null;
  const resolveBtn = document.getElementById("modal-resolve");
  resolveBtn.hidden = true;
  resolveBtn.onclick = null;

  const snippet = siteSnippetCode();
  const clickExample = SITE_CLICK_EXAMPLE;

  modalTitle.textContent = "Connect your website";
  modalBody.innerHTML = `
    <p class="mcp-intro">Paste this snippet into your site's <code>&lt;head&gt;</code> to count visits, referrers and UTM campaigns here, next to your app's analytics. No cookies, no third parties.</p>
    <div class="mcp-field">
      <label>Embed snippet</label>
      <pre class="mcp-config">${escapeHtml(snippet)}</pre>
      <button id="site-copy-snippet" class="primary" type="button">Copy snippet</button>
    </div>
    <div class="mcp-field">
      <label>What gets tracked</label>
      <ul class="site-list">
        <li><code>site_visit</code> — every page view, with path, external referrer and <code>utm_source / utm_medium / utm_campaign</code>.</li>
        <li><code>site_click</code> — clicks on any element with a <code>data-track</code> attribute, e.g.:</li>
      </ul>
      <pre class="mcp-config">${escapeHtml(clickExample)}</pre>
      <button id="site-copy-click" type="button">Copy example</button>
    </div>
    <p class="mcp-foot">Then build a funnel like <code>site_visit → site_click</code> on the Funnels page to see how many visitors head for the download. Custom events: <code>appstat.track("name", { any: "props" })</code>.</p>`;

  wireCopyBtn(document.getElementById("site-copy-snippet"), snippet);
  wireCopyBtn(document.getElementById("site-copy-click"), clickExample);
  modalBackdrop.classList.add("open");
}

// ---------- Events / Logs (grouped, server-filtered by version/OS) ----------
const eventsFilter = { release: "", os: "" };

// ---------- Crashes & errors (AppCenter-style diagnostics) ----------
// release/days are server filters; status/kind/search are applied client-side.
const diagnostics = { release: "", days: 30, status: "", kind: "", search: "" };

// Server-side platform (os) / app version (release) filters for the Events page.
const trackFilter = { release: "", os: "" };

// Maps a platform bucket (from the backend) to its chart color; anything unrecognised
// falls back to the neutral "other" token.
const PLATFORM_COLORS = {
  Windows: "var(--plat-windows)",
  Android: "var(--plat-android)",
  Web: "var(--plat-web)",
  Linux: "var(--plat-linux)",
  macOS: "var(--plat-macos)",
  iOS: "var(--plat-ios)",
};
function platformColor(name) {
  return PLATFORM_COLORS[name] || "var(--plat-other)";
}

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
      <button class="mcp-connect" id="ev-connect-site" type="button" title="Get the snippet that tracks your website's visits here">
        <span class="mcp-connect-dot"></span>Connect website
      </button>
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
    <section class="panel wide"><h2>Events per day</h2><div id="ev-chart-legend"></div><div id="ev-chart"></div></section>
    <div class="toolbar"><input class="search" id="ev-search" type="search" placeholder="Search event name…" /></div>
    <div class="table-wrap" id="ev-table"></div>`;

  // Stack each day's bar into a colored segment per platform (Windows / Android / Web / …).
  const ppd = r.platformsPerDay || [];
  const platSeries = (r.platforms || []).map((p) => ({
    name: p,
    color: platformColor(p),
    values: ppd.map((d) => (d.counts && d.counts[p]) || 0),
  }));
  document.getElementById("ev-chart-legend").innerHTML = platSeries.length > 1 ? legendHtml(platSeries) : "";
  renderStackedBarChart(
    document.getElementById("ev-chart"),
    ppd.map((d) => d.date.slice(5)),
    platSeries
  );

  document.getElementById("ev-connect-site").addEventListener("click", openSiteModal);
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

// AppCenter-style diagnostics: crashes + handled errors, two per-day charts and one
// combined table, filterable by app version, with per-issue resolve/reopen.
async function renderDiagnostics() {
  if (!cache.facets) cache.facets = await fetchJson("/api/facets");
  const facets = cache.facets;

  view.innerHTML = `
    <div class="page-head">
      <h1>Crashes &amp; errors</h1>
      <span class="page-sub" id="diag-sub"></span>
      <button class="mcp-connect" id="diag-connect-mcp" type="button" title="Let an AI agent read these crashes live via MCP">
        <span class="mcp-connect-dot"></span>Connect MCP
      </button>
    </div>
    <div class="toolbar">
      <div class="range">
        ${[7, 14, 30, 90].map((d) => `<button class="range-btn ${d === diagnostics.days ? "active" : ""}" data-days="${d}">${d}d</button>`).join("")}
      </div>
      <input class="search" id="diag-search" type="search" placeholder="Search message…" value="${escapeHtml(diagnostics.search)}" />
      <select id="diag-release">${optionList("All versions", facets.releases, diagnostics.release)}</select>
      <select id="diag-kind">
        <option value=""${diagnostics.kind === "" ? " selected" : ""}>Crashes &amp; errors</option>
        <option value="crash"${diagnostics.kind === "crash" ? " selected" : ""}>Crashes only</option>
        <option value="error"${diagnostics.kind === "error" ? " selected" : ""}>Errors only</option>
      </select>
      <select id="diag-status">
        <option value=""${diagnostics.status === "" ? " selected" : ""}>All statuses</option>
        <option value="open"${diagnostics.status === "open" ? " selected" : ""}>Open</option>
        <option value="resolved"${diagnostics.status === "resolved" ? " selected" : ""}>Resolved</option>
      </select>
    </div>
    <section class="cards" id="diag-cards"></section>
    <section class="panels two">
      <div class="panel"><h2>Crashes per day</h2><div id="diag-crash-chart"></div></div>
      <div class="panel"><h2>Errors per day</h2><div id="diag-error-chart"></div></div>
    </section>
    <div class="table-wrap" id="diag-table"><div class="empty">Loading…</div></div>`;

  const relSel = document.getElementById("diag-release");
  const statusSel = document.getElementById("diag-status");
  const kindSel = document.getElementById("diag-kind");
  const searchEl = document.getElementById("diag-search");

  relSel.addEventListener("change", () => { diagnostics.release = relSel.value; load(); });
  statusSel.addEventListener("change", () => { diagnostics.status = statusSel.value; draw(); });
  kindSel.addEventListener("change", () => { diagnostics.kind = kindSel.value; draw(); });
  searchEl.addEventListener("input", () => { diagnostics.search = searchEl.value; draw(); });
  document.querySelectorAll(".range-btn").forEach((b) => {
    b.addEventListener("click", () => { diagnostics.days = Number(b.dataset.days); renderDiagnostics(); });
  });
  document.getElementById("diag-connect-mcp").addEventListener("click", openMcpModal);

  let report = null;
  await load();

  async function load() {
    const qs = new URLSearchParams({ days: String(diagnostics.days) });
    if (diagnostics.release) qs.set("release", diagnostics.release);
    report = await fetchJson("/api/diagnostics?" + qs);

    document.getElementById("diag-cards").innerHTML =
      statTile("Crashes", report.totalCrashes, "crashes") +
      statTile("Errors", report.totalErrors, "errors") +
      statTile("Affected users", report.affectedUsers, "sessions") +
      statTile("Open issues", report.openGroups);

    renderBarChart(
      document.getElementById("diag-crash-chart"),
      report.crashesPerDay.map((d) => ({ label: d.date.slice(5), value: d.count })),
      { color: "var(--critical)" }
    );
    renderBarChart(
      document.getElementById("diag-error-chart"),
      report.errorsPerDay.map((d) => ({ label: d.date.slice(5), value: d.count })),
      { color: "var(--serious)" }
    );

    draw();
  }

  function draw() {
    const q = diagnostics.search.trim().toLowerCase();
    const rows = report.groups.filter((g) => {
      if (diagnostics.kind && g.kind !== diagnostics.kind) return false;
      if (diagnostics.status === "open" && g.resolved) return false;
      if (diagnostics.status === "resolved" && !g.resolved) return false;
      if (!q) return true;
      return [g.title, g.release].some((v) => String(v ?? "").toLowerCase().includes(q));
    });

    document.getElementById("diag-sub").textContent =
      `Last ${report.days} days · ${rows.length} issue${rows.length === 1 ? "" : "s"}` +
      (diagnostics.release ? ` · ${diagnostics.release}` : "");

    const host = document.getElementById("diag-table");
    if (!rows.length) {
      host.innerHTML = report.totalCrashes || report.totalErrors
        ? '<div class="empty">No issues match this filter.</div>'
        : '<div class="empty">No crashes or errors in this window. 🎉</div>';
      return;
    }
    host.innerHTML =
      `<table><thead><tr><th>Type</th><th>Message</th><th class="num">Count</th><th class="num">Users</th><th>Last seen</th><th>Version</th><th>Status</th><th></th></tr></thead><tbody>` +
      rows
        .map((g, i) => {
          const kindBadge = g.kind === "crash"
            ? '<span class="kind kind-crash">Crash</span>'
            : '<span class="kind kind-error">Error</span>';
          const statusBadge = g.resolved
            ? '<span class="status resolved">Resolved</span>'
            : '<span class="status open">Open</span>';
          return `<tr class="clickable ${g.resolved ? "is-resolved" : ""}" data-i="${i}">
            <td>${kindBadge}</td>
            <td class="msg"><div class="text">${escapeHtml(g.title)}</div></td>
            <td class="num"><b>${g.count}</b></td>
            <td class="num">${g.users}</td>
            <td class="time" title="${fmtTime(g.lastSeen)}">${timeAgo(g.lastSeen)}</td>
            <td>${escapeHtml(g.release || "-")}</td>
            <td>${statusBadge}</td>
            <td class="actions"><button class="resolve-btn" data-i="${i}">${g.resolved ? "Reopen" : "Resolve"}</button></td></tr>`;
        })
        .join("") +
      `</tbody></table>`;

    host.querySelectorAll("tr.clickable").forEach((tr) => {
      tr.addEventListener("click", (e) => {
        if (e.target.closest(".resolve-btn")) return;
        const g = rows[Number(tr.dataset.i)];
        openEventModal(g.sample, g, resolve);
      });
    });
    host.querySelectorAll(".resolve-btn").forEach((btn) => {
      btn.addEventListener("click", (e) => {
        e.stopPropagation();
        resolve(rows[Number(btn.dataset.i)]);
      });
    });
  }

  async function resolve(group) {
    await postJson("/api/resolve", { key: group.key, resolved: !group.resolved });
    await load();
  }
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
      <span class="page-sub">Host resources and synthetic-data self-test</span>
    </div>
    <div class="section-head">
      <h2 class="section-title">System resources</h2>
      <button id="sys-refresh" type="button" class="btn-sm">Refresh</button>
    </div>
    <div id="sys-host"><div class="empty">Loading system info…</div></div>

    <h2 class="section-title">Data cleanup</h2>
    <section class="maint-grid">
      <div class="maint-card">
        <h2>Delete old data</h2>
        <p class="maint-desc">Removes raw events, sessions and custom events older than the chosen age. Aggregates on the dashboard are computed from raw records, so history beyond this age disappears. Cannot be undone.</p>
        <div class="maint-controls">
          <select id="purge-days">
            <option value="365">older than 1 year</option>
            <option value="180" selected>older than 180 days</option>
            <option value="90">older than 90 days</option>
            <option value="30">older than 30 days</option>
          </select>
        </div>
        <p class="maint-est" id="purge-est">Estimating…</p>
        <button class="danger" id="purge-btn">Delete old data</button>
      </div>
      <div class="maint-card">
        <h2>Compact database</h2>
        <p class="maint-desc">Rebuilds the LiteDB file so pages freed by deletions are returned to the OS — run it after a cleanup to actually shrink the file. Takes a moment on a large database.</p>
        <p class="maint-est" id="compact-est"></p>
        <button class="primary" id="compact-btn">Compact now</button>
      </div>
    </section>

    <h2 class="section-title">Website tracking</h2>
    <section class="dsn-panel">
      <div class="dsn-info">
        <span class="dsn-label">Connect your website</span>
        <p class="dsn-hint">Count your site's visits, referrers and UTM campaigns here, next to the app's analytics — no cookies, no third parties. Paste this into the site's <code>&lt;head&gt;</code>:</p>
      </div>
      <div class="site-embed">
        <pre class="mcp-config">${escapeHtml(siteSnippetCode())}</pre>
        <button id="maint-copy-snippet" class="primary" type="button">Copy snippet</button>
      </div>
      <div class="dsn-info">
        <p class="dsn-hint">Every page view arrives as <code>site_visit</code> (path, external referrer, <code>utm_source / utm_medium / utm_campaign</code>). To count clicks — e.g. on a download button — add a <code>data-track</code> attribute to any element:</p>
      </div>
      <div class="site-embed">
        <pre class="mcp-config">${escapeHtml(SITE_CLICK_EXAMPLE)}</pre>
        <button id="maint-copy-click" type="button">Copy example</button>
      </div>
      <div class="dsn-info">
        <p class="dsn-hint">Clicks arrive as <code>site_click</code>. Custom events: <code>appstat.track("name", { any: "props" })</code>. Then build a funnel like <code>site_visit → site_click</code> on the <a href="#/funnels">Funnels</a> page to see how many visitors head for the download.</p>
      </div>
    </section>

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

  document.getElementById("sys-refresh").addEventListener("click", () => loadSystemInfo());
  loadSystemInfo();

  wireCopyBtn(document.getElementById("maint-copy-snippet"), siteSnippetCode());
  wireCopyBtn(document.getElementById("maint-copy-click"), SITE_CLICK_EXAMPLE);
  document.getElementById("purge-btn").addEventListener("click", maintPurge);
  document.getElementById("compact-btn").addEventListener("click", maintCompact);
  document.getElementById("purge-days").addEventListener("change", updatePurgeEstimate);
  updatePurgeEstimate();

  drawMaintLog();
}

// Preview what the selected purge age would remove, so the red button isn't a blind jump.
async function updatePurgeEstimate() {
  const el = document.getElementById("purge-est");
  if (!el) return;
  const days = Number(document.getElementById("purge-days").value);
  el.textContent = "Estimating…";
  try {
    const r = await fetchJson("/api/maintenance/purge-preview?olderThanDays=" + days);
    el.textContent = r.total
      ? `Would remove ${fmtNum(r.total)} record${r.total === 1 ? "" : "s"} (~${fmtBytes(r.bytes)} of data)`
      : "Nothing is older than this — nothing would be removed.";
  } catch {
    el.textContent = "Couldn't estimate.";
  }
}

// Delete raw records older than the selected age, then refresh the storage picture.
async function maintPurge() {
  const btn = document.getElementById("purge-btn");
  const days = Number(document.getElementById("purge-days").value);
  if (!confirm(`Delete all events, sessions and custom events older than ${days} days? This cannot be undone.`)) return;

  btn.disabled = true;
  const original = btn.textContent;
  btn.textContent = "Deleting…";
  try {
    const r = await postJson("/api/maintenance/purge", { olderThanDays: days });
    maintLog.unshift({
      time: new Date().toISOString(),
      ok: true,
      text: `Deleted ${r.total} record${r.total === 1 ? "" : "s"} older than ${days} days`,
      detail: `${r.events} events · ${r.sessions} sessions · ${r.trackEvents} custom events · ~${fmtBytes(r.bytes)} freed (compact to shrink the file)`,
      route: null,
    });
    // The SPA caches page data — drop it so every page reflects the purge.
    for (const k of Object.keys(cache)) delete cache[k];
  } catch (e) {
    maintLog.unshift({ time: new Date().toISOString(), ok: false, text: "Purge failed: " + e.message, detail: "", route: null });
  } finally {
    btn.disabled = false;
    btn.textContent = original;
    drawMaintLog();
    loadSystemInfo();
    updatePurgeEstimate();
  }
}

// Rebuild the database file to reclaim the pages freed by purges.
async function maintCompact() {
  const btn = document.getElementById("compact-btn");
  if (!confirm("Compact the database now? The server rebuilds its data file; this may take a moment.")) return;

  btn.disabled = true;
  const original = btn.textContent;
  btn.textContent = "Compacting…";
  try {
    const r = await postJson("/api/maintenance/compact", {});
    const freed = Math.max(0, (r.bytesBefore || 0) - (r.bytesAfter || 0));
    maintLog.unshift({
      time: new Date().toISOString(),
      ok: true,
      text: `Database compacted — ${fmtBytes(freed)} reclaimed`,
      detail: `${fmtBytes(r.bytesBefore)} → ${fmtBytes(r.bytesAfter)}`,
      route: null,
    });
  } catch (e) {
    maintLog.unshift({ time: new Date().toISOString(), ok: false, text: "Compaction failed: " + e.message, detail: "", route: null });
  } finally {
    btn.disabled = false;
    btn.textContent = original;
    drawMaintLog();
    loadSystemInfo();
  }
}

// Fetch and render the host-resource panel. Kept out of the SPA cache so Refresh always
// reflects the live disk/RAM/storage picture.
async function loadSystemInfo() {
  const host = document.getElementById("sys-host");
  if (!host) return;
  try {
    const s = await fetchJson("/api/system");
    host.innerHTML = drawSystemInfo(s);

    // The compact card's estimate: the gap between the file and its live data is free
    // pages + index overhead — the upper bound of what a rebuild can give back.
    const compactEst = document.getElementById("compact-est");
    if (compactEst) {
      const sto = s.storage || {};
      const reclaimable = Math.max(0, (sto.databaseFileBytes || 0) - (sto.dataBytes || 0));
      compactEst.textContent = sto.databaseFileBytes
        ? `Up to ~${fmtBytes(reclaimable)} of the ${fmtBytes(sto.databaseFileBytes)} file is reclaimable (free + index pages).`
        : "";
    }
  } catch {
    host.innerHTML = '<div class="empty">Failed to load system info.</div>';
  }
}

function drawSystemInfo(s) {
  const disk = s.disk || {};
  const mem = s.memory || {};
  const sto = s.storage || {};

  const diskCard = meterCard("Disk", disk.freeBytes, disk.totalBytes, {
    valueText: fmtBytes(disk.freeBytes) + " free",
    sub: `${fmtBytes(disk.usedBytes)} of ${fmtBytes(disk.totalBytes)} used${disk.drive ? " · " + escapeHtml(disk.drive) : ""}`,
    // For disk/RAM the meter tracks *used*, so a full bar (little free) is the warning.
    fillBytes: disk.usedBytes,
  });

  const ramCard = meterCard("Memory (RAM)", mem.freeBytes, mem.totalBytes, {
    valueText: fmtBytes(mem.freeBytes) + " free",
    sub: `${fmtBytes(mem.usedBytes)} of ${fmtBytes(mem.totalBytes)} used`,
    fillBytes: mem.usedBytes,
  });

  const procCard = meterCard("This process", mem.processWorkingSetBytes, mem.totalBytes, {
    valueText: fmtBytes(mem.processWorkingSetBytes),
    sub: `managed heap ${fmtBytes(mem.processManagedHeapBytes)} · ${pctText(mem.processWorkingSetBytes, mem.totalBytes)} of RAM`,
    fillBytes: mem.processWorkingSetBytes,
  });

  // The database file is our storage footprint; the fill shows how much of it is live data
  // (the rest is index and free-page overhead reclaimed on compaction).
  const dataCard = meterCard("Our data", sto.dataBytes, sto.databaseFileBytes, {
    valueText: fmtBytes(sto.databaseFileBytes),
    sub: `${fmtBytes(sto.dataBytes)} live${disk.totalBytes ? " · " + pctText(sto.databaseFileBytes, disk.totalBytes) + " of disk" : ""}`,
    fillBytes: sto.dataBytes,
    neutral: true,
  });

  const cards = `<section class="sys-grid">${diskCard}${ramCard}${procCard}${dataCard}</section>`;

  const cols = (sto.collections || []).filter((c) => c.documents > 0);
  const maxBytes = cols.reduce((m, c) => Math.max(m, c.bytes), 0) || 1;
  const rows = cols.length
    ? cols
        .map(
          (c) => `
      <div class="sto-row">
        <span class="sto-name">${escapeHtml(c.label || c.name)}</span>
        <span class="sto-meta">${fmtNum(c.documents)} ${c.documents === 1 ? "record" : "records"} · ${fmtBytes(c.bytes)}</span>
        <div class="sto-bar"><div class="sto-bar-fill" style="width:${((c.bytes / maxBytes) * 100).toFixed(1)}%"></div></div>
      </div>`
        )
        .join("")
    : '<div class="empty">No stored data yet.</div>';

  const note = sto.databasePath
    ? `<div class="sys-note">Database file: <span class="mono">${escapeHtml(sto.databasePath)}</span></div>`
    : "";

  return `${cards}
    <section class="panel wide">
      <h2>Storage by data type</h2>
      <div class="sto-table">${rows}</div>
      ${note}
    </section>`;
}

// A resource tile: big value, a used/total meter, and a sub-line. `fillBytes` is what the
// bar tracks (defaults to `used`); `neutral` keeps the bar accent-coloured instead of
// switching to warning/critical as it fills.
function meterCard(label, used, total, opts = {}) {
  const fill = opts.fillBytes != null ? opts.fillBytes : used;
  const pct = total > 0 ? Math.min(100, Math.max(0, (fill / total) * 100)) : 0;
  const cls = opts.neutral ? "neutral" : pct >= 90 ? "bad" : pct >= 75 ? "warn" : "good";
  const value = opts.valueText != null ? opts.valueText : fmtBytes(used);
  const sub = opts.sub ? `<div class="sys-sub">${opts.sub}</div>` : "";
  return `
    <div class="sys-card">
      <div class="sys-head"><span class="sys-label">${escapeHtml(label)}</span><span class="sys-val">${escapeHtml(value)}</span></div>
      <div class="meter"><div class="meter-fill ${cls}" style="width:${pct.toFixed(1)}%"></div></div>
      ${sub}
    </div>`;
}

function pctText(part, whole) {
  if (!whole) return "0%";
  const p = (part / whole) * 100;
  return (p < 0.1 ? "<0.1" : p < 10 ? p.toFixed(1) : Math.round(p)) + "%";
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

// Assemble a self-contained, paste-into-an-agent report of a crash/error: all the
// identifying metadata plus the stack trace (and the raw Sentry payload when present).
function buildEventReport(ev, group) {
  const kind = group?.kind === "crash" || ev.isCrash ? "Crash" : ev.isError ? "Error" : "Event";
  const title = (group && group.title) || ev.message || "(no message)";
  const lines = [`# ${kind}: ${title}`, ""];

  const summary = [["Type", kind]];
  if (ev.level) summary.push(["Level", ev.level]);
  if (ev.message) summary.push(["Message", ev.message]);
  if (group) {
    if (group.count != null) summary.push(["Occurrences", String(group.count)]);
    if (group.users != null) summary.push(["Affected users", String(group.users)]);
    if (group.firstSeen) summary.push(["First seen", fmtTime(group.firstSeen)]);
    if (group.lastSeen) summary.push(["Last seen", fmtTime(group.lastSeen)]);
    if (group.resolved !== undefined)
      summary.push([
        "Status",
        group.resolved ? "Resolved" + (group.resolvedAt ? " (" + fmtTime(group.resolvedAt) + ")" : "") : "Open",
      ]);
  }
  const release = ev.release || (group && group.release);
  if (release) summary.push(["Release", release]);
  if (ev.os) summary.push(["OS", ev.os]);
  if (ev.deviceModel) summary.push(["Device", ev.deviceModel]);
  lines.push("## Summary", ...summary.map(([k, v]) => `- ${k}: ${v}`), "");

  const meta = [];
  if (ev.id) meta.push(["Id", ev.id]);
  if (ev.timestamp) meta.push(["Time", fmtTime(ev.timestamp)]);
  if (ev.userId) meta.push(["User", ev.userId]);
  if (ev.sessionId) meta.push(["Session", ev.sessionId]);
  if (ev.traceId) meta.push(["Trace", ev.traceId]);
  if (ev.spanId) meta.push(["Span", ev.spanId]);
  const flags = [ev.isCrash ? "crash" : null, ev.isError ? "error" : null].filter(Boolean).join(", ");
  if (flags) meta.push(["Flags", flags]);
  if (meta.length) lines.push("## Event", ...meta.map(([k, v]) => `- ${k}: ${v}`), "");

  if (ev.stackTrace) lines.push("## Stack trace", "```", ev.stackTrace, "```", "");

  // The flat events list keeps the raw Sentry payload; grouped samples trim it — include when present.
  if (ev.eventEntry) {
    let raw = ev.eventEntry;
    try {
      raw = JSON.stringify(JSON.parse(ev.eventEntry), null, 2);
    } catch {
      // not JSON — copy as-is
    }
    lines.push("## Raw event", "```json", raw, "```", "");
  }

  return lines.join("\n").trim() + "\n";
}

// Copy arbitrary text, with a fallback for non-secure contexts where the async
// Clipboard API is unavailable. Returns whether the copy succeeded.
async function copyToClipboard(text) {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    try {
      const ta = document.createElement("textarea");
      ta.value = text;
      ta.style.position = "fixed";
      ta.style.opacity = "0";
      document.body.appendChild(ta);
      ta.select();
      const ok = document.execCommand("copy");
      document.body.removeChild(ta);
      return ok;
    } catch {
      return false;
    }
  }
}

// Wire a button to copy a fixed string, with the shared "✓ Copied" feedback for 1.5s.
function wireCopyBtn(btn, text) {
  if (!btn) return;
  const original = btn.textContent;
  btn.onclick = async () => {
    const ok = await copyToClipboard(text);
    btn.textContent = ok ? "✓ Copied" : "Copy failed";
    btn.classList.toggle("copied", ok);
    setTimeout(() => {
      btn.textContent = original;
      btn.classList.remove("copied");
    }, 1500);
  };
}

// Show the "Copy for agent" action and wire it to a freshly built report for this event.
function wireModalCopy(ev, group) {
  const btn = document.getElementById("modal-copy");
  btn.hidden = false;
  btn.textContent = "Copy for agent";
  btn.classList.remove("copied");
  btn.onclick = async () => {
    const ok = await copyToClipboard(buildEventReport(ev, group));
    btn.textContent = ok ? "✓ Copied" : "Copy failed";
    btn.classList.toggle("copied", ok);
    setTimeout(() => {
      btn.textContent = "Copy for agent";
      btn.classList.remove("copied");
    }, 1500);
  };
}

function openEventModal(ev, group, onResolve) {
  modalTitle.innerHTML = `<span class="badge ${levelClass(ev.level)}">${escapeHtml(ev.level || "-")}</span> ${escapeHtml(ev.message)}`;

  const groupRows = group
    ? [
        ...(group.kind ? [["Type", group.kind === "crash" ? "Crash" : "Error"]] : []),
        ["Occurrences", String(group.count)],
        ["Affected users", String(group.users)],
        ["First seen", fmtTime(group.firstSeen)],
        ["Last seen", fmtTime(group.lastSeen)],
        ...(group.resolved !== undefined
          ? [["Status", group.resolved
              ? "Resolved" + (group.resolvedAt ? " · " + fmtTime(group.resolvedAt) : "")
              : "Open"]]
          : []),
      ]
    : [];

  // A resolve/reopen action is available only when the caller passes a handler (diagnostics page).
  const resolveBtn = document.getElementById("modal-resolve");
  if (onResolve && group && group.key) {
    resolveBtn.hidden = false;
    resolveBtn.textContent = group.resolved ? "Reopen" : "Resolve";
    resolveBtn.onclick = async () => {
      await onResolve(group);
      closeModal();
    };
  } else {
    resolveBtn.hidden = true;
    resolveBtn.onclick = null;
  }

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
  wireModalCopy(ev, group);
  modalBackdrop.classList.add("open");
}

// Detail for a custom event: totals + a value breakdown per property key.
function openTrackEventModal(stat) {
  const resolveBtn = document.getElementById("modal-resolve");
  resolveBtn.hidden = true;
  resolveBtn.onclick = null;

  // The agent-copy action is for crashes/errors only; custom events have no stack to fix.
  const copyBtn = document.getElementById("modal-copy");
  copyBtn.hidden = true;
  copyBtn.onclick = null;

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

// Connection details for the MCP endpoint so an agent can read live crashes/errors.
async function openMcpModal() {
  // The copy/resolve actions belong to the event modal — hide them for this dialog.
  const copyBtn = document.getElementById("modal-copy");
  copyBtn.hidden = true;
  copyBtn.onclick = null;
  const resolveBtn = document.getElementById("modal-resolve");
  resolveBtn.hidden = true;
  resolveBtn.onclick = null;

  modalTitle.textContent = "Connect MCP";
  modalBody.innerHTML = `<p class="mcp-intro">Loading…</p>`;
  modalBackdrop.classList.add("open");

  let info;
  try {
    if (!cache.mcpInfo) cache.mcpInfo = await fetchJson("/api/mcp-info");
    info = cache.mcpInfo;
  } catch {
    modalBody.innerHTML = `<p class="mcp-intro">Couldn't load MCP connection details. Try Refresh.</p>`;
    return;
  }

  const intro = `<p class="mcp-intro">Let an AI agent (Claude Code, Cursor, …) read this server's live crashes and errors — the same data you see here — and fix them in your codebase.</p>`;

  if (!info.enabled) {
    modalBody.innerHTML = `${intro}
      <div class="mcp-note">
        The MCP endpoint is <strong>disabled</strong>. Set the <code>Mcp__Token</code> environment
        variable on the server to a long random secret and restart to enable it.
      </div>`;
    return;
  }

  const configJson = JSON.stringify(
    {
      mcpServers: {
        appstat: {
          type: "http",
          url: info.url,
          headers: { Authorization: `Bearer ${info.token}` },
        },
      },
    },
    null,
    2
  );

  modalBody.innerHTML = `${intro}
    <div class="mcp-field">
      <label>Endpoint URL</label>
      <div class="mcp-copy-row">
        <code>${escapeHtml(info.url)}</code>
        <button id="mcp-copy-url" type="button">Copy</button>
      </div>
    </div>
    <div class="mcp-field">
      <label>Access token</label>
      <div class="mcp-copy-row">
        <code>${escapeHtml(info.token)}</code>
        <button id="mcp-copy-token" class="primary" type="button">Copy token</button>
      </div>
    </div>
    <div class="mcp-field">
      <label>Or drop this into your <code>.mcp.json</code></label>
      <pre class="mcp-config">${escapeHtml(configJson)}</pre>
      <button id="mcp-copy-config" type="button">Copy config</button>
    </div>
    <p class="mcp-foot">Typical loop: <code>list_diagnostics</code> → <code>get_issue</code> → fix the code → <code>resolve_issue</code>.</p>`;

  wireCopyBtn(document.getElementById("mcp-copy-url"), info.url);
  wireCopyBtn(document.getElementById("mcp-copy-token"), info.token);
  wireCopyBtn(document.getElementById("mcp-copy-config"), configJson);
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
