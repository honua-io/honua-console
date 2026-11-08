import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readAdminItems } from '../../e2e/playwright/live/admin-items.mjs';

test('reads every server page and preserves the service scope', async () => {
  const calls = [];
  const items = await readAdminItems(async path => {
    const url = new URL(path, 'http://fixture.invalid');
    calls.push(url);
    const page = Number(url.searchParams.get('page'));
    return { data: { items: [{ layerId: page }], page, pageSize: 1, total: 2 } };
  }, '/api/v1/admin/connections/example/layers/?serviceName=parks%20west');
  assert.deepEqual(items, [{ layerId: 1 }, { layerId: 2 }]);
  assert.deepEqual(calls.map(url => url.searchParams.get('page')), ['1', '2']);
  assert.ok(calls.every(url => url.searchParams.get('serviceName') === 'parks west'));
});

test('an empty registry returns an empty list', async () => {
  assert.deepEqual(await readAdminItems(async () => ({
    data: { items: [], page: 1, pageSize: 50, total: 0 },
  }), '/api/v1/admin/services'), []);
});

test('invalid or stalled pages fail instead of hiding missing seed data', async () => {
  for (const data of [undefined, [], { items: [] },
    { items: [], page: 1, pageSize: 0, total: 1 },
    { items: [], page: 1, pageSize: 50, total: 60 },
    { items: [{}], page: 2, pageSize: 50, total: 1 },
    { items: [{}], page: 1, pageSize: 50, total: -1 }]) {
    await assert.rejects(readAdminItems(async () => ({ data }), '/api/v1/admin/services'), /Invalid admin page/);
  }
});

test('a failed server read propagates', async () => {
  await assert.rejects(readAdminItems(async () => { throw new Error('server read failed'); },
    '/api/v1/admin/services'), /server read failed/);
});

test('the independent witness follows service and layer pages before checking proposals', async () => {
  const { observeConsoleApiWitness } = await import('../../e2e/playwright/live/console-receipt-producer.mjs');
  const pages = [];
  const transport = async ({ url }) => {
    const target = new URL(url);
    const path = target.pathname;
    if (path === '/api/v1/admin/proposals') throw new Error('resource reads complete');
    let body = {};
    if (path === '/api/v1/admin/version') body = { data: { version: 'fixture', sourceRevision: 'revision' } };
    if (path === '/api/v1/admin/connections/') body = { data: [{ connectionId: 'connection' }] };
    if (path === '/api/v1/admin/jobs/job') body = { data: { jobId: 'job' } };
    if (path === '/api/v1/admin/services/' || path.endsWith('/layers/')) {
      const page = Number(target.searchParams.get('page') ?? 1);
      pages.push([path, page, target.searchParams.get('serviceName')]);
      const item = path.endsWith('/layers/')
        ? { layerId: page === 2 ? 7 : 1 }
        : { serviceName: page === 2 ? 'parks west' : 'other' };
      body = { data: { items: [item], page, pageSize: 1, total: 2 } };
    }
    return { status: 200, body: JSON.stringify(body) };
  };
  await assert.rejects(observeConsoleApiWitness({
    endpoint: 'http://127.0.0.1:8088', credential: 'fixture-key', transport,
    boundary: { target: 'local-docker', components: { 'honua-server': 'revision' }, resources: {
      connectionId: 'connection', serviceId: 'parks west', layerIds: { parcels: 7 }, jobs: {}, gp: { jobId: 'job' },
    } },
  }), /resource reads complete/);
  assert.deepEqual(pages.map(([, page]) => page), [1, 2, 1, 2]);
  assert.ok(pages.filter(([path]) => path.endsWith('/layers/')).every(([, , service]) => service === 'parks west'));
});
