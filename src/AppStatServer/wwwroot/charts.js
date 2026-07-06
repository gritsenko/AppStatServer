"use strict";

// ---------- shared formatting / helpers ----------
function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, (c) => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
  }[c]));
}

function levelClass(level) {
  const l = String(level ?? "").toLowerCase();
  return ["fatal", "error", "warning", "info", "debug"].includes(l) ? "lvl-" + l : "lvl-default";
}

function fmtTime(iso) {
  if (!iso) return "";
  const d = new Date(iso);
  if (isNaN(d)) return escapeHtml(iso);
  const p = (n) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
}

function fmtDuration(sec) {
  sec = Math.round(sec || 0);
  if (sec < 60) return sec + "s";
  if (sec < 3600) return Math.floor(sec / 60) + "m " + (sec % 60) + "s";
  return Math.floor(sec / 3600) + "h " + Math.floor((sec % 3600) / 60) + "m";
}

function fmtNum(n) {
  n = Math.round(n);
  return n >= 1000 ? (n / 1000).toFixed(n % 1000 ? 1 : 0) + "k" : "" + n;
}

function shortId(id) {
  const s = String(id ?? "");
  return s.length > 12 ? s.slice(0, 8) + "…" : s;
}

function timeAgo(iso) {
  const d = new Date(iso);
  if (isNaN(d)) return "";
  const s = Math.max(0, (Date.now() - d.getTime()) / 1000);
  if (s < 60) return "just now";
  if (s < 3600) return Math.floor(s / 60) + "m ago";
  if (s < 86400) return Math.floor(s / 3600) + "h ago";
  return Math.floor(s / 86400) + "d ago";
}

function niceCeil(v) {
  if (v <= 5) return 5;
  const p = Math.pow(10, Math.floor(Math.log10(v)));
  const f = v / p;
  const nf = f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10;
  return nf * p;
}

// ---------- vertical bar chart ----------
// Top-rounded bar: rounds the data end, keeps a square foot on the baseline.
function barPath(x, y, w, h, r) {
  r = Math.min(r, h, w / 2);
  return `M${x},${y + h} L${x},${y + r} Q${x},${y} ${x + r},${y} L${x + w - r},${y} Q${x + w},${y} ${x + w},${y + r} L${x + w},${y + h} Z`;
}

// items: [{ label, value }]
function renderBarChart(el, items) {
  if (!items || !items.length) {
    el.innerHTML = '<div class="empty">No data</div>';
    return;
  }
  const H = 200, padTop = 18, padBottom = 26, plotH = H - padTop - padBottom;
  const W = Math.max(el.clientWidth || 600, 120);
  const slot = W / items.length;
  const barW = Math.min(40, slot * 0.6);
  const max = Math.max(...items.map((d) => d.value), 1);
  const baseY = padTop + plotH;

  // Thin out x-axis labels when bars are packed, so dates don't overlap.
  const labelStep = Math.max(1, Math.ceil(items.length / 15));

  let marks = "";
  items.forEach((d, i) => {
    const h = d.value > 0 ? Math.max((d.value / max) * plotH, 2) : 0;
    const cx = i * slot + slot / 2;
    const x = cx - barW / 2;
    const y = baseY - h;
    marks += `<path class="bar" d="${barPath(x, y, barW, h, 4)}"><title>${escapeHtml(d.label)}: ${d.value}</title></path>`;
    if (i % labelStep === 0)
      marks += `<text class="axis-label" x="${cx}" y="${H - 10}" text-anchor="middle">${escapeHtml(d.label)}</text>`;
    if (d.value > 0)
      marks += `<text class="axis-label" x="${cx}" y="${y - 4}" text-anchor="middle">${d.value}</text>`;
  });

  el.innerHTML =
    `<svg class="barchart" viewBox="0 0 ${W} ${H}" preserveAspectRatio="none" role="img">` +
    `<line class="baseline" x1="0" y1="${baseY}" x2="${W}" y2="${baseY}"></line>${marks}</svg>`;
}

// ---------- horizontal bars (breakdown) ----------
// items: [{ key, count }]. opts.colorByLevel colors each row by its level.
function renderHBars(el, items, opts = {}) {
  if (!items || !items.length) {
    el.innerHTML = '<div class="empty">No data</div>';
    return;
  }
  const max = Math.max(...items.map((i) => i.count), 1);
  el.innerHTML = items
    .map((it) => {
      const cls = opts.colorByLevel ? levelClass(it.key) : "";
      const label = opts.colorByLevel
        ? `<span class="badge ${cls}">${escapeHtml(it.key)}</span>`
        : `<span class="hb-key" title="${escapeHtml(it.key)}">${escapeHtml(it.key)}</span>`;
      const pct = (it.count / max) * 100;
      return `<div class="row ${cls}">
        ${label}
        <div class="track"><div class="fill" style="width:${pct}%"></div></div>
        <span class="count">${it.count}</span>
      </div>`;
    })
    .join("");
}

