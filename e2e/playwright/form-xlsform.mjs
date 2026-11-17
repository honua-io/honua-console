import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import { startStaticBundleServer } from './static-bundle-server.mjs';
import { createContractApi } from './static-contract-server.mjs';

// Published-source fixture for XLSForm import/export on the forms page (honua-console#973). It answers the
// honua-server#8087 route contract; it is browser proof of the Console, not installed server acceptance.
const { chromium } = await import(process.env.HONUA_PLAYWRIGHT_MODULE ?? 'playwright');
const output = process.env.HONUA_FORM_XLSFORM_OUTPUT ?? 'artifacts/form-xlsform';
const root = process.env.HONUA_CONSOLE_STATIC_ROOT ?? 'artifacts/static-publish/wwwroot/console';
await fs.mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, ...(process.env.HONUA_CHROMIUM_PATH
  ? { executablePath: process.env.HONUA_CHROMIUM_PATH, args: ['--no-sandbox'] } : {}) });
const WORKBOOK_TYPE = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
const BASE = '/api/v1/admin/forms/packages';
const results = [];
const cases = [false, true].flatMap(v4 => [1280, 390].map(width => ({ v4, width })));
try {
  for (const scenario of cases) await runCase(scenario);
} finally {
  await browser.close();
  await fs.writeFile(`${output}/results.json`, JSON.stringify(results, null, 2));
}
assert.equal(results.filter(row => !row.passed).length, 0, 'Every XLSForm case must pass');

function packageVersion(formId, title, version, status) {
  return {
    formId, version, status, etag: `"${formId}-${version}"`, createdAt: '2026-11-17T00:00:00Z',
    package: { schemaVersion: 'honua.form-package.v1', formId, title, target: { serviceId: 'inspections', layerId: 7 },
      sections: [], fields: [{ fieldId: 'asset_id', label: 'Asset ID', type: 'text', targetField: 'asset_id' }] },
  };
}

function summaries(state) {
  const rows = [{ formId: 'form-1', title: 'Hydrant inspection', serviceId: 'inspections', layerId: 7,
    currentPublishedVersion: 2, updatedAt: '2026-11-17T00:00:00Z' }];
  if (state.imported) rows.push({ formId: 'hydrants', title: 'Hydrants', serviceId: 'inspections', layerId: 7,
    currentDraftVersion: 1, updatedAt: '2026-11-17T01:00:00Z' });
  return rows;
}

async function readBody(request) {
  const chunks = [];
  for await (const chunk of request) chunks.push(chunk);
  return Buffer.concat(chunks).toString('latin1');
}

async function handleForms(request, response, url, state) {
  const send = (status, body, headers = { 'Content-Type': 'application/json' }) =>
    response.writeHead(status, headers).end(typeof body === 'string' || Buffer.isBuffer(body) ? body : JSON.stringify(body));
  if (url.pathname === `${BASE}/import` && request.method === 'POST') {
    const body = await readBody(request);
    state.imports.push({ serviceId: url.searchParams.get('serviceId'), layerId: url.searchParams.get('layerId'),
      multipart: /name="?file"?; filename="?[^";]+\.xlsx/.test(body) });
    if (body.includes('range')) {
      return send(400, { title: 'Bad Request', status: 400, detail: 'The workbook is not an XLSForm survey a Honua form package can represent.',
        diagnostics: [{ sheet: 'survey', row: 4, column: 'type', message: "Question type 'range' is not supported." }] });
    }
    state.imported = true;
    return send(201, packageVersion('hydrants', 'Hydrants', 1, 'draft'));
  }
  if (url.pathname === `${BASE}/form-1/export`) {
    state.exports++;
    return send(200, Buffer.from('PK\u0003\u0004fixture-workbook'), { 'Content-Type': WORKBOOK_TYPE,
      'Content-Disposition': 'attachment; filename=form-1-v2.xlsx; filename*=UTF-8\'\'form-1-v2.xlsx' });
  }
  if (url.pathname === BASE) return send(200, summaries(state));
  if (url.pathname === `${BASE}/form-1`) return send(200, packageVersion('form-1', 'Hydrant inspection', 2, 'published'));
  if (url.pathname === `${BASE}/hydrants`) return send(200, packageVersion('hydrants', 'Hydrants', 1, 'draft'));
  return send(404, { title: 'Not Found', status: 404 });
}

