import { trendChart, limitBar, donut, hourBars, sparkline, heatmap } from './charts.js';

const PAGE = 50;
const state = { site: '', equipment: null, rule: '', sensor: '', offset: 0, total: 0, status: '', sort: 'status', items: [], hours: [], hourStart: null };
const $ = (id) => document.getElementById(id);

async function get(path) {
  const res = await fetch(path);
  const body = await res.json();
  if (!res.ok) throw new Error(body?.error?.message || `Request failed (${res.status})`);
  return body;
}

function showError(err) {
  $('error').textContent = err.message;
  $('error').hidden = false;
  setTimeout(() => ($('error').hidden = true), 5000);
}

function el(tag, props = {}, ...children) {
  const node = Object.assign(document.createElement(tag), props);
  node.append(...children);
  return node;
}

const fmt = (v) => Number(v).toLocaleString('en-US', { maximumFractionDigits: 3 });
const time = (iso) => iso.replace('T', ' ').slice(0, 16);

async function loadSites() {
  const { items } = await get('/api/sites');
  for (const s of items) $('site').append(el('option', { value: s.site_id, textContent: `${s.site_name} (${s.site_id})` }));
}

async function loadEquipment() {
  const q = state.site ? `?site_id=${encodeURIComponent(state.site)}` : '';
  const [{ items }, summary] = await Promise.all([get(`/api/equipment${q}`), get(`/api/alerts/summary${q}`)]);
  state.items = items;
  state.hours = summary.items;
  state.hourStart = hourStart(summary.items);
  renderFleet();
  if (state.equipment && !items.some((e) => e.equipment_id === state.equipment)) {
    state.equipment = null;
    $('detail').hidden = true;
  }
}

// The data covers one local day starting at 03:00 Kuwait time, so columns are hours since the first alert's day start.
function hourStart(rows) {
  if (!rows.length) return null;
  const first = rows.reduce((a, r) => (Date.parse(r.hour) < Date.parse(a.hour) ? r : a)).hour;
  const localHour = Number(first.slice(11, 13));
  return Date.parse(first) - ((localHour - 3 + 24) % 24) * 3600000;
}

const hourLabels = () => Array.from({ length: 24 }, (_, i) => String((3 + i) % 24).padStart(2, '0'));

function perHour(rows) {
  const cells = Array.from({ length: 24 }, () => ({ alarm: 0, warning: 0 }));
  for (const r of rows) {
    const i = Math.round((Date.parse(r.hour) - state.hourStart) / 3600000);
    if (i >= 0 && i < 24) { cells[i].alarm += r.alarm_count; cells[i].warning += r.warning_count; }
  }
  return cells;
}

const totalAlerts = (e) => Object.values(e.alert_counts).reduce((a, b) => a + b, 0);
const sorters = {
  status: () => 0,
  criticality: (a, b) => a.criticality.localeCompare(b.criticality),
  alerts: (a, b) => totalAlerts(b) - totalAlerts(a),
  id: (a, b) => a.equipment_id.localeCompare(b.equipment_id),
};

function renderFleet() {
  const tally = { ALARM: 0, WARNING: 0, NORMAL: 0 };
  state.items.forEach((e) => tally[e.status]++);
  $('summary').replaceChildren(...Object.entries(tally).map(([k, n]) => {
    const tile = el('button', { className: `stat ${k}`, type: 'button' }, el('b', { textContent: n }), el('span', { textContent: k }));
    tile.setAttribute('aria-pressed', String(state.status === k));
    tile.title = state.status === k ? 'Show all machines' : `Show only ${k} machines`;
    tile.onclick = () => { state.status = state.status === k ? '' : k; renderFleet(); };
    return tile;
  }));
  const shown = state.items.filter((e) => !state.status || e.status === state.status)
    .map((e, i) => ({ e, i })).sort((a, b) => sorters[state.sort](a.e, b.e) || a.i - b.i).map((x) => x.e);
  $('cards').replaceChildren(...shown.map(card));

  $('donut').replaceChildren(donut(tally), el('ul', { className: 'donut-legend' },
    ...['ALARM', 'WARNING', 'NORMAL'].map((k) => el('li', {}, el('i', { className: `sw ${k.toLowerCase()}` }), `${tally[k]} ${k.toLowerCase()}`))));
  const labels = hourLabels();
  const totals = perHour(state.hours);
  $('hour-bars').replaceChildren(hourBars(totals.map((c, i) => ({ label: labels[i], ...c }))));
  $('heatmap').replaceChildren(heatmap({
    hourLabels: labels,
    rows: state.items.map((e) => ({ id: e.equipment_id, label: e.equipment_id,
      cells: perHour(state.hours.filter((r) => r.equipment_id === e.equipment_id)) })),
    onSelect: (id) => selectEquipment(id).catch(showError),
  }));
}