// ---------- multi-series line chart with hover crosshair + tooltip ----------
// labels: ["yyyy-mm-dd", …]. series: [{ name, color, values:[…] }].
function renderLineChart(el, labels, series) {
  if (!labels || !labels.length) {
    el.innerHTML = '<div class="empty">No data</div>';
    return;
  }
  const W = Math.max(el.clientWidth || 600, 200);
  const H = 240, padL = 40, padR = 14, padT = 14, padB = 26;
  const plotW = W - padL - padR, plotH = H - padT - padB;
  const n = labels.length;

  const rawMax = Math.max(1, ...series.flatMap((s) => s.values));
  const niceMax = niceCeil(rawMax);
  const xAt = (i) => padL + (n === 1 ? plotW / 2 : (i / (n - 1)) * plotW);
  const yAt = (v) => padT + plotH - (v / niceMax) * plotH;
  const baseY = padT + plotH;

  let grid = "";
  [0, niceMax / 2, niceMax].forEach((t) => {
    const y = yAt(t);
    grid += `<line class="grid" x1="${padL}" y1="${y}" x2="${W - padR}" y2="${y}"></line>`;
    grid += `<text class="axis-label" x="${padL - 6}" y="${y + 3}" text-anchor="end">${fmtNum(t)}</text>`;
  });

  let xlabels = "";
  const step = Math.max(1, Math.ceil(n / 6));
  for (let i = 0; i < n; i += step)
    xlabels += `<text class="axis-label" x="${xAt(i)}" y="${H - 8}" text-anchor="middle">${escapeHtml(labels[i].slice(5))}</text>`;

  let paths = "";
  series.forEach((s) => {
    const pts = s.values.map((v, i) => `${xAt(i)},${yAt(v)}`).join(" ");
    paths += `<polyline fill="none" stroke="${s.color}" stroke-width="2" stroke-linejoin="round" stroke-linecap="round" points="${pts}"></polyline>`;
    s.values.forEach((v, i) => (paths += `<circle cx="${xAt(i)}" cy="${yAt(v)}" r="2.5" fill="${s.color}"></circle>`));
  });

  const svg =
    `<svg class="linechart" viewBox="0 0 ${W} ${H}" preserveAspectRatio="none" role="img">${grid}${xlabels}` +
    `<line class="axis" x1="${padL}" y1="${baseY}" x2="${W - padR}" y2="${baseY}"></line>${paths}` +
    `<line class="crosshair" x1="0" y1="${padT}" x2="0" y2="${baseY}" style="display:none"></line></svg>`;

  el.innerHTML = `<div class="chart-hoverwrap">${svg}<div class="tooltip" style="display:none"></div></div>`;

  const wrap = el.querySelector(".chart-hoverwrap");
  const svgEl = el.querySelector("svg");
  const tip = el.querySelector(".tooltip");
  const cross = el.querySelector(".crosshair");

  svgEl.addEventListener("pointermove", (ev) => {
    const rect = svgEl.getBoundingClientRect();
    const vbx = ((ev.clientX - rect.left) / rect.width) * W;
    let i = n === 1 ? 0 : Math.round(((vbx - padL) / plotW) * (n - 1));
    i = Math.max(0, Math.min(n - 1, i));
    const cx = xAt(i);
    cross.setAttribute("x1", cx);
    cross.setAttribute("x2", cx);
    cross.style.display = "";
    const rows = series
      .map((s) => `<div class="tt-row"><span class="dot" style="background:${s.color}"></span>${escapeHtml(s.name)}: <b>${s.values[i]}</b></div>`)
      .join("");
    tip.innerHTML = `<div class="tt-date">${escapeHtml(labels[i])}</div>${rows}`;
    tip.style.display = "";
    const wrapRect = wrap.getBoundingClientRect();
    let left = ev.clientX - wrapRect.left + 14;
    if (left + 170 > wrapRect.width) left = ev.clientX - wrapRect.left - 170;
    tip.style.left = Math.max(0, left) + "px";
  });
  svgEl.addEventListener("pointerleave", () => {
    tip.style.display = "none";
    cross.style.display = "none";
  });
}

// ---------- legend ----------
function legendHtml(series) {
  return (
    '<div class="legend">' +
    series.map((s) => `<span class="legend-item"><span class="dot" style="background:${s.color}"></span>${escapeHtml(s.name)}</span>`).join("") +
    "</div>"
  );
}
