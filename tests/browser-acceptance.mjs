import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { startBrowser } from './browser-support.mjs';

const run = await startBrowser();
const { page, byId, check, responseFor, screenshot, base } = run;
const apiBase = process.env.HENGCHUAN_API ?? 'http://127.0.0.1:5080';
assert.match(apiBase, /^http:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/);
const tag = randomUUID().slice(0, 8), own = { businesses: new Set(), screens: new Set(), indicators: new Set() };
async function readApi(path) { const response = await fetch(apiBase + path, { signal: AbortSignal.timeout(15_000) }); assert.ok(response.ok); return response.json(); }
async function createApi(kind, data) {
  const revision = (await readApi('/api/meta')).revision;
  const response = await fetch(`${apiBase}/api/${kind}`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Hengchuan-Write': '1' }, body: JSON.stringify({ revision, data }), signal: AbortSignal.timeout(15_000) });
  assert.ok(response.ok, `synthetic ${kind}: HTTP ${response.status}`); const result = await response.json(); own[kind].add(result.data.id); return result.data;
}
async function nav(id) { await byId(`nav-${id}`).click(); }
let meta, checkout, businessId, screenId;
try {
  meta = await readApi('/api/meta'); assert.equal(meta.mode, 'demo', 'Only the isolated demo is eligible for mutation tests.');
  checkout = (await readApi('/api/businesses/biz-checkout')).data;
  await page.goto(base + '/polaris', { waitUntil: 'networkidle' });
  await check('overview renders genuine demo, scoped cards and search empty state', async () => {
    await byId('space-select').selectOption(checkout.spaceId); await byId('business-card-biz-checkout').waitFor();
    assert.match(await page.locator('body').innerText(), /合成演示|演示模式/);
    await byId('business-search').fill(`NO_MATCH_${tag}`);
    const empty = await responseFor('/api/overview', () => byId('business-search').press('Enter'));
    assert.equal(empty.items.length, 0); assert.match(await page.locator('body').innerText(), /暂无匹配业务|没有.*业务/);
    await byId('business-search').fill(''); await responseFor('/api/overview', () => byId('business-search').press('Enter'));
    await byId('business-card-biz-checkout').waitFor();
    assert.deepEqual(await byId('business-auto-refresh').locator('option').evaluateAll(options => options.map(option => option.value)), ['0', '30', '60']);
    await byId('business-layout-compact').click(); await page.locator('[data-testid="business-layout-content"][data-layout="compact"]').waitFor(); assert.ok(await byId('business-drill-biz-checkout').isVisible());
    await byId('business-layout-cards').click(); await page.locator('[data-testid="business-layout-content"][data-layout="cards"]').waitFor();
    assert.equal(await byId('business-layout-content').evaluate(element => getComputedStyle(element).gridTemplateColumns.split(' ').length), 2);
    await screenshot('01-overview');
  });
  await check('business cancel, create, edit, saved refresh and explicit deletion', async () => {
    await byId('business-create').click(); await byId('business-name').fill(`取消业务-${tag}`); await byId('form-cancel').click();
    assert.equal(await page.getByRole('dialog').count(), 0);
    await byId('business-create').click();
    for (const [id, value] of Object.entries({ 'business-name': `浏览器业务-${tag}`, 'business-owner': '合成验收', 'business-service': `synthetic-${tag}`, 'business-environment': 'demo', 'business-target': '99.9', 'business-description': '<script>window.__HENGCHUAN_INJECTED__=true</script> 合成文本，不执行。' })) await byId(id).fill(value);
    const rejected = page.waitForResponse(response => new URL(response.url()).pathname === '/api/businesses' && response.request().method() === 'POST'); await byId('form-save').click(); assert.equal((await rejected).status(), 400);
    await page.getByRole('alert').filter({ hasText: /HTML|脚本/ }).waitFor(); assert.equal(await page.evaluate(() => window.__HENGCHUAN_INJECTED__), undefined);
    await byId('business-description').fill('仅用于隔离合成浏览器验收的业务描述。');
    const created = await responseFor('/api/businesses', () => byId('form-save').click(), 'POST'); businessId = created.data.id; own.businesses.add(businessId);
    await page.getByRole('dialog').waitFor({ state: 'hidden' }); await byId(`business-card-${businessId}`).waitFor();
    await byId(`business-edit-${businessId}`).click(); await byId('business-name').fill(`已保存业务-${tag}`); await byId('business-target').fill('99.95');
    await responseFor(`/api/businesses/${businessId}`, () => byId('form-save').click(), 'PUT'); await page.getByRole('dialog').waitFor({ state: 'hidden' });
    await page.reload({ waitUntil: 'networkidle' }); await byId(`business-card-${businessId}`).waitFor();
    assert.match(await byId(`business-card-${businessId}`).innerText(), new RegExp(`已保存业务-${tag}`));
    assert.equal(await page.evaluate(() => window.__HENGCHUAN_INJECTED__), undefined);
    await byId(`business-delete-${businessId}`).click();
    await responseFor(`/api/businesses/${businessId}`, () => byId('business-delete-confirm').click(), 'DELETE'); own.businesses.delete(businessId);
    await page.getByRole('dialog').waitFor({ state: 'hidden' });
    await page.waitForFunction(id => !document.querySelector(`[data-testid="business-card-${id}"]`), businessId);
  });
  await check('business drilldown shows thirteen charts with comparison and honest empty state', async () => {
    await byId('business-drill-biz-checkout').click(); await page.waitForURL('**/polaris/n/biz-checkout');
    await page.reload({ waitUntil: 'networkidle' });
    await byId('dashboard-chart-ind-empty').waitFor(); assert.equal(await page.locator('[data-testid^="dashboard-chart-"]').count(), 13);
    assert.equal(await byId('business-dashboard-grid').evaluate(element => getComputedStyle(element).gridTemplateColumns.split(' ').length), 2);
    await page.setViewportSize({ width: 390, height: 844 }); assert.equal(await byId('business-dashboard-grid').evaluate(element => getComputedStyle(element).gridTemplateColumns.split(' ').length), 1); await page.setViewportSize({ width: 1440, height: 1050 });
    assert.match(await byId('dashboard-chart-ind-empty').innerText(), /暂无|无.*数据|没有.*数据/);
    const compare = await responseFor('/api/businesses/biz-checkout/dashboard', () => byId('dashboard-comparison').selectOption('yesterday'));
    assert.equal(compare.comparison, 'yesterday');
    const aggregated = await responseFor('/api/businesses/biz-checkout/dashboard', () => byId('dashboard-aggregation').selectOption('sum'));
    assert.equal(aggregated.aggregation, 'sum');
    const normal = byId(`dashboard-chart-${aggregated.charts.find(chart => chart.points.length).indicatorId}`);
    await normal.locator('svg[role="img"]').hover(); await normal.getByRole('tooltip').waitFor();
    await normal.getByRole('button', { name: '统计', exact: true }).click(); assert.ok(await normal.getByRole('table').isVisible());
    await normal.getByRole('button', { name: '对比', exact: true }).click();
    await normal.getByRole('button', { name: '全屏查看图表' }).click(); await page.getByRole('dialog').waitFor();
    await page.keyboard.press('Escape'); await page.getByRole('dialog').waitFor({ state: 'hidden' });
    const custom = await responseFor('/api/businesses/biz-checkout/dashboard', () => page.getByLabel('快捷时间范围').selectOption('6'));
    assert.ok(Date.parse(custom.window.to) - Date.parse(custom.window.from) <= 6 * 3600_000);
    await screenshot('02-business-dashboard');
  });
  await check('indicator paging, AND filters, columns and atomic batch conflict recovery', async () => {
    const input = { spaceId: checkout.spaceId, name: `浏览器指标-${tag}`, sourceId: 'demo', sourceName: '浏览器合成源', metricName: `synthetic.browser.${tag}`, unit: 'ms', owner: '合成验收', visibility: 'space', enabled: false, businessIds: [], behavior: 'normal' };
    const first = await createApi('indicators', input), second = await createApi('indicators', { ...input, name: `浏览器批量-${tag}`, metricName: `synthetic.batch.${tag}` });
    await responseFor('/api/indicators', () => nav('indicator')); await byId('indicator-search').waitFor();
    const paged = await responseFor('/api/indicators', () => byId('indicator-page-size').selectOption('5')); assert.ok(paged.items.length <= 5 && paged.total > 5);
    const next = await responseFor('/api/indicators', () => byId('indicator-page-next').click()); assert.equal(next.page, 2);
    await byId('indicator-search').fill(tag); await byId('indicator-source').selectOption('demo'); await byId('indicator-enabled').selectOption('false'); await byId('indicator-source-name').fill('浏览器合成源');
    const filtered = await responseFor('/api/indicators', () => byId('indicator-search').press('Enter')); assert.equal(filtered.total, 2); assert.ok(filtered.items.every(i => !i.enabled));
    await byId('indicator-columns').click(); await byId('indicator-column-owner').uncheck(); await byId('indicator-column-updatedBy').check();
    assert.equal(await page.getByRole('columnheader', { name: '负责人', exact: true }).count(), 0); assert.ok(await page.getByRole('columnheader', { name: '更新人', exact: true }).isVisible()); await byId('indicator-columns').click();
    await byId(`indicator-select-${first.id}`).check(); await byId(`indicator-select-${second.id}`).check();
    await createApi('indicators', { ...input, name: `版本竞争-${randomUUID().slice(0, 8)}`, metricName: `synthetic.conflict.${randomUUID()}` });
    const rejected = page.waitForResponse(response => new URL(response.url()).pathname === '/api/indicators/batch' && response.request().method() === 'POST');
    await byId('indicator-batch-enable').click(); assert.equal((await rejected).status(), 409); await page.getByRole('alert').waitFor();
    const afterConflict = await readApi(`/api/indicators?spaceId=${checkout.spaceId}&search=${tag}&pageSize=100`); assert.ok(afterConflict.items.every(i => !i.enabled));
    await responseFor('/api/indicators', () => page.getByRole('button', { name: '刷新列表', exact: true }).click());
    await byId(`indicator-select-${first.id}`).check(); await byId(`indicator-select-${second.id}`).check();
    const enabled = await responseFor('/api/indicators/batch', () => byId('indicator-batch-enable').click(), 'POST'); assert.equal(enabled.updatedCount, 2);
    await page.getByText('没有匹配的指标', { exact: true }).waitFor();
    await byId('indicator-enabled').selectOption('true'); await responseFor('/api/indicators', () => byId('indicator-search').press('Enter'));
    await byId(`indicator-select-${first.id}`).waitFor(); await screenshot('03-indicators');
  });
  await check('status matrix expands all demo spaces with seven UTC day columns', async () => {
    const status = await responseFor('/api/statuspage', () => nav('statuspage')); assert.equal(status.days.length, 7); assert.equal(status.timezone, 'UTC');
    await byId('status-expand-all').click(); await byId('status-business-biz-checkout').waitFor();
    assert.equal(await page.getByRole('columnheader').count(), 9); assert.match(await page.locator('body').innerText(), /全部演示空间|全部.*空间/);
    await screenshot('04-statuspage');
  });
  await check('stability filters event windows, expands source detail and shows future empty state', async () => {
    const annual = await responseFor('/api/stability', () => nav('stability')); assert.ok(annual.events.length > 0); assert.equal(annual.summary.eventCount, annual.events.length);
    const event = annual.events[0];
    const detail = await responseFor(`/api/events/${event.id}`, () => byId(`event-expand-${event.id}`).click());
    await byId(`event-details-${event.id}`).getByText(detail.data.description, { exact: true }).waitFor();
    await screenshot('05-stability-events');
    await byId(`event-expand-${event.id}`).click(); assert.equal(await byId(`event-details-${event.id}`).count(), 0);
    const today = await responseFor('/api/stability', () => byId('stability-period').selectOption('day')); assert.ok(Date.parse(today.window.to) - Date.parse(today.window.from) <= 24 * 3600_000);
    const filtered = await responseFor('/api/stability', () => byId('stability-level').selectOption('P1')); assert.ok(filtered.events.every(e => e.level === 'P1'));
    const year = new Date(meta.now).getUTCFullYear();
    const future = await responseFor('/api/stability', async () => { await byId('stability-year').fill(String(year + 1)); await byId('stability-year').press('Tab'); });
    assert.equal(future.events.length, 0); await page.getByText('所选周期暂无观测数据', { exact: true }).waitFor();
    await responseFor('/api/stability', async () => { await byId('stability-year').fill(String(year)); await byId('stability-year').press('Tab'); });
  });
  await check('screen create, edit, refresh, per-panel preview and actual export download', async () => {
    await responseFor('/api/screens', () => nav('screen')); await byId('screen-create').click();
    await byId('screen-name').fill(`浏览器大屏-${tag}`); await byId('screen-business').selectOption('biz-checkout'); await byId('screen-panel-indicator-0').waitFor();
    while (await page.getByRole('button', { name: '移除面板', exact: true }).count() > 2) await page.getByRole('button', { name: '移除面板', exact: true }).last().click();
    await byId('screen-panel-kind-0').selectOption('bar'); await byId('screen-panel-aggregation-0').selectOption('sum'); await byId('screen-panel-indicator-1').selectOption('ind-empty');
    const created = await responseFor('/api/screens', () => byId('form-save').click(), 'POST'); screenId = created.data.id; own.screens.add(screenId);
    await page.getByRole('dialog').waitFor({ state: 'hidden' }); await byId(`screen-card-${screenId}`).waitFor();
    await byId(`screen-edit-${screenId}`).click(); await byId('screen-name').fill(`已保存大屏-${tag}`); await byId('screen-columns').selectOption('3'); await byId('screen-panel-kind-0').selectOption('funnel'); await byId('screen-panel-aggregation-0').selectOption('raw');
    await responseFor(`/api/screens/${screenId}`, () => byId('form-save').click(), 'PUT'); await page.getByRole('dialog').waitFor({ state: 'hidden' });
    await page.reload({ waitUntil: 'networkidle' }); await byId(`screen-card-${screenId}`).waitFor(); assert.match(await byId(`screen-card-${screenId}`).innerText(), /3 列布局/);
    const preview = await responseFor(`/api/screens/${screenId}/preview`, () => byId(`screen-preview-${screenId}`).click()); assert.equal(preview.screen.panels.length, 2); assert.equal(preview.dashboard.charts.length, 2);
    await page.getByRole('dialog').getByText(/当前窗口|暂无|没有.*数据|无数据/).first().waitFor(); await screenshot('06-screen-preview');
    await page.getByRole('button', { name: '关闭弹窗', exact: true }).click();
    const downloadWait = page.waitForEvent('download'); await byId(`screen-export-${screenId}`).click(); const download = await downloadWait;
    const stream = await download.createReadStream(), chunks = []; for await (const chunk of stream) chunks.push(chunk);
    const document = JSON.parse(Buffer.concat(chunks).toString('utf8')); assert.equal(document.format, 'hengchuan-screen'); assert.equal(document.version, 1); assert.ok(!('id' in document.screen));
    run.exportedScreen = document;
  });
  await check('screen import rejects malformed/unknown JSON, roundtrips and deletes explicitly', async () => {
    assert.ok(run.exportedScreen); const before = (await readApi('/api/meta')).revision;
    await byId('screen-import').click(); await byId('screen-import-content').fill('{ invalid JSON'); await byId('screen-import-submit').click(); await page.getByRole('alert').filter({ hasText: /JSON/ }).waitFor(); assert.equal((await readApi('/api/meta')).revision, before);
    const document = { ...run.exportedScreen, screen: { ...run.exportedScreen.screen, name: `浏览器导入-${tag}` } };
    await byId('screen-import-content').fill(JSON.stringify({ ...document, injectedUnknown: true }));
    const rejected = page.waitForResponse(response => new URL(response.url()).pathname === '/api/screens/import' && response.request().method() === 'POST'); await byId('screen-import-submit').click(); assert.equal((await rejected).status(), 400); assert.equal((await readApi('/api/meta')).revision, before);
    await byId('screen-import-content').fill(JSON.stringify(document)); const imported = await responseFor('/api/screens/import', () => byId('screen-import-submit').click(), 'POST'); own.screens.add(imported.data.id);
    await page.getByRole('dialog').waitFor({ state: 'hidden' }); await byId(`screen-card-${imported.data.id}`).waitFor();
    await byId(`screen-delete-${imported.data.id}`).click(); await responseFor(`/api/screens/${imported.data.id}`, () => byId('screen-delete-confirm').click(), 'DELETE'); own.screens.delete(imported.data.id);
    await page.getByRole('dialog').waitFor({ state: 'hidden' }); await page.waitForFunction(id => !document.querySelector(`[data-testid="screen-card-${id}"]`), imported.data.id);
  });
  await check('space switching ignores a delayed previous-space response', async () => {
    await nav('polaris'); await byId('business-card-biz-checkout').waitFor();
    const another = meta.spaces.find(space => space.id !== checkout.spaceId); assert.ok(another);
    let release; const gate = new Promise(resolve => { release = resolve; });
    await page.route('**/api/overview?**', async route => {
      if (new URL(route.request().url()).searchParams.get('spaceId') === checkout.spaceId) await gate;
      await route.continue().catch(() => {});
    });
    try {
      const oldRequest = page.waitForRequest(request => new URL(request.url()).pathname === '/api/overview' && new URL(request.url()).searchParams.get('spaceId') === checkout.spaceId);
      await page.getByRole('button', { name: /刷新/ }).click(); await oldRequest;
      const latest = await responseFor('/api/overview', () => byId('space-select').selectOption(another.id));
      assert.ok(latest.items.every(item => item.spaceId === another.id));
      release(); await page.waitForTimeout(200);
      assert.equal(await byId('business-card-biz-checkout').count(), 0); assert.equal(await byId('space-select').inputValue(), another.id);
    } finally { release(); await page.unroute('**/api/overview?**'); }
    await responseFor('/api/overview', () => byId('space-select').selectOption(checkout.spaceId));
  });
  await check('local API error and canceled drilldown recover without stale results', async () => {
    await page.route('**/api/overview?**', route => route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ code: 'AcceptanceUnavailable', message: '合成验收：服务暂不可用' }) }), { times: 1 });
    await page.getByRole('button', { name: /刷新/ }).click(); await page.getByText('合成验收：服务暂不可用', { exact: true }).waitFor();
    await responseFor('/api/overview', () => page.getByRole('button', { name: '重新加载', exact: true }).click()); await byId('business-card-biz-checkout').waitFor();
    await page.route('**/api/businesses/biz-checkout/dashboard?**', async route => { await new Promise(resolve => setTimeout(resolve, 800)); await route.continue().catch(() => {}); });
    await byId('business-drill-biz-checkout').click(); await byId('dashboard-cancel').click(); await byId('dashboard-cancel').waitFor({ state: 'hidden' });
    await page.unroute('**/api/businesses/biz-checkout/dashboard?**');
    await responseFor('/api/businesses/biz-checkout/dashboard', () => page.getByRole('button', { name: /刷新/ }).click()); await byId('dashboard-chart-ind-empty').waitFor();
    assert.equal(await page.locator('[data-testid^="dashboard-chart-"]').count(), 13);
  });
  await check('browser has no script errors or external requests', async () => { assert.deepEqual(run.errors, []); assert.deepEqual(run.external, []); });
} finally {
  for (const kind of ['screens', 'indicators', 'businesses']) for (const id of own[kind]) {
    try {
      const revision = (await readApi('/api/meta')).revision;
      const response = await fetch(`${apiBase}/api/${kind}/${id}?revision=${revision}`, { method: 'DELETE', headers: { 'X-Hengchuan-Write': '1' }, signal: AbortSignal.timeout(10_000) });
      if (![200, 204, 404].includes(response.status)) throw new Error(`cleanup HTTP ${response.status}`);
    } catch (error) { run.errors.push(`Synthetic ${kind} cleanup: ${error.message}`); }
  }
  await run.finish();
}
