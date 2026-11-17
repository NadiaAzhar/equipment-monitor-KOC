// Small dependency-free SVG chart helpers for the dashboard.
const NS = 'http://www.w3.org/2000/svg';
const COLORS = { alarm: '#c4261d', warning: '#a35a00', normal: '#1b7a3d', line: '#0b5cd5', grid: '#e1e5ea', muted: '#5b6577' };

function svg(tag, attrs = {}, ...children) {
  const node = document.createElementNS(NS, tag);
  for (const [k, v] of Object.entries(attrs)) node.setAttribute(k, v);
  node.append(...children);
  return node;
}

const text = (x, y, content, attrs = {}) => {
  const t = svg('text', { x, y, 'font-size': 11, fill: COLORS.muted, ...attrs });
  t.textContent = content;
  return t;
};

const num = (v) => Number(v).toLocaleString('en-US', { maximumFractionDigits: 2 });
const clock = (ms, offsetMinutes) => {
  const d = new Date(ms + offsetMinutes * 60000);
  return `${String(d.getUTCHours()).padStart(2, '0')}:${String(d.getUTCMinutes()).padStart(2, '0')}`;
};

function tooltip(host) {
  const tip = document.createElement('div');
  tip.className = 'chart-tip';
  tip.hidden = true;
  host.append(tip);
  return {
    show(html, ev) {
      const box = host.getBoundingClientRect();
      tip.textContent = '';
      tip.append(...html);
      tip.hidden = false;
      tip.style.left = `${Math.min(ev.clientX - box.left + 12, box.width - tip.offsetWidth - 4)}px`;
      tip.style.top = `${Math.max(ev.clientY - box.top - tip.offsetHeight - 10, 0)}px`;
    },
    hide() { tip.hidden = true; },
  };
}

/**
 * Trend chart for one sensor. points: API reading buckets; limits: {warning, alarm, rolling}; alerts: API alerts.
 * Gaps (missing buckets) break the line; buckets with only BAD readings show a grey tick on the axis.
 */