async function loadQuality() {
  const r = await get('/api/import-report');
  const readings = r.files['sensor_readings.csv'] || { read: 0, loaded: 0, rejected: 0 };
  const rows = [['Readings loaded', readings.loaded], ['BAD kept, not alerted', r.bad_readings],
    ['Duplicates dropped', r.reject_reasons.DUPLICATE || 0], ['Other rejected', readings.rejected - (r.reject_reasons.DUPLICATE || 0)], ['Alerts raised', r.alerts]];
  $('quality').replaceChildren(...rows.flatMap(([k, v]) => [el('dt', { textContent: k }), el('dd', { textContent: fmt(v) })]));
}

function card(e) {
  const c = e.alert_counts;
  const counts = perHour(state.hours.filter((r) => r.equipment_id === e.equipment_id));
  const btn = el('button', { className: `card ${e.status}-border`, type: 'button' },
    el('div', { className: 'top' },
      el('strong', { textContent: `${e.equipment_id} · ${e.description}` }),
      el('span', { className: `pill ${e.status}`, textContent: e.status })),
    el('div', { className: 'meta', textContent: `${e.site_name} · ${e.equipment_type} · criticality ${e.criticality}` }),
    sparkline(counts.map((x) => x.alarm + x.warning), e.status),
    el('div', { className: 'counts' },
      el('span', { textContent: `Alarm ${c.HIGH_ALARM}` }),
      el('span', { textContent: `Warning ${c.HIGH_WARNING}` }),
      el('span', { textContent: `Rolling avg ${c.ROLLING_AVERAGE_HIGH}` }),
      e.bad_reading_count ? el('span', { className: 'bad', textContent: `${e.bad_reading_count} BAD readings ignored` }) : ''));
  btn.dataset.id = e.equipment_id;
  btn.setAttribute('aria-pressed', String(e.equipment_id === state.equipment));
  btn.onclick = () => selectEquipment(e.equipment_id).catch(showError);
  return btn;
}

async function loadTrends(id, sensors) {
  const results = await Promise.all(sensors.map(async (s) => {
    const q = `sensor=${s.sensor}&bucket_minutes=5`;
    const [readings, alerts] = await Promise.all([
      get(`/api/equipment/${encodeURIComponent(id)}/readings?${q}`),
      get(`/api/equipment/${encodeURIComponent(id)}/alerts?sensor=${s.sensor}&limit=500`)]);
    return { s, points: readings.items, alerts: alerts.items };
  }));
  if (state.equipment !== id) return;
  $('trends').replaceChildren(...results.map(({ s, points, alerts }) => {
    const panel = el('figure', { className: 'trend-panel' }, el('figcaption', { textContent: `${s.sensor} · ${s.measures} (${s.unit})` }));
    if (!points.length) panel.append(el('p', { className: 'empty', textContent: 'No readings.' }));
    else panel.append(trendChart({ points, alerts, unit: s.unit, bucketMinutes: 5,
      offsetMinutes: Number(points[0].timestamp.slice(-6, -3)) * 60,
      limits: { warning: s.warning_high, alarm: s.alarm_high, rolling: s.rolling_avg_high } }));
    const peak = Math.max(...points.map((p) => p.max ?? -Infinity));
    const cell = document.querySelector(`[data-peak="${s.sensor}"]`);
    if (cell && Number.isFinite(peak)) cell.replaceChildren(limitBar({ value: peak, warning: s.warning_high, alarm: s.alarm_high, unit: s.unit }));
    return panel;
  }));
}

