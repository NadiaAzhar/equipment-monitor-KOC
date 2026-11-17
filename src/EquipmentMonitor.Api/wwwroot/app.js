const PAGE = 50;
const state = { site: '', equipment: null, rule: '', sensor: '', offset: 0, total: 0 };
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
  const { items } = await get(`/api/equipment${q}`);
  const tally = { ALARM: 0, WARNING: 0, NORMAL: 0 };
  items.forEach((e) => tally[e.status]++);
  $('summary').replaceChildren(...Object.entries(tally).map(([k, n]) => el('span', { className: `pill ${k}`, textContent: `${n} ${k}` })));
  $('cards').replaceChildren(...items.map(card));
  if (state.equipment && !items.some((e) => e.equipment_id === state.equipment)) {
    state.equipment = null;
    $('detail').hidden = true;
  }
}

function card(e) {
  const c = e.alert_counts;
  const btn = el('button', { className: `card ${e.status}-border`, type: 'button' },
    el('div', { className: 'top' },
      el('strong', { textContent: `${e.equipment_id} · ${e.description}` }),
      el('span', { className: `pill ${e.status}`, textContent: e.status })),
    el('div', { className: 'meta', textContent: `${e.site_name} · ${e.equipment_type} · criticality ${e.criticality}` }),
    el('div', { className: 'counts' },
      el('span', { textContent: `Alarm ${c.HIGH_ALARM}` }),
      el('span', { textContent: `Warning ${c.HIGH_WARNING}` }),
      el('span', { textContent: `Rolling avg ${c.ROLLING_AVERAGE_HIGH}` }),
      e.bad_reading_count ? el('span', { className: 'bad', textContent: `${e.bad_reading_count} BAD readings ignored` }) : ''));
  btn.setAttribute('aria-pressed', String(e.equipment_id === state.equipment));
  btn.onclick = () => selectEquipment(e.equipment_id).catch(showError);
  return btn;
}

async function selectEquipment(id) {
  Object.assign(state, { equipment: id, rule: '', sensor: '', offset: 0 });
  document.querySelectorAll('.card').forEach((b) => b.setAttribute('aria-pressed', String(b.textContent.startsWith(id + ' '))));
  const { equipment: e, sensors } = await get(`/api/equipment/${encodeURIComponent(id)}`);
  $('detail-title').textContent = `${e.equipment_id} · ${e.description} — ${e.status}`;
  $('limits').replaceChildren(
    el('thead', {}, el('tr', {}, ...['Sensor', 'Measures', 'Warning', 'Alarm', 'Rolling avg', 'Alerts', 'BAD'].map((h) => el('th', { textContent: h })))),
    el('tbody', {}, ...sensors.map((s) => el('tr', {},
      el('td', { textContent: s.sensor }), el('td', { textContent: s.measures }),
      el('td', { className: 'num', textContent: `${fmt(s.warning_high)} ${s.unit}` }),
      el('td', { className: 'num', textContent: `${fmt(s.alarm_high)} ${s.unit}` }),
      el('td', { className: 'num', textContent: `${fmt(s.rolling_avg_high)} ${s.unit}` }),
      el('td', { className: 'num', textContent: Object.values(s.alert_counts).reduce((a, b) => a + b, 0) }),
      el('td', { className: 'num', textContent: s.bad_reading_count })))));
  $('sensor').replaceChildren(el('option', { value: '', textContent: 'All sensors' }),
    ...sensors.map((s) => el('option', { value: s.sensor, textContent: s.sensor })));
  $('rule').value = '';
  $('detail').hidden = false;
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
    el('td', { textContent: a.sensor }),
    el('td', { className: 'num', textContent: `${fmt(a.value)} ${a.unit} ≥ ${fmt(a.limit)} ${a.unit}` +
      (a.original_unit !== a.unit ? ` (reported ${fmt(a.original_value)} ${a.original_unit})` : '') }),
    el('td', { textContent: a.message })))
    : [el('tr', {}, el('td', { colSpan: 5, textContent: 'No alerts — this machine stayed within its limits.' }))]));
  const pages = Math.max(1, Math.ceil(page.total / PAGE));
  $('page').textContent = `Page ${Math.floor(state.offset / PAGE) + 1} of ${pages}`;
  $('prev').disabled = state.offset === 0;
  $('next').disabled = state.offset + PAGE >= page.total;
}

$('site').onchange = (ev) => { state.site = ev.target.value; loadEquipment().catch(showError); };
$('rule').onchange = (ev) => { state.rule = ev.target.value; state.offset = 0; loadAlerts().catch(showError); };
$('sensor').onchange = (ev) => { state.sensor = ev.target.value; state.offset = 0; loadAlerts().catch(showError); };
$('prev').onclick = () => { state.offset = Math.max(0, state.offset - PAGE); loadAlerts().catch(showError); };
$('next').onclick = () => { state.offset += PAGE; loadAlerts().catch(showError); };

for (const r of ['HIGH_ALARM', 'HIGH_WARNING', 'ROLLING_AVERAGE_HIGH']) $('rule').append(el('option', { value: r, textContent: r }));
loadSites().then(loadEquipment).catch(showError);