export function trendChart({ points, limits, alerts, unit, bucketMinutes, offsetMinutes }) {
  const W = 480, H = 230, m = { l: 40, r: 10, t: 10, b: 24 };
  const times = points.map((p) => Date.parse(p.timestamp));
  const t0 = Math.min(...times), t1 = Math.max(...times);
  const good = points.filter((p) => p.max != null);
  // Zoom the Y axis to the data and the limits instead of starting at zero, so small movements stay visible.
  const lo = Math.min(...good.map((p) => p.min), limits.rolling, limits.warning);
  const hi = Math.max(limits.alarm * 1.04, ...good.map((p) => p.max));
  const pad = (hi - lo) * 0.1;
  const yMin = lo >= 0 ? Math.max(0, lo - pad) : lo - pad, yMax = hi + pad;
  const x = (t) => m.l + ((t - t0) / (t1 - t0 || 1)) * (W - m.l - m.r);
  const y = (v) => H - m.b - ((v - yMin) / (yMax - yMin)) * (H - m.t - m.b);

  const root = svg('svg', { viewBox: `0 0 ${W} ${H}`, role: 'img', class: 'trend',
    'aria-label': `Trend of ${unit} readings with warning ${num(limits.warning)} and alarm ${num(limits.alarm)} limits` });
  root.append(
    svg('rect', { x: m.l, y: m.t, width: W - m.l - m.r, height: y(limits.alarm) - m.t, fill: COLORS.alarm, opacity: .07 }),
    svg('rect', { x: m.l, y: y(limits.alarm), width: W - m.l - m.r, height: y(limits.warning) - y(limits.alarm), fill: COLORS.warning, opacity: .08 }));

  const ticks = 4;
  for (let i = 0; i <= ticks; i++) {
    const v = yMin + ((yMax - yMin) / ticks) * i;
    root.append(svg('line', { x1: m.l, x2: W - m.r, y1: y(v), y2: y(v), stroke: COLORS.grid }), text(m.l - 6, y(v) + 4, num(v), { 'text-anchor': 'end' }));
  }
  const stepMs = Math.max(1, Math.ceil((t1 - t0) / 3600000 / 8)) * 3600000;
  for (let t = t0; t <= t1; t += stepMs) {
    root.append(svg('line', { x1: x(t), x2: x(t), y1: H - m.b, y2: H - m.b + 4, stroke: COLORS.muted }), text(x(t), H - 6, clock(t, offsetMinutes), { 'text-anchor': 'middle' }));
  }

  // Rolling average sits below the warning line in practice, so its label goes under its line; the others go above.
  for (const [label, v, color, below] of [['Alarm', limits.alarm, COLORS.alarm, false], ['Warning', limits.warning, COLORS.warning, false], ['Rolling avg', limits.rolling, '#6f3fc4', true]]) {
    root.append(svg('line', { x1: m.l, x2: W - m.r, y1: y(v), y2: y(v), stroke: color, 'stroke-dasharray': '5 4', 'stroke-width': 1.2 }),
      text(W - m.r - 2, y(v) + (below ? 12 : -3), `${label} ${num(v)}`, { 'text-anchor': 'end', fill: color, 'font-weight': 600 }));
  }

  // One path per continuous run; a missing bucket or a bucket with only BAD readings ends the run.
  const gap = bucketMinutes * 60000 * 1.5;
  let band = '', line = '', prev = null;
  const band2 = [];
  for (const p of points) {
    const t = Date.parse(p.timestamp);
    if (p.value == null) { prev = null; continue; }
    if (prev === null || t - prev > gap) {
      if (band2.length) { band += bandPath(band2, x, y); band2.length = 0; }
      line += `M${x(t).toFixed(1)},${y(p.value).toFixed(1)}`;
    } else line += `L${x(t).toFixed(1)},${y(p.value).toFixed(1)}`;
    band2.push({ t, min: p.min, max: p.max });
    prev = t;
  }
  if (band2.length) band += bandPath(band2, x, y);
  root.append(svg('path', { d: band, fill: COLORS.line, opacity: .15 }), svg('path', { d: line, fill: 'none', stroke: COLORS.line, 'stroke-width': 1.6, 'stroke-linejoin': 'round' }));

  for (const p of points.filter((q) => q.bad_count > 0)) {
    root.append(svg('line', { x1: x(Date.parse(p.timestamp)), x2: x(Date.parse(p.timestamp)), y1: H - m.b - 8, y2: H - m.b, stroke: '#6f3fc4', 'stroke-width': 2 }));
  }
  for (const a of alerts) {
    const t = Date.parse(a.timestamp);
    if (t < t0 || t > t1) continue;
    root.append(svg('circle', { cx: x(t), cy: y(a.value), r: 3, fill: a.severity === 'ALARM' ? COLORS.alarm : COLORS.warning, stroke: '#fff', 'stroke-width': 1 }));
  }

  const cursor = svg('line', { y1: m.t, y2: H - m.b, stroke: COLORS.muted, 'stroke-dasharray': '2 3', visibility: 'hidden' });
  const overlay = svg('rect', { x: m.l, y: m.t, width: W - m.l - m.r, height: H - m.t - m.b, fill: 'transparent' });
  root.append(cursor, overlay);

  const host = document.createElement('div');
  host.className = 'chart';
  host.append(root);
  const tip = tooltip(host);
  overlay.addEventListener('mousemove', (ev) => {
    const r = root.getBoundingClientRect();
    const t = t0 + (((ev.clientX - r.left) / r.width) * W - m.l) / (W - m.l - m.r) * (t1 - t0);
    const p = points.reduce((best, q) => (Math.abs(Date.parse(q.timestamp) - t) < Math.abs(Date.parse(best.timestamp) - t) ? q : best));
    const px = x(Date.parse(p.timestamp));
    cursor.setAttribute('x1', px); cursor.setAttribute('x2', px); cursor.removeAttribute('visibility');
    const body = [document.createTextNode(clock(Date.parse(p.timestamp), offsetMinutes) + '  '),
      el('strong', p.value == null ? 'no GOOD reading' : `${num(p.value)} ${unit}`)];
    if (p.value != null && p.min !== p.max) body.push(el('span', ` (${num(p.min)}–${num(p.max)})`));
    if (p.bad_count) body.push(el('span', ` · ${p.bad_count} BAD ignored`));
    tip.show(body, ev);
  });
  overlay.addEventListener('mouseleave', () => { tip.hide(); cursor.setAttribute('visibility', 'hidden'); });
  return host;
}

function bandPath(run, x, y) {
  const up = run.map((p) => `${x(p.t).toFixed(1)},${y(p.max).toFixed(1)}`);
  const down = run.map((p) => `${x(p.t).toFixed(1)},${y(p.min).toFixed(1)}`).reverse();
  return `M${up.join('L')}L${down.join('L')}Z`;
}

function el(tag, content) {
  const n = document.createElement(tag);
  n.textContent = content;
  return n;
}

/** Horizontal bar showing where a value sits against the warning and alarm limits. */
export function limitBar({ value, warning, alarm, unit }) {
  const max = Math.max(alarm * 1.2, value * 1.05);
  const pct = (v) => `${(Math.min(v, max) / max) * 100}%`;
  const level = value >= alarm ? 'alarm' : value >= warning ? 'warning' : 'normal';
  const root = document.createElement('div');
  root.className = 'limit-bar';
  root.title = `Peak ${num(value)} ${unit} (warning ${num(warning)}, alarm ${num(alarm)})`;
  const track = document.createElement('div');
  track.className = 'track';
  track.style.background = `linear-gradient(90deg, #e7f5ec ${pct(warning)}, #fff4e0 ${pct(warning)} ${pct(alarm)}, #fdecea ${pct(alarm)})`;
  const fill = document.createElement('div');
  fill.className = `fill ${level}`;
  fill.style.width = pct(value);
  track.append(fill);
  const label = el('span', `${num(value)} ${unit}`);
  label.className = `val-${level.toUpperCase()}`;
  root.append(track, label);
  return root;
}

