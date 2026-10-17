import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';

const base = process.env.HENGCHUAN_API ?? 'http://127.0.0.1:5080';
if (!/^http:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/.test(base)) throw new Error('Acceptance is restricted to loopback.');
const results = [], resources = { screens: new Set(), indicators: new Set(), businesses: new Set() };
const tag = randomUUID().slice(0, 8);
let meta, checkout, ownBusiness, ownIndicator, ownScreen;
async function api(path, { method = 'GET', body, expected = [200], write = true, headers = {} } = {}) {
  const response = await fetch(base + path, { method, headers: { ...(body === undefined ? {} : { 'Content-Type': 'application/json' }), ...(method !== 'GET' && write ? { 'X-Hengchuan-Write': '1' } : {}), ...headers }, ...(body === undefined ? {} : { body: JSON.stringify(body) }), signal: AbortSignal.timeout(30_000) });
  const raw = await response.text();
  let data;
  try { data = raw ? JSON.parse(raw) : undefined; } catch { throw new Error(`${path}: invalid JSON response (${response.status})`); }
  assert.ok(expected === '4xx' ? response.status >= 400 && response.status < 500 : expected.includes(response.status), `${path}: HTTP ${response.status}, ${data?.message ?? raw.slice(0, 160)}`);
  if (response.status >= 400) assert.ok(data?.code && data?.message, `${path}: HTTP ${response.status} errors require the published code/message JSON contract`);
  return { status: response.status, data };
}
const revision = async () => (await api('/api/meta')).data.revision;
async function create(kind, data) {
  const result = (await api(`/api/${kind}`, { method: 'POST', body: { revision: await revision(), data }, expected: [200, 201] })).data;
  assert.ok(Number.isInteger(result.revision) && result.data.id); resources[kind]?.add(result.data.id); return result;
}
async function update(kind, id, data) { return (await api(`/api/${kind}/${id}`, { method: 'PUT', body: { revision: await revision(), data } })).data; }
async function remove(kind, id) {
  const response = (await api(`/api/${kind}/${id}?revision=${await revision()}`, { method: 'DELETE' })).data;
  assert.equal(response.deletedId, id); resources[kind]?.delete(id); return response;
}
async function check(name, work) {
  try { await work(); results.push({ name, status: 'passed' }); console.log(`PASS ${name}`); }
  catch (error) { results.push({ name, status: 'failed', error: error.message }); console.error(`FAIL ${name}: ${error.message}`); }
}
function close(a, b, label) { assert.ok(Math.abs(a - b) <= Math.max(0.001, Math.abs(b) * 0.00001), `${label}: ${a} differs from ${b}`); }
function editableBusiness(b, overrides = {}) { return { spaceId: b.spaceId, name: b.name, owner: b.owner, description: b.description, service: b.service, environment: b.environment, targetAvailability: b.targetAvailability, ...overrides }; }
function editableIndicator(i, overrides = {}) { return { spaceId: i.spaceId, name: i.name, sourceId: i.sourceId, sourceName: i.sourceName, metricName: i.metricName, unit: i.unit, owner: i.owner, visibility: i.visibility, enabled: i.enabled, businessIds: i.businessIds, behavior: i.behavior, ...overrides }; }
const query = values => new URLSearchParams(Object.entries(values).filter(([, v]) => v !== undefined).map(([k, v]) => [k, String(v)])).toString();