async function startFixture(scenario) {
  const state = { imports: [], exports: 0, imported: false };
  let api;
  const server = await startStaticBundleServer(root, 0, {
    configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: scenario.v4 },
    handleApi: async (request, response) => {
      const url = new URL(request.url, 'http://localhost');
      if (url.pathname.startsWith(BASE)) return handleForms(request, response, url, state);
      await api(request, response);
    },
  });
  const origin = `http://127.0.0.1:${server.address().port}`;
  api = createContractApi({ allowedOrigin: origin });
  return { server, origin, state };
}

async function runCase(scenario) {
  const fixture = await startFixture(scenario);
  const context = await browser.newContext({ viewport: { width: scenario.width, height: 900 }, acceptDownloads: true });
  await context.addCookies([{ name: 'console-fixture-session', value: 'issued', url: fixture.origin }]);
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  const row = { ...scenario, passed: false };
  results.push(row);
  try {
    await exercise({ page, fixture, row });
    assert.deepEqual(errors, []);
    row.passed = true;
  } catch (error) { row.error = error.stack; }
  finally {
    row.pageErrors = errors;
    await context.close();
    fixture.server.closeAllConnections();
    await new Promise(resolve => fixture.server.close(resolve));
  }
  console.log(JSON.stringify(row));
}

async function importWorkbook(page, name, content) {
  const panel = page.locator('[data-xlsform-import]');
  await panel.locator('input[type=file]').setInputFiles({ name, mimeType: WORKBOOK_TYPE, buffer: Buffer.from(content) });
  await panel.getByRole('textbox', { name: 'Service id' }).fill('inspections');
  await panel.getByRole('spinbutton', { name: 'Layer id' }).fill('7');
  await panel.getByRole('button', { name: 'Import', exact: true }).click();
}

async function exercise({ page, fixture, row }) {
  const prefix = `${output}/${row.v4 ? 'v4' : 'legacy'}-${row.width}`;
  await page.goto(`${fixture.origin}/console/studio/form`);
  await page.getByRole('button', { name: 'Import XLSForm' }).click();

  await importWorkbook(page, 'bad.xlsx', 'PK survey type range');
  const rows = page.locator('[data-xlsform-diagnostics] tbody tr');
  await rows.first().waitFor();
  assert.equal(await rows.count(), 1);
  assert.match(await rows.first().innerText(), /survey\s+4\s+type\s+Question type 'range' is not supported\./);
  assert.equal(fixture.state.imported, false);
  await page.screenshot({ path: `${prefix}-import-diagnostics.png`, fullPage: true });
  row.overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  assert.ok(row.overflow <= 0, `document overflows by ${row.overflow}px`);

  await importWorkbook(page, 'hydrants.xlsx', 'PK survey text');
  await page.locator('[data-form-builder]').waitFor();
  assert.match(await page.locator('[data-form-builder]').innerText(), /hydrants · v1/);
  assert.deepEqual(fixture.state.imports.map(item => [item.serviceId, item.layerId, item.multipart]),
    [['inspections', '7', true], ['inspections', '7', true]]);

  await page.getByRole('button', { name: 'Back to forms' }).click();
  await page.getByRole('button', { name: 'Hydrant inspection' }).click();
  const downloaded = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export XLSForm' }).click();
  const download = await downloaded;
  row.downloadName = download.suggestedFilename();
  assert.equal(row.downloadName, 'form-1-v2.xlsx');
  assert.equal((await fs.readFile(await download.path())).toString('latin1'), 'PK\u0003\u0004fixture-workbook');
  assert.equal(fixture.state.exports, 1);
  await page.screenshot({ path: `${prefix}-exported.png`, fullPage: true });
}