/** Donut of status counts. tally: {ALARM, WARNING, NORMAL}. */
export function donut(tally) {
  const total = Object.values(tally).reduce((a, b) => a + b, 0);
  const R = 42, C = 2 * Math.PI * R;
  const root = svg('svg', { viewBox: '0 0 120 120', class: 'donut', role: 'img',
    'aria-label': `${total} machines: ${tally.ALARM} alarm, ${tally.WARNING} warning, ${tally.NORMAL} normal` });
  root.append(svg('circle', { cx: 60, cy: 60, r: R, fill: 'none', stroke: COLORS.grid, 'stroke-width': 16 }));
  let offset = 0;
  for (const [k, color] of [['ALARM', COLORS.alarm], ['WARNING', COLORS.warning], ['NORMAL', COLORS.normal]]) {
    if (!tally[k]) continue;
    const len = (tally[k] / total) * C;
    root.append(svg('circle', { cx: 60, cy: 60, r: R, fill: 'none', stroke: color, 'stroke-width': 16,
      'stroke-dasharray': `${len} ${C - len}`, 'stroke-dashoffset': -offset, transform: 'rotate(-90 60 60)' }));
    offset += len;
  }
  root.append(text(60, 62, total, { 'text-anchor': 'middle', 'font-size': 24, 'font-weight': 700, fill: '#172033' }),
    text(60, 78, 'machines', { 'text-anchor': 'middle' }));
  return root;
}

/** Stacked bars: one column per hour. hours: [{label, alarm, warning}] */
export function hourBars(hours) {
  const W = 400, H = 150, m = { l: 26, r: 4, t: 6, b: 18 };
  const max = Math.max(1, ...hours.map((h) => h.alarm + h.warning));
  const bw = (W - m.l - m.r) / hours.length;
  const y = (v) => H - m.b - (v / max) * (H - m.t - m.b);
  const root = svg('svg', { viewBox: `0 0 ${W} ${H}`, class: 'hour-bars', role: 'img', 'aria-label': 'Alerts per hour' });
  root.append(svg('line', { x1: m.l, x2: W - m.r, y1: y(0), y2: y(0), stroke: COLORS.grid }),
    svg('line', { x1: m.l, x2: W - m.r, y1: y(max), y2: y(max), stroke: COLORS.grid }), text(m.l - 4, y(max) + 4, max, { 'text-anchor': 'end' }), text(m.l - 4, y(0), 0, { 'text-anchor': 'end' }));
  hours.forEach((h, i) => {
    const x = m.l + i * bw + 1.5;
    const bar = svg('g');
    const title = svg('title');
    title.textContent = `${h.label}: ${h.alarm} alarm, ${h.warning} warning`;
    bar.append(title,
      svg('rect', { x, y: y(h.alarm), width: bw - 3, height: y(0) - y(h.alarm), fill: COLORS.alarm }),
      svg('rect', { x, y: y(h.alarm + h.warning), width: bw - 3, height: y(h.alarm) - y(h.alarm + h.warning), fill: COLORS.warning }));
    root.append(bar);
    if (i % 3 === 0) root.append(text(x + (bw - 3) / 2, H - 4, h.label, { 'text-anchor': 'middle', 'font-size': 10 }));
  });
  return root;
}

/** Tiny alerts-per-hour sparkline for a card. counts: number[] */
export function sparkline(counts, severity) {
  const W = 120, H = 24, max = Math.max(1, ...counts);
  const bw = W / counts.length;
  const root = svg('svg', { viewBox: `0 0 ${W} ${H}`, class: 'spark', role: 'img', 'aria-label': `Alerts per hour, peak ${Math.max(...counts)}` });
  counts.forEach((c, i) => {
    const h = c ? Math.max(2, (c / max) * (H - 2)) : 1;
    root.append(svg('rect', { x: i * bw + .5, y: H - h, width: bw - 1, height: h, fill: c ? (severity === 'ALARM' ? COLORS.alarm : COLORS.warning) : COLORS.grid, rx: 1 }));
  });
  return root;
}

/** Equipment x hour heatmap. rows: [{id, label, cells:[{alarm, warning, label}]}], onSelect(id). */
export function heatmap({ rows, hourLabels, onSelect }) {
  const table = document.createElement('table');
  table.className = 'heatmap';
  const head = table.createTHead().insertRow();
  head.append(document.createElement('th'));
  hourLabels.forEach((l, i) => { const th = document.createElement('th'); th.textContent = i % 3 === 0 ? l : ''; head.append(th); });
  const body = table.createTBody();
  for (const row of rows) {
    const tr = body.insertRow();
    const name = document.createElement('th');
    name.scope = 'row';
    const btn = el('button', row.label);
    btn.type = 'button';
    btn.onclick = () => onSelect(row.id);
    name.append(btn);
    tr.append(name);
    row.cells.forEach((c, i) => {
      const td = tr.insertCell();
      td.className = c.alarm ? 'h-alarm' : c.warning ? 'h-warning' : 'h-none';
      td.title = `${row.id} ${hourLabels[i]}: ${c.alarm} alarm, ${c.warning} warning`;
    });
  }
  return table;
}
