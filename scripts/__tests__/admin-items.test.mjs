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