async function selectEquipment(id) {
  Object.assign(state, { equipment: id, rule: '', sensor: '', offset: 0 });
  document.querySelectorAll('.card').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.id === id)));
  const { equipment: e, sensors } = await get(`/api/equipment/${encodeURIComponent(id)}`);
  $('detail-title').replaceChildren(el('span', { textContent: `${e.equipment_id} · ${e.description}` }),
    el('span', { className: `pill ${e.status}`, textContent: e.status }));
  $('limits').replaceChildren(
    el('thead', {}, el('tr', {}, ...['Sensor', 'Measures', 'Warning', 'Alarm', 'Rolling avg', 'Peak', 'Alerts', 'BAD'].map((h) => el('th', { textContent: h })))),
    el('tbody', {}, ...sensors.map((s) => el('tr', {},
      el('td', {}, el('span', { className: 'chip', textContent: s.sensor })), el('td', { textContent: s.measures }),
      el('td', { className: 'num lim-warning', textContent: `${fmt(s.warning_high)} ${s.unit}` }),
      el('td', { className: 'num lim-alarm', textContent: `${fmt(s.alarm_high)} ${s.unit}` }),
      el('td', { className: 'num', textContent: `${fmt(s.rolling_avg_high)} ${s.unit}` }),
      el('td', { className: 'peak', textContent: '…' }),
      el('td', { className: 'num', textContent: Object.values(s.alert_counts).reduce((a, b) => a + b, 0) }),
      el('td', { className: 'num', textContent: s.bad_reading_count })))));
  $('sensor').replaceChildren(el('option', { value: '', textContent: 'All sensors' }),
    ...sensors.map((s) => el('option', { value: s.sensor, textContent: s.sensor })));
  $('rule').value = '';
  $('trends').replaceChildren(el('p', { className: 'empty', textContent: 'Loading trends…' }));
  $('detail').hidden = false;
  document.querySelectorAll('#limits tbody tr').forEach((tr, i) => { tr.cells[5].dataset.peak = sensors[i].sensor; });
  loadTrends(id, sensors).catch(showError);
  await loadAlerts();
}

async function loadAlerts() {
  const p = new URLSearchParams({ limit: PAGE, offset: state.offset });
  if (state.rule) p.set('rule', state.rule);
  if (state.sensor) p.set('sensor', state.sensor);
  const page = await get(`/api/equipment/${encodeURIComponent(state.equipment)}/alerts?${p}`);
  state.total = page.total;
  $('alert-total').textContent = `${page.total} alert${page.total === 1 ? '' : 's'}`;
  $('alerts').tBodies[0].replaceChildren(...(page.items.length ? page.items.map((a) => el('tr', {},
    el('td', { className: 'num', textContent: time(a.timestamp) }),
    el('td', {}, el('span', { className: `pill ${a.severity}`, textContent: a.rule_id })),
    el('td', {}, el('span', { className: 'chip', textContent: a.sensor })),
    el('td', { className: 'num' }, el('strong', { className: `val-${a.severity}`, textContent: `${fmt(a.value)} ${a.unit}` }),
      el('span', { className: 'limit', textContent: ` ≥ ${fmt(a.limit)} ${a.unit}` }),
      a.original_unit !== a.unit ? el('div', { className: 'limit', textContent: `reported ${fmt(a.original_value)} ${a.original_unit}` }) : ''),
    el('td', { className: 'msg', textContent: a.message })))
    : [el('tr', {}, el('td', { colSpan: 5, textContent: 'No alerts — this machine stayed within its limits.' }))]));
  const pages = Math.max(1, Math.ceil(page.total / PAGE));
  $('page').textContent = `Page ${Math.floor(state.offset / PAGE) + 1} of ${pages}`;
  $('prev').disabled = state.offset === 0;
  $('next').disabled = state.offset + PAGE >= page.total;
}

$('sort').onchange = (ev) => { state.sort = ev.target.value; renderFleet(); };
$('site').onchange = (ev) => { state.site = ev.target.value; loadEquipment().catch(showError); };
$('rule').onchange = (ev) => { state.rule = ev.target.value; state.offset = 0; loadAlerts().catch(showError); };
$('sensor').onchange = (ev) => { state.sensor = ev.target.value; state.offset = 0; loadAlerts().catch(showError); };
$('prev').onclick = () => { state.offset = Math.max(0, state.offset - PAGE); loadAlerts().catch(showError); };
$('next').onclick = () => { state.offset += PAGE; loadAlerts().catch(showError); };

for (const r of ['HIGH_ALARM', 'HIGH_WARNING', 'ROLLING_AVERAGE_HIGH']) $('rule').append(el('option', { value: r, textContent: r }));
loadSites().then(loadEquipment).catch(showError);
loadQuality().catch(showError);