await check('health, demo source boundaries and seed spaces', async () => {
  const health = (await api('/api/health')).data; assert.equal(health.status, 'ok'); assert.equal(health.mode, 'demo'); assert.equal(health.product, '衡川观测');
  meta = (await api('/api/meta')).data; assert.equal(meta.mode, 'demo'); assert.ok(meta.spaces.length >= 3); assert.ok(Number.isInteger(meta.revision));
  assert.ok(meta.sourceStatus.some(s => s.id === 'demo')); assert.ok(meta.sourceStatus.some(s => /oracle/i.test(s.id) && /unconnected|disabled|未/.test(s.state + s.detail)));
  checkout = (await api('/api/businesses/biz-checkout')).data; assert.equal(checkout.data.id, 'biz-checkout'); assert.ok(checkout.indicators.length >= 13);
});
if (!meta || meta.mode !== 'demo' || !checkout) { await persist(); process.exit(1); }
const spaceId = checkout.data.spaceId, otherSpaceId = meta.spaces.find(s => s.id !== spaceId).id;
await check('space overview and combined indicator filtering stay scoped', async () => {
  for (const space of meta.spaces) {
    const overview = (await api(`/api/overview?${query({ spaceId: space.id })}`)).data;
    assert.ok(overview.items.every(b => b.spaceId === space.id)); assert.equal(overview.window.timezone, 'UTC');
    const indicators = (await api(`/api/indicators?${query({ spaceId: space.id, page: 1, pageSize: 100 })}`)).data;
    assert.ok(indicators.items.every(i => i.spaceId === space.id));
  }
  await api(`/api/indicators?${query({ spaceId: otherSpaceId, businessId: 'biz-checkout' })}`, { expected: '4xx' });
  const filtered = (await api(`/api/indicators?${query({ spaceId, sourceId: 'demo', enabled: true, businessId: 'biz-checkout', page: 1, pageSize: 100 })}`)).data;
  assert.ok(filtered.items.every(i => i.spaceId === spaceId && i.sourceId === 'demo' && i.enabled && i.businessIds.includes('biz-checkout')));
});
await check('business create, search, edit, persisted reread and revision conflict', async () => {
  const input = { spaceId, name: `验收业务-${tag}`, owner: '合成验收', description: '仅用于隔离合成验收的业务描述。', service: `synthetic-${tag}`, environment: 'demo', targetAvailability: 99.9 };
  const guardRevision = await revision();
  await api('/api/businesses', { method: 'POST', body: { revision: guardRevision, data: { ...input, description: '<script>window.__HENGCHUAN_INJECTED__=true</script>' } }, expected: [400] }); assert.equal(await revision(), guardRevision);
  const created = await create('businesses', input); ownBusiness = created.data; assert.match(ownBusiness.id, /^b-/);
  const exact = (await api(`/api/overview?${query({ spaceId, search: tag })}`)).data; assert.ok(exact.items.some(b => b.id === ownBusiness.id));
  assert.ok((await api(`/api/overview?${query({ spaceId: otherSpaceId, search: tag })}`)).data.items.every(b => b.id !== ownBusiness.id));
  const changed = editableBusiness(ownBusiness, { name: `已更新业务-${tag}`, targetAvailability: 99.95 });
  ownBusiness = (await update('businesses', ownBusiness.id, changed)).data;
  const reread = (await api(`/api/businesses/${ownBusiness.id}`)).data; assert.equal(reread.data.name, changed.name); assert.equal(reread.data.description, input.description);
  const current = await revision();
  await api(`/api/businesses/${ownBusiness.id}`, { method: 'PUT', body: { revision: created.revision, data: editableBusiness(ownBusiness, { name: `陈旧写入-${tag}` }) }, expected: [409] });
  assert.equal(await revision(), current); assert.equal((await api(`/api/businesses/${ownBusiness.id}`)).data.data.name, changed.name);
});
await check('write guard, invalid origin and invalid payload do not change state', async () => {
  assert.ok(ownBusiness); const before = await revision(); const data = editableBusiness(ownBusiness, { name: `拒绝修改-${tag}` });
  await api(`/api/businesses/${ownBusiness.id}`, { method: 'PUT', body: { revision: before, data }, write: false, expected: '4xx' });
  await api(`/api/businesses/${ownBusiness.id}`, { method: 'PUT', body: { revision: before, data }, headers: { Origin: 'https://example.invalid' }, expected: '4xx' });
  await api(`/api/businesses/${ownBusiness.id}`, { method: 'PUT', body: { revision: before, data: { ...data, targetAvailability: 101 } }, expected: '4xx' });
  assert.equal(await revision(), before);
});
await check('thirteen-chart drilldown, real empty state, raw aggregation and statistics', async () => {
  const to = new Date(meta.now).toISOString(), from = new Date(Date.parse(to) - 6 * 3600_000).toISOString();
  const dashboard = (await api(`/api/businesses/biz-checkout/dashboard?${query({ from, to, aggregation: 'avg', comparison: 'yesterday' })}`)).data;
  assert.equal(dashboard.charts.length, 13); assert.equal(dashboard.mode, 'demo'); assert.equal(dashboard.window.timezone, 'UTC'); assert.equal(dashboard.comparison, 'yesterday');
  const empty = dashboard.charts.find(c => c.indicatorId === 'ind-empty'); assert.ok(empty && empty.points.length === 0 && empty.emptyReason);
  for (const key of ['sum', 'max', 'min', 'average']) assert.equal(empty.stats[key], null);
  for (const chart of dashboard.charts.filter(c => c.points.length)) {
    const values = chart.points.map(p => p.value); assert.ok(values.every(Number.isFinite));
    close(chart.stats.sum, values.reduce((a, b) => a + b, 0), 'sum'); close(chart.stats.max, Math.max(...values), 'max'); close(chart.stats.min, Math.min(...values), 'min'); close(chart.stats.average, values.reduce((a, b) => a + b, 0) / values.length, 'average');
    assert.ok(chart.points.some(p => Number.isFinite(p.comparisonValue)));
  }
  const raw = (await api(`/api/businesses/biz-checkout/dashboard?${query({ from, to, aggregation: 'raw', comparison: 'none' })}`)).data;
  const sum = (await api(`/api/businesses/biz-checkout/dashboard?${query({ from, to, aggregation: 'sum', comparison: 'lastWeek' })}`)).data;
  const normal = dashboard.charts.find(c => c.points.length); assert.ok(raw.charts.find(c => c.indicatorId === normal.indicatorId).points.length >= normal.points.length);
  assert.equal(sum.aggregation, 'sum'); assert.equal(sum.comparison, 'lastWeek');
  await api('/api/businesses/biz-checkout/dashboard?from=乱码&to=乱码', { expected: '4xx' });
  await api(`/api/businesses/biz-checkout/dashboard?${query({ from: to, to: from })}`, { expected: '4xx' });
});
await check('indicator creation, pagination, edit and cross-space references', async () => {
  const input = { spaceId, name: `验收指标-${tag}`, sourceId: 'demo', sourceName: '合成验收', metricName: `synthetic.${tag}`, unit: 'ms', owner: '合成验收', visibility: 'space', enabled: true, businessIds: [], behavior: 'normal' };
  ownIndicator = (await create('indicators', input)).data;
  const filtered = (await api(`/api/indicators?${query({ spaceId, search: tag, sourceName: '合成验收', unreferenced: true, page: 1, pageSize: 1 })}`)).data;
  assert.equal(filtered.total, 1); assert.equal(filtered.items[0].id, ownIndicator.id); assert.equal(filtered.pageSize, 1);
  const second = (await api(`/api/indicators?${query({ spaceId, search: tag, page: 2, pageSize: 1 })}`)).data; assert.equal(second.total, 1); assert.equal(second.items.length, 0);
  ownIndicator = (await update('indicators', ownIndicator.id, editableIndicator(ownIndicator, { enabled: false, behavior: 'empty' }))).data;
  assert.equal((await api(`/api/indicators?${query({ spaceId, search: tag, enabled: false })}`)).data.items[0].behavior, 'empty');
  const rev = await revision();
  await api(`/api/indicators/${ownIndicator.id}`, { method: 'PUT', body: { revision: rev, data: editableIndicator(ownIndicator, { spaceId: otherSpaceId, businessIds: ['biz-checkout'] }) }, expected: '4xx' }); assert.equal(await revision(), rev);
});
await check('indicator batch enable is atomic, scoped and revision guarded', async () => {
  assert.ok(ownIndicator);
  const second = (await create('indicators', editableIndicator(ownIndicator, { name: `验收批量-${tag}`, metricName: `synthetic.batch.${tag}`, enabled: false }))).data;
  const before = await revision();
  await api('/api/indicators/batch', { method: 'POST', body: { revision: before, spaceId, ids: [ownIndicator.id, 'i-nonexistent-synthetic'], enabled: true }, expected: '4xx' });
  assert.equal(await revision(), before); assert.ok((await api(`/api/indicators?${query({ spaceId, search: tag })}`)).data.items.filter(i => [ownIndicator.id, second.id].includes(i.id)).every(i => !i.enabled));
  await api('/api/indicators/batch', { method: 'POST', body: { revision: before, spaceId: otherSpaceId, ids: [ownIndicator.id, second.id], enabled: true }, expected: '4xx' }); assert.equal(await revision(), before);
  const result = (await api('/api/indicators/batch', { method: 'POST', body: { revision: before, spaceId, ids: [ownIndicator.id, second.id], enabled: true } })).data;
  assert.equal(result.updatedCount, 2); assert.equal(result.revision, before + 1);
  assert.ok((await api(`/api/indicators?${query({ spaceId, search: tag })}`)).data.items.filter(i => [ownIndicator.id, second.id].includes(i.id)).every(i => i.enabled));
  await api('/api/indicators/batch', { method: 'POST', body: { revision: before, spaceId, ids: [ownIndicator.id, second.id], enabled: false }, expected: [409] });
});
await check('stability event window, level filter, future empty state and expansion detail', async () => {
  const now = new Date(meta.now), year = now.getUTCFullYear();
  const annual = (await api(`/api/stability?${query({ spaceId, year, period: 'year', level: 'all' })}`)).data;
  assert.equal(annual.window.timezone, 'UTC'); assert.equal(annual.summary.eventCount, annual.events.length);
  assert.ok(annual.items.every(b => b.spaceId === spaceId && b.availability >= 0 && b.availability <= 100 && b.remainingMinutes >= 0));
  assert.ok(annual.events.length > 0);
  const event = annual.events[0], detail = (await api(`/api/events/${event.id}`)).data.data; assert.equal(detail.id, event.id); assert.equal(detail.spaceId, spaceId); assert.equal(detail.businessId, event.businessId);
  const filtered = (await api(`/api/stability?${query({ spaceId, year, period: 'year', level: 'P1' })}`)).data; assert.ok(filtered.events.every(e => e.level === 'P1')); assert.equal(filtered.summary.eventCount, filtered.events.length);
  const future = (await api(`/api/stability?${query({ spaceId, year: year + 1, period: 'year', level: 'all' })}`)).data; assert.equal(future.events.length, 0); assert.equal(future.summary.eventCount, 0); assert.ok(future.items.every(item => item.availability === null), 'future observations cannot claim 100% availability');
  const day = (await api(`/api/stability?${query({ spaceId, year, period: 'day', anchor: now.toISOString().slice(0, 10), level: 'all' })}`)).data; assert.equal(day.window.timezone, 'UTC'); assert.ok(day.events.length <= annual.events.length);
});
await check('seven-day status matrix preserves UTC and supported status values', async () => {
  const status = (await api('/api/statuspage')).data; assert.equal(status.timezone, 'UTC'); assert.equal(status.days.length, 7); assert.ok(status.spaces.length >= 3);
  for (const space of status.spaces) for (const business of space.businesses) {
    assert.equal(business.days.length, 7); assert.ok(['healthy', 'incident'].includes(business.latestStatus));
    assert.ok(business.days.every(d => ['healthy', 'incident', 'no-data'].includes(d.status)));
  }
});
await check('screen create, edit, persisted read, export and preview', async () => {
  const indicator = checkout.indicators.find(i => i.id !== 'ind-empty'); assert.ok(indicator);
  const input = { spaceId, name: `验收大屏-${tag}`, businessId: 'biz-checkout', columns: 2, panels: [{ indicatorId: indicator.id, kind: 'line', aggregation: 'avg' }, { indicatorId: 'ind-empty', kind: 'bar', aggregation: 'sum' }] };
  ownScreen = (await create('screens', input)).data;
  const changed = { ...input, name: `已更新大屏-${tag}`, columns: 3, panels: [{ indicatorId: indicator.id, kind: 'funnel', aggregation: 'raw' }] };
  ownScreen = (await update('screens', ownScreen.id, changed)).data;
  const reread = (await api(`/api/screens/${ownScreen.id}`)).data.data; assert.equal(reread.columns, 3); assert.equal(reread.name, changed.name);
  const exported = (await api(`/api/screens/${ownScreen.id}/export`)).data; assert.equal(exported.format, 'hengchuan-screen'); assert.equal(exported.version, 1); assert.ok(!('id' in exported.screen)); assert.ok(!('updatedAt' in exported.screen));
  const preview = (await api(`/api/screens/${ownScreen.id}/preview`)).data; assert.equal(preview.screen.id, ownScreen.id); assert.equal(preview.dashboard.charts.length, 1); assert.equal(preview.dashboard.charts[0].indicatorId, indicator.id);
  assert.ok((await api(`/api/screens?${query({ spaceId, search: tag })}`)).data.items.some(s => s.id === ownScreen.id));
  assert.ok((await api(`/api/screens?${query({ spaceId: otherSpaceId, search: tag })}`)).data.items.every(s => s.id !== ownScreen.id));
});
await check('screen import roundtrip, duplicate conflict and strict format atomicity', async () => {
  assert.ok(ownScreen);
  const document = (await api(`/api/screens/${ownScreen.id}/export`)).data;
  const importedDocument = { ...document, screen: { ...document.screen, name: `导入大屏-${tag}` } };
  const imported = (await api('/api/screens/import', { method: 'POST', body: { revision: await revision(), spaceId, document: importedDocument }, expected: [200, 201] })).data; resources.screens.add(imported.data.id);
  assert.equal(imported.data.name, importedDocument.screen.name);
  const before = await revision();
  await api('/api/screens/import', { method: 'POST', body: { revision: before, spaceId, document: importedDocument }, expected: [409] });
  for (const bad of [{ ...importedDocument, version: 99 }, { ...importedDocument, injectedUnknown: true }, { ...importedDocument, screen: { ...importedDocument.screen, name: `无效大屏-${tag}`, columns: 4 } }, { ...importedDocument, screen: { ...importedDocument.screen, name: `跨空间大屏-${tag}`, businessId: 'biz-checkout' } }]) {
    await api('/api/screens/import', { method: 'POST', body: { revision: before, spaceId: bad.screen.name.startsWith('跨空间') ? otherSpaceId : spaceId, document: bad }, expected: '4xx' }); assert.equal(await revision(), before);
  }
});
await check('resource deletion persists and unavailable ids are explicit', async () => {
  assert.ok(ownBusiness && ownIndicator && ownScreen);
  await remove('screens', ownScreen.id); await api(`/api/screens/${ownScreen.id}`, { expected: [404] });
  await remove('indicators', ownIndicator.id); assert.ok((await api(`/api/indicators?${query({ spaceId, search: tag, pageSize: 100 })}`)).data.items.every(i => i.id !== ownIndicator.id));
  await remove('businesses', ownBusiness.id); await api(`/api/businesses/${ownBusiness.id}`, { expected: [404] });
  assert.equal((await api(`/api/overview?${query({ spaceId, search: 'NO_MATCH_' + tag })}`)).data.items.length, 0);
});
for (const kind of ['screens', 'indicators', 'businesses']) for (const id of resources[kind]) {
  try { await remove(kind, id); }
  catch (error) { results.push({ name: `cleanup synthetic ${kind}`, status: 'failed', error: error.message }); }
}
await persist();
console.log(`${results.filter(r => r.status === 'passed').length}/${results.length} checks passed`);
process.exitCode = results.some(r => r.status === 'failed') ? 1 : 0;
async function persist() {
  await mkdir(new URL('./evidence/', import.meta.url), { recursive: true });
  await writeFile(new URL('./evidence/api-results.json', import.meta.url), JSON.stringify({ base, ranAt: new Date().toISOString(), syntheticTag: tag, results }, null, 2));
}
