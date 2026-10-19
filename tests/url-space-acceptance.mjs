import assert from 'node:assert/strict';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { startBrowser } from './browser-support.mjs';

// Isolated, read-only acceptance: no remote origins, imported sessions or API writes.
// The special-character space exists only in intercepted local GET responses.
const apiBase = process.env.HENGCHUAN_API ?? 'http://127.0.0.1:5080';
assert.match(apiBase, /^http:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/);
const evidence = resolve(process.env.HENGCHUAN_EVIDENCE_DIR ?? new URL('./evidence/', import.meta.url).pathname);
const screenshots = join(evidence, 'url-space-screenshots');
await mkdir(screenshots, { recursive: true });
const temporary = await mkdtemp(join(tmpdir(), 'hengchuan-url-space-'));
const previousTmpdir = process.env.TMPDIR;
let run;
try {
  // browser-support owns a nested temporary profile; keep it inside our cleanup root.
  process.env.TMPDIR = temporary;
  run = await startBrowser();
} finally {
  if (previousTmpdir === undefined) delete process.env.TMPDIR;
  else process.env.TMPDIR = previousTmpdir;
  if (!run) await rm(temporary, { recursive: true, force: true });
}
const { page, context, browser, base, byId, responseFor } = run;
page.setDefaultTimeout(12_000);
const results = [], requests = [], blockedWrites = [];
page.on('request', request => {
  const url = new URL(request.url());
  const document = new URL(page.url());
  if (url.pathname.startsWith('/api/')) requests.push({
    method: request.method(), path: url.pathname, query: url.search,
    documentPath: document.pathname, documentQuery: document.search,
  });
});
await context.route('**/api/**', async route => {
  if (route.request().method() === 'GET') return route.fallback();
  blockedWrites.push({ method: route.request().method(), path: new URL(route.request().url()).pathname });
  await route.abort('blockedbyclient');
});
async function readApi(path) {
  const response = await fetch(apiBase + path, { signal: AbortSignal.timeout(15_000), redirect: 'error' });
  assert.ok(response.ok, `${path}: HTTP ${response.status}`);
  return response.json();
}
async function shot(name) { await page.screenshot({ path: join(screenshots, `${name}.png`), fullPage: true }); }
async function check(name, action) {
  // Use the shared check runner, but keep a separate result collection and finish.
  await run.check(name, async () => {
    try { await action(); results.push({ name, status: 'passed' }); }
    catch (error) {
      results.push({ name, status: 'failed', error: error.message });
      await shot(`failure-${results.length}`).catch(() => {});
      throw error;
    }
  });
}
function urlFor(path, space, extra = {}) {
  const url = new URL(path, base);
  url.searchParams.set('space', space.name);
  for (const [key, value] of Object.entries(extra)) url.searchParams.set(key, value);
  return url.href;
}
async function assertSpace(space, path) {
  await page.waitForFunction(({ id, name, path }) => {
    const url = new URL(location.href);
    return document.querySelector('[data-testid="space-select"]')?.value === id
      && url.searchParams.get('space') === name && url.searchParams.getAll('space').length === 1
      && !url.searchParams.has('spaceId') && (!path || url.pathname === path);
  }, { id: space.id, name: space.name, path });
  assert.equal(await byId('space-error').count(), 0);
}
function assertScopedRequests(start, space) {
  for (const request of requests.slice(start)) {
    if (['/api/overview', '/api/indicators', '/api/stability', '/api/screens'].includes(request.path)) {
      assert.equal(new URLSearchParams(request.query).get('spaceId'), space.id, `${request.path} must use the URL-selected space`);
    }
  }
}
async function assertLinks(space) {
  const links = await page.locator('a[href]').evaluateAll(anchors => anchors.map(anchor => anchor.href));
  const applicationLinks = links.map(href => new URL(href)).filter(url => url.origin === new URL(base).origin && url.pathname.startsWith('/polaris'));
  assert.ok(applicationLinks.length >= 5);
  for (const url of applicationLinks) {
    assert.equal(url.searchParams.get('space'), space.name, `Link loses space: ${url.pathname}`);
    assert.equal(url.searchParams.getAll('space').length, 1);
    assert.equal(url.searchParams.has('spaceId'), false);
  }
}
let meta, defaultSpace, targetSpace, targetBusiness, defaultBusiness, targetOverview, targetIndicator, targetScreen;
let fatal;
try {
  meta = await readApi('/api/meta');
  assert.equal(meta.mode, 'demo', 'Only the isolated synthetic demo is eligible.');
  defaultSpace = meta.spaces.find(space => space.id === 'space-default') ?? meta.spaces[0];
  targetSpace = meta.spaces.find(space => space.id !== defaultSpace.id);
  assert.ok(defaultSpace && targetSpace, 'Two synthetic spaces are required.');
  targetOverview = await readApi(`/api/overview?spaceId=${encodeURIComponent(targetSpace.id)}`);
  const defaultOverview = await readApi(`/api/overview?spaceId=${encodeURIComponent(defaultSpace.id)}`);
  targetBusiness = targetOverview.items[0]; defaultBusiness = defaultOverview.items[0];
  targetIndicator = (await readApi(`/api/indicators?spaceId=${encodeURIComponent(targetSpace.id)}`)).items[0];
  targetScreen = (await readApi(`/api/screens?spaceId=${encodeURIComponent(targetSpace.id)}`)).items[0];
  assert.ok(targetBusiness && defaultBusiness && targetIndicator && targetScreen, 'Synthetic page fixtures are required.');
  const pages = [
    { label: 'overview', path: '/polaris', marker: `business-card-${targetBusiness.id}` },
    { label: 'business detail', path: `/polaris/n/${encodeURIComponent(targetBusiness.id)}`, marker: 'business-dashboard-grid' },
    { label: 'indicators', path: '/polaris/indicator', marker: `indicator-select-${targetIndicator.id}` },
    { label: 'status matrix', path: '/polaris/statuspage', marker: `status-space-toggle-${targetSpace.id}` },
    { label: 'stability', path: '/polaris/stability', heading: '稳定性统计' },
    { label: 'screens', path: '/polaris/screen', marker: `screen-card-${targetScreen.id}` },
  ];
  async function assertPage(item) {
    await assertSpace(targetSpace, item.path);
    if (item.marker) await byId(item.marker).waitFor();
    else await page.getByRole('heading', { name: item.heading, exact: true }).waitFor();
    assert.equal(await page.locator('.page [role="alert"]').count(), 0);
    if (item.path === '/polaris') assert.equal(await byId(`business-card-${defaultBusiness.id}`).count(), 0);
    if (item.path.startsWith('/polaris/n/')) assert.equal(await page.getByRole('heading', { name: targetBusiness.name, exact: true }).count(), 1);
    if (item.path === '/polaris/screen') assert.equal(await byId('screen-card-screen-checkout').count(), 0);
  }
  for (const [index, item] of pages.entries()) {
    await check(`${item.label}: non-default named deep link and browser refresh keep the same space`, async () => {
      const start = requests.length;
      await page.goto(urlFor(item.path, targetSpace), { waitUntil: 'networkidle' });
      await assertPage(item); assertScopedRequests(start, targetSpace);
      const reloadStart = requests.length;
      await page.reload({ waitUntil: 'networkidle' });
      await assertPage(item); assertScopedRequests(reloadStart, targetSpace);
      if (item.path !== '/polaris/statuspage') await assertLinks(targetSpace);
      await shot(`${String(index + 1).padStart(2, '0')}-${item.label.replaceAll(' ', '-')}`);
    });
  }
  await check('missing space uses a documented, canonical default without a second history entry', async () => {
    await page.goto(urlFor('/polaris', targetSpace), { waitUntil: 'networkidle' });
    const historyBefore = await page.evaluate(() => history.length), start = requests.length;
    await page.goto(base + '/polaris?search=preserved', { waitUntil: 'networkidle' });
    await assertSpace(defaultSpace, '/polaris');
    assert.equal(new URL(page.url()).searchParams.get('search'), 'preserved');
    assert.equal(await page.evaluate(() => history.length), historyBefore + 1, 'Canonical selection must replace the incoming entry.');
    assertScopedRequests(start, defaultSpace);
    await page.goBack({ waitUntil: 'networkidle' }); await assertSpace(targetSpace, '/polaris');
  });
  await check('selector, navigation, back and forward keep URL, selector and data consistent', async () => {
    await page.goto(urlFor('/polaris', targetSpace), { waitUntil: 'networkidle' });
    await responseFor('/api/overview', () => byId('space-select').selectOption(defaultSpace.id));
    await assertSpace(defaultSpace, '/polaris'); await byId(`business-card-${defaultBusiness.id}`).waitFor();
    await byId('nav-indicator').click(); await assertSpace(defaultSpace, '/polaris/indicator'); await byId('indicator-search').waitFor();
    await page.goBack({ waitUntil: 'networkidle' }); await assertSpace(defaultSpace, '/polaris');
    await byId(`business-card-${defaultBusiness.id}`).waitFor();
    await page.goBack({ waitUntil: 'networkidle' }); await assertSpace(targetSpace, '/polaris');
    await byId(`business-card-${targetBusiness.id}`).waitFor(); assert.equal(await byId(`business-card-${defaultBusiness.id}`).count(), 0);
    await page.goForward({ waitUntil: 'networkidle' }); await assertSpace(defaultSpace, '/polaris');
    await byId(`business-card-${defaultBusiness.id}`).waitFor(); assert.equal(await byId(`business-card-${targetBusiness.id}`).count(), 0);
    await page.goForward({ waitUntil: 'networkidle' }); await assertSpace(defaultSpace, '/polaris/indicator');
    for (const nav of ['statuspage', 'stability', 'screen', 'polaris']) {
      await byId(`nav-${nav}`).click();
      await assertSpace(defaultSpace, nav === 'polaris' ? '/polaris' : `/polaris/${nav}`);
    }
    await page.reload({ waitUntil: 'networkidle' }); await assertSpace(defaultSpace, '/polaris');
  });
  await check('special-character Chinese names survive encoding, selector changes and refresh without database writes', async () => {
    const special = { id: 'space-url-test-only', name: '研发 & 增长/测试+?#=% 空间' };
    const metaHandler = route => route.fulfill({ json: { ...meta, spaces: [...meta.spaces, special] } });
    const overviewHandler = route => new URL(route.request().url()).searchParams.get('spaceId') === special.id
      ? route.fulfill({ json: { ...targetOverview, items: [] } }) : route.fallback();
    await page.route('**/api/meta', metaHandler); await page.route('**/api/overview?**', overviewHandler);
    try {
      await page.goto(urlFor('/polaris', targetSpace), { waitUntil: 'networkidle' });
      const start = requests.length;
      await responseFor('/api/overview', () => byId('space-select').selectOption(special.id));
      await assertSpace(special, '/polaris'); assertScopedRequests(start, special);
      let url = new URL(page.url());
      assert.equal(url.hash, ''); assert.equal(url.searchParams.size, 1);
      for (const encoded of ['%26', '%2B', '%23', '%25']) assert.ok(url.search.toUpperCase().includes(encoded), `Missing escaped delimiter ${encoded}`);
      // Query-value '?' and '=' may remain literal. Exact parsing proves that the
      // name survived once-only encoding without requiring a serializer's style.
      assert.equal(url.searchParams.get('space'), special.name);
      await page.getByText('当前空间暂无匹配业务', { exact: true }).waitFor();
      assert.equal(await page.locator('[data-testid^="business-card-"]').count(), 0); await assertLinks(special);
      await page.reload({ waitUntil: 'networkidle' }); await assertSpace(special, '/polaris');
      await page.getByText('当前空间暂无匹配业务', { exact: true }).waitFor();
      url = new URL(page.url()); assert.equal(url.searchParams.get('space'), special.name);
      await shot('07-encoded-empty-space');
      await responseFor('/api/overview', () => byId('space-select').selectOption(targetSpace.id));
      await assertSpace(targetSpace, '/polaris');
    } finally { await page.unroute('**/api/meta', metaHandler); await page.unroute('**/api/overview?**', overviewHandler); }
  });
  await check('duplicate names retain the explicit spaceId through refresh and selector changes', async () => {
    const first = { id: 'space-duplicate-first-test-only', name: '重名空间 URL 测试' };
    const second = { id: 'space-duplicate-second-test-only', name: first.name };
    const ids = new Set([first.id, second.id]);
    const metaHandler = route => route.fulfill({ json: { ...meta, spaces: [...meta.spaces, first, second] } });
    const overviewHandler = route => ids.has(new URL(route.request().url()).searchParams.get('spaceId'))
      ? route.fulfill({ json: { ...targetOverview, items: [] } }) : route.fallback();
    async function assertDuplicate(space) {
      await page.waitForFunction(({ id, name }) => {
        const url = new URL(location.href);
        return url.pathname === '/polaris' && url.searchParams.get('space') === name
          && url.searchParams.getAll('space').length === 1 && url.searchParams.get('spaceId') === id
          && url.searchParams.getAll('spaceId').length === 1
          && document.querySelector('[data-testid="space-select"]')?.value === id;
      }, { id: space.id, name: space.name });
      assert.equal(await byId('space-error').count(), 0);
      await page.getByText('当前空间暂无匹配业务', { exact: true }).waitFor();
      assert.equal(await page.locator('[data-testid^="business-card-"]').count(), 0);
      const links = await page.locator('a[href]').evaluateAll(anchors => anchors.map(anchor => anchor.href));
      for (const url of links.map(href => new URL(href)).filter(url => url.origin === new URL(base).origin && url.pathname.startsWith('/polaris'))) {
        assert.equal(url.searchParams.get('space'), space.name);
        assert.equal(url.searchParams.get('spaceId'), space.id);
      }
    }
    await page.route('**/api/meta', metaHandler); await page.route('**/api/overview?**', overviewHandler);
    try {
      let start = requests.length;
      await page.goto(urlFor('/polaris', targetSpace, { spaceId: first.id }), { waitUntil: 'networkidle' });
      await assertDuplicate(first); assertScopedRequests(start, first);
      start = requests.length;
      await page.reload({ waitUntil: 'networkidle' }); await assertDuplicate(first); assertScopedRequests(start, first);
      start = requests.length;
      await responseFor('/api/overview', () => byId('space-select').selectOption(second.id));
      await assertDuplicate(second); assertScopedRequests(start, second);
      start = requests.length;
      await page.reload({ waitUntil: 'networkidle' }); await assertDuplicate(second); assertScopedRequests(start, second);
      start = requests.length;
      await page.goto(urlFor('/polaris', first), { waitUntil: 'networkidle' });
      await byId('space-error').waitFor(); assert.match(await byId('space-error').innerText(), /重名/);
      assert.deepEqual(requests.slice(start).filter(request => request.path !== '/api/meta'), []);
      await responseFor('/api/overview', () => byId('space-select').selectOption(first.id));
      await assertDuplicate(first); await shot('08-duplicate-name-space');
    } finally { await page.unroute('**/api/meta', metaHandler); await page.unroute('**/api/overview?**', overviewHandler); }
  });
  await check('unknown, empty and multi-value parameters show an explicit error and issue no data requests', async () => {
    const valid = encodeURIComponent(targetSpace.name), id = encodeURIComponent(targetSpace.id);
    const invalid = [
      'space=URL_TEST_UNKNOWN_SPACE', 'space=', 'space',
      `space=${valid}&space=${encodeURIComponent(defaultSpace.name)}`, `space=${valid}&space=${valid}`,
      `spaceId=space-url-test-unknown&space=${valid}`, `spaceId=${id}&spaceId=${id}`, 'spaceId=',
    ];
    if (!meta.spaces.some(space => space.name === '默认空间')) invalid.push(`space=${encodeURIComponent('默认空间')}`);
    for (const query of invalid) {
      const start = requests.length;
      await page.goto(`${base}/polaris?${query}`, { waitUntil: 'networkidle' });
      await byId('space-error').waitFor();
      assert.match(await byId('space-error').innerText(), /空间/);
      assert.equal(await byId('space-select').inputValue(), '', query);
      assert.equal(await page.locator('.page h1').count(), 0, query);
      assert.equal(await page.locator('[data-testid^="business-card-"]').count(), 0, query);
      assert.deepEqual(requests.slice(start).filter(request => request.path !== '/api/meta'), [], query);
    }
    await shot('08-invalid-space');
    await responseFor('/api/overview', () => byId('space-select').selectOption(targetSpace.id));
    await assertSpace(targetSpace, '/polaris'); await byId(`business-card-${targetBusiness.id}`).waitFor();
  });
  await check('a valid spaceId has precedence and canonicalizes to one encoded space name', async () => {
    const start = requests.length;
    await page.goto(`${base}/polaris?spaceId=${encodeURIComponent(targetSpace.id)}&space=URL_TEST_UNKNOWN_SPACE&search=kept`, { waitUntil: 'networkidle' });
    await assertSpace(targetSpace, '/polaris'); await byId(`business-card-${targetBusiness.id}`).waitFor();
    assert.equal(new URL(page.url()).searchParams.get('search'), 'kept'); assertScopedRequests(start, targetSpace);
    await page.reload({ waitUntil: 'networkidle' }); await assertSpace(targetSpace, '/polaris');
  });
  await check('legacy unscoped business bookmarks infer the actual space before rendering', async () => {
    const path = `/polaris/n/${encodeURIComponent(targetBusiness.id)}`, start = requests.length;
    await page.goto(base + path, { waitUntil: 'networkidle' });
    await assertSpace(targetSpace, path); await byId('business-dashboard-grid').waitFor();
    assert.equal(await page.getByRole('heading', { name: targetBusiness.name, exact: true }).count(), 1);
    assertScopedRequests(start, targetSpace);
    await page.reload({ waitUntil: 'networkidle' }); await assertSpace(targetSpace, path);
  });
  await check('an old delayed legacy-space lookup cannot replace a newer indicator navigation', async () => {
    const detailPath = `/api/businesses/${encodeURIComponent(targetBusiness.id)}`;
    let held = false, release, requested;
    const gate = new Promise(resolve => { release = resolve; });
    const seen = new Promise(resolve => { requested = resolve; });
    const handler = async route => {
      if (held) return route.fallback();
      held = true; requested(); await gate;
      return route.continue().catch(() => {});
    };
    await page.route(`**${detailPath}`, handler);
    const start = requests.length;
    try {
      await page.goto(`${base}/polaris/n/${encodeURIComponent(targetBusiness.id)}`, { waitUntil: 'domcontentloaded' });
      await Promise.race([seen, new Promise((_, reject) => setTimeout(() => reject(new Error('Legacy-space lookup was not requested.')), 5000))]);
      await byId('nav-indicator').click();
      await assertSpace(defaultSpace, '/polaris/indicator');
      await page.getByRole('heading', { name: '指标管理', exact: true }).waitFor();
      const selectedUrl = page.url();
      const oldResponse = page.waitForResponse(response => new URL(response.url()).pathname === detailPath && response.request().method() === 'GET');
      release(); assert.ok((await oldResponse).ok()); await page.waitForLoadState('networkidle');
      assert.equal(page.url(), selectedUrl, 'A superseded legacy lookup must not redirect over newer navigation.');
      await assertSpace(defaultSpace, '/polaris/indicator');
      await page.getByRole('heading', { name: '指标管理', exact: true }).waitFor();
      assert.equal(await byId('business-dashboard-grid').count(), 0);
      assert.equal(requests.slice(start).filter(request => request.path === `${detailPath}/dashboard`).length, 0);
      assert.equal(await page.locator('.page [role="alert"]').count(), 0);
    } finally { release(); await page.unroute(`**${detailPath}`, handler); }
  });
  await check('all six pages wait for delayed metadata and never request default-space data first', async () => {
    for (const item of pages) {
      let release, requested;
      const gate = new Promise(resolve => { release = resolve; });
      const seen = new Promise(resolve => { requested = resolve; });
      const handler = async route => { requested(); await gate; await route.fulfill({ json: meta }).catch(() => {}); };
      await page.route('**/api/meta', handler);
      const start = requests.length;
      try {
        await page.goto(urlFor(item.path, targetSpace), { waitUntil: 'domcontentloaded' });
        await Promise.race([seen, new Promise((_, reject) => setTimeout(() => reject(new Error(`${item.label}: metadata was not requested`)), 5000))]);
        await page.waitForTimeout(100);
        assert.equal(await page.locator('.page h1').count(), 0, item.label);
        assert.deepEqual(requests.slice(start).filter(request => request.path !== '/api/meta'), [], item.label);
        release(); await page.waitForLoadState('networkidle'); await assertPage(item); assertScopedRequests(start, targetSpace);
      } finally { release(); await page.unroute('**/api/meta', handler); }
    }
  });
  await check('metadata failure and same-page recovery never load data for the previous unknown-space URL', async () => {
    let attempts = 0, release;
    const gate = new Promise(resolve => { release = resolve; });
    const metaHandler = async route => {
      attempts++;
      if (attempts === 1) return route.fulfill({
        status: 503,
        json: { code: 'UrlSpaceAcceptanceUnavailable', message: '合成验收：元数据首次失败' },
      });
      await gate;
      return route.fulfill({ json: meta });
    };
    await page.route('**/api/meta', metaHandler);
    const start = requests.length;
    try {
      await page.goto(base + '/polaris?space=UNKNOWN_SPACE', { waitUntil: 'networkidle' });
      await page.locator('.page [role="alert"]').filter({ hasText: '合成验收：元数据首次失败' }).waitFor();
      assert.equal(attempts, 1);
      assert.equal(await page.locator('.page h1').count(), 0);
      assert.deepEqual(requests.slice(start).filter(request => request.path !== '/api/meta'), []);

      const secondMetadata = page.waitForRequest(request => new URL(request.url()).pathname === '/api/meta');
      const navigation = byId('nav-indicator').click();
      await secondMetadata;
      assert.equal(await page.locator('.page h1').count(), 0);
      assert.deepEqual(requests.slice(start).filter(request => request.path !== '/api/meta'), []);
      release(); await navigation; await page.waitForLoadState('networkidle');
      await assertSpace(defaultSpace, '/polaris/indicator'); await byId('indicator-search').waitFor();
      assert.equal(attempts, 2);
      assert.equal(await page.locator('.page [role="alert"]').count(), 0, 'Successful metadata retry must clear the initial failure.');
      const recovered = requests.slice(start).filter(request => request.path !== '/api/meta');
      assert.ok(recovered.some(request => request.path === '/api/indicators'));
      for (const request of recovered) {
        assert.equal(request.documentPath, '/polaris/indicator', `${request.path} was requested before the final route was committed`);
        assert.equal(new URLSearchParams(request.documentQuery).get('space'), defaultSpace.name);
      }
      assertScopedRequests(start, defaultSpace);
      await shot('09-metadata-recovery');
    } finally { release(); await page.unroute('**/api/meta', metaHandler); }
  });
  await check('business details reject an explicit different space without displaying or adopting that business', async () => {
    const path = `/polaris/n/${encodeURIComponent(defaultBusiness.id)}`, start = requests.length;
    await page.goto(urlFor(path, targetSpace), { waitUntil: 'networkidle' });
    await assertSpace(targetSpace, path);
    await page.locator('.page [role="alert"]').filter({ hasText: /空间/ }).waitFor();
    assert.equal(await byId('business-dashboard-grid').count(), 0);
    assert.equal(await page.locator('[data-testid^="dashboard-chart-"]').count(), 0);
    assert.equal(await page.getByRole('heading', { name: defaultBusiness.name, exact: true }).count(), 0);
    assert.equal(requests.slice(start).filter(request => request.path === `/api/businesses/${defaultBusiness.id}/dashboard`).length, 0);
    await shot('09-cross-space-detail');
    await responseFor('/api/overview', () => byId('space-select').selectOption(defaultSpace.id));
    await assertSpace(defaultSpace, '/polaris'); await byId(`business-card-${defaultBusiness.id}`).waitFor();
    await byId(`business-drill-${defaultBusiness.id}`).click(); await assertSpace(defaultSpace, path);
    await byId('business-dashboard-grid').waitFor();
    await page.getByRole('link', { name: '管理关联指标', exact: true }).click();
    await assertSpace(defaultSpace, '/polaris/indicator');
    assert.equal(new URL(page.url()).searchParams.get('businessId'), defaultBusiness.id);
    await byId('indicator-business').locator(`option[value="${defaultBusiness.id}"]`).waitFor({ state: 'attached' });
    assert.equal(await byId('indicator-business').inputValue(), defaultBusiness.id);
    await responseFor('/api/indicators', () => byId('space-select').selectOption(targetSpace.id));
    await assertSpace(targetSpace, '/polaris/indicator');
    assert.equal(new URL(page.url()).searchParams.has('businessId'), false);
    assert.equal(await byId('indicator-business').inputValue(), '');
    assert.equal(await page.locator('.page [role="alert"]').count(), 0);
  });
  await check('status matrix explicitly shows all spaces and business links select their actual target space', async () => {
    await page.goto(urlFor('/polaris/statuspage', targetSpace), { waitUntil: 'networkidle' });
    await assertSpace(targetSpace, '/polaris/statuspage');
    assert.match(await page.locator('.page').innerText(), /全部演示空间/);
    await byId(`status-space-toggle-${defaultSpace.id}`).waitFor(); await byId(`status-space-toggle-${targetSpace.id}`).waitFor();
    await byId('status-expand-all').click();
    for (const [business, space] of [[defaultBusiness, defaultSpace], [targetBusiness, targetSpace]]) {
      const link = byId(`status-business-${business.id}`).getByRole('link');
      await link.waitFor(); const url = new URL(await link.getAttribute('href'), base);
      assert.equal(url.pathname, `/polaris/n/${business.id}`); assert.equal(url.searchParams.get('space'), space.name);
    }
    await byId(`status-business-${defaultBusiness.id}`).getByRole('link').click();
    await assertSpace(defaultSpace, `/polaris/n/${defaultBusiness.id}`); await byId('business-dashboard-grid').waitFor();
    assert.equal(await page.getByRole('heading', { name: defaultBusiness.name, exact: true }).count(), 1);
  });
  await check('a delayed old-space response cannot restore stale cards after selector navigation', async () => {
    await page.goto(urlFor('/polaris', targetSpace), { waitUntil: 'networkidle' });
    await byId(`business-card-${targetBusiness.id}`).waitFor();
    let release;
    const gate = new Promise(resolve => { release = resolve; });
    const handler = async route => {
      if (new URL(route.request().url()).searchParams.get('spaceId') === targetSpace.id) await gate;
      await route.continue().catch(() => {});
    };
    await page.route('**/api/overview?**', handler);
    try {
      const oldRequest = page.waitForRequest(request => new URL(request.url()).pathname === '/api/overview' && new URL(request.url()).searchParams.get('spaceId') === targetSpace.id);
      await byId('business-refresh').click(); await oldRequest;
      await responseFor('/api/overview', () => byId('space-select').selectOption(defaultSpace.id));
      await assertSpace(defaultSpace, '/polaris'); release(); await page.waitForLoadState('networkidle');
      await byId(`business-card-${defaultBusiness.id}`).waitFor(); assert.equal(await byId(`business-card-${targetBusiness.id}`).count(), 0);
    } finally { release(); await page.unroute('**/api/overview?**', handler); }
  });
  await check('browser reports no script errors, external requests or write attempts', async () => {
    assert.deepEqual(run.errors, []); assert.deepEqual(run.external, []); assert.deepEqual(blockedWrites, []);
  });
} catch (error) {
  fatal = error.message; console.error(error); process.exitCode = 1;
} finally {
  const document = {
    base, apiBase, ranAt: new Date().toISOString(), chromium: browser.version(), sandbox: true,
    scope: 'Local synthetic demo only; GET requests; isolated context; special space mocked without persistence.',
    selectedSpaces: [defaultSpace, targetSpace].filter(Boolean), results, fatal,
    pageErrors: run.errors, blockedExternalOrigins: [...new Set(run.external)], blockedWriteAttempts: blockedWrites,
    apiRequests: requests,
  };
  await writeFile(join(evidence, 'url-space-results.json'), JSON.stringify(document, null, 2));
  await browser.close(); await rm(temporary, { recursive: true, force: true });
  console.log(`${results.filter(result => result.status === 'passed').length}/${results.length} URL-space checks passed`);
  process.exitCode = fatal || results.some(result => result.status === 'failed') || run.errors.length || run.external.length || blockedWrites.length ? 1 : 0;
}
