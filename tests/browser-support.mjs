import { createRequire } from 'node:module';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import assert from 'node:assert/strict';

const require = createRequire(import.meta.url);
let engine;
try { engine = require('playwright-core'); }
catch { engine = require(process.env.PLAYWRIGHT_CORE_DIR ?? '/workspace/rea-tool/node_modules/playwright-core'); }

export async function startBrowser() {
  const base = process.env.HENGCHUAN_WEB ?? 'http://127.0.0.1:5173';
  assert.match(base, /^http:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/, 'Only loopback application targets are allowed.');
  const temporary = await mkdtemp(join(tmpdir(), 'hengchuan-browser-'));
  await mkdir(join(temporary, 'config')); await mkdir(join(temporary, 'cache'));
  const browser = await engine.chromium.launch({ executablePath: process.env.CHROMIUM_PATH ?? '/usr/bin/chromium', headless: true, chromiumSandbox: true, args: ['--disable-setuid-sandbox', '--disable-dev-shm-usage'], env: { PATH: '/usr/bin:/bin', LANG: 'C.UTF-8', XDG_CONFIG_HOME: join(temporary, 'config'), XDG_CACHE_HOME: join(temporary, 'cache') } });
  const context = await browser.newContext({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN', timezoneId: 'UTC' });
  const page = await context.newPage();
  const results = [], errors = [], external = [];
  const evidence = new URL('./evidence/', import.meta.url), screenshots = new URL('./evidence/screenshots/', import.meta.url);
  await mkdir(screenshots, { recursive: true });
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error' && /Content Security Policy|Refused to|MIME type|module script/i.test(message.text())) errors.push(message.text());
  });
  page.on('response', response => {
    if (['script', 'stylesheet'].includes(response.request().resourceType()) && response.status() >= 400) errors.push(`Asset HTTP ${response.status()}: ${new URL(response.url()).pathname}`);
  });
  await context.route('**/*', async route => {
    const url = new URL(route.request().url());
    if (url.origin === new URL(base).origin || ['data:', 'blob:'].includes(url.protocol)) return route.continue();
    external.push(url.origin); await route.abort('blockedbyclient');
  });
  const byId = id => page.getByTestId(id);
  async function check(name, action) {
    try { await action(); results.push({ name, status: 'passed' }); console.log(`PASS ${name}`); }
    catch (error) {
      results.push({ name, status: 'failed', error: error.message }); console.error(`FAIL ${name}: ${error.message}`);
      await screenshot(`failure-${results.length}`).catch(() => {});
    }
  }
  async function screenshot(name) { await page.screenshot({ path: new URL(`${name}.png`, screenshots).pathname, fullPage: true }); }
  async function responseFor(path, action, method = 'GET') {
    const wait = page.waitForResponse(response => new URL(response.url()).pathname === path && response.request().method() === method, { timeout: 20_000 });
    await action(); const response = await wait; assert.ok(response.ok(), `${path}: HTTP ${response.status()}`);
    return response.status() === 204 ? undefined : await response.json();
  }
  async function finish() {
    await writeFile(new URL('browser-results.json', evidence), JSON.stringify({ base, ranAt: new Date().toISOString(), chromium: browser.version(), sandbox: true, results, pageErrors: errors, blockedExternalOrigins: [...new Set(external)] }, null, 2));
    await browser.close(); await rm(temporary, { recursive: true, force: true });
    console.log(`${results.filter(r => r.status === 'passed').length}/${results.length} checks passed`);
    process.exitCode = results.some(r => r.status === 'failed') || errors.length || external.length ? 1 : 0;
  }
  return { base, browser, context, page, byId, check, responseFor, screenshot, finish, errors, external };
}
