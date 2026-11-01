import test from 'node:test';
import assert from 'node:assert/strict';
import { StudioLayerPicker } from '../../src/Honua.Console.Web/wwwroot/js/studio-layer-picker.mjs';

const source = { id: 'roads', title: 'Roads', protocol: 'geoservices-feature-service',
  locator: { url: '/rest/services/folder/roads/FeatureServer', layerId: 2 } };
function fixture() {
  const calls = [];
  const app = { isConnected: true, workspaceDraftIdentity: { draftId: 'draft' },
    workspaceCookieBinding: { principalId: 'human', environmentId: 'env', sourcePathBase: '' },
    listRecipeSources: async () => [source],
    addPublishedLayer: async layer => { calls.push(layer); return { ok: true }; } };
  return { app, calls, picker: new StudioLayerPicker(app, () => true) };
}

test('a current human selects a verified published layer for the active draft', async () => {
  const { picker, calls } = fixture();
  assert.deepEqual(await picker.list(), [{ id: 'roads', title: 'Roads', serviceName: 'folder/roads', layerId: 2 }]);
  await picker.add('roads');
  assert.deepEqual(calls, [{ serviceName: 'folder/roads', layerId: 2, layerName: 'Roads' }]);
});

test('an absent draft, a changed draft or changed authority cannot dispatch', async () => {
  const { picker, app, calls } = fixture();
  app.workspaceDraftIdentity = undefined;
  await assert.rejects(picker.list(), /open a map/i);
  app.workspaceDraftIdentity = { draftId: 'draft' };
  await picker.list();
  app.workspaceDraftIdentity = { draftId: 'other' };
  await assert.rejects(picker.add('roads'), /changed/i);
  app.workspaceDraftIdentity = { draftId: 'draft' };
  app.workspaceCookieBinding = { ...app.workspaceCookieBinding, principalId: 'another' };
  await assert.rejects(picker.add('roads'), /changed/i);
  assert.deepEqual(calls, []);
});

test('a late catalog result cannot survive a changed authority', async () => {
  const { picker, app, calls } = fixture();
  let finish;
  app.listRecipeSources = () => new Promise(resolve => { finish = resolve; });
  const pending = picker.list();
  app.workspaceCookieBinding = { ...app.workspaceCookieBinding, environmentId: 'other' };
  finish([source]);
  await assert.rejects(pending, /changed/i);
  await assert.rejects(picker.add('roads'), /refresh|changed/i);
  assert.deepEqual(calls, []);
});

test('disposing or detaching the view prevents a pending choice from dispatching', async () => {
  const { picker, app, calls } = fixture();
  await picker.list();
  app.isConnected = false;
  await assert.rejects(picker.add('roads'), /view changed/i);
  app.isConnected = true;
  picker.dispose();
  await assert.rejects(picker.add('roads'), /refresh|changed/i);
  assert.deepEqual(calls, []);
});

test('a late addition cannot report success after the customer changes maps', async () => {
  const { picker, app } = fixture();
  await picker.list();
  let finish;
  app.addPublishedLayer = () => new Promise(resolve => { finish = resolve; });
  const pending = picker.add('roads');
  app.workspaceDraftIdentity = { draftId: 'other' };
  finish({ ok: true });
  await assert.rejects(pending, /changed/i);
});

test('the Studio mutation receives a guard that expires when its host view closes', async () => {
  const { picker, app } = fixture();
  await picker.list();
  app.addPublishedLayer = async (_layer, options) => {
    assert.equal(options.isCurrent(), true);
    app.isConnected = false;
    assert.equal(options.isCurrent(), false);
    return { ok: false, reason: 'view changed' };
  };
  await assert.rejects(picker.add('roads'), /view changed/i);
});

test('refreshing choices invalidates an in-flight selection before dispatch', async () => {
  const { picker, app } = fixture();
  await picker.list();
  let finish, guard;
  app.addPublishedLayer = (_layer, options) => new Promise(resolve => { finish = resolve; guard = options.isCurrent; });
  const pending = picker.add('roads');
  assert.equal(guard(), true);
  await picker.list();
  assert.equal(guard(), false);
  finish({ ok: true });
  await assert.rejects(pending, /choices changed/i);
});

test('only verified vector FeatureServer identities are selectable', async () => {
  const { picker, app, calls } = fixture();
  app.listRecipeSources = async () => [source,
    { ...source, id: 'raster', protocol: 'raster' },
    { ...source, id: 'bad', locator: { url: '/rest/services/a%2Fb/FeatureServer', layerId: 3 } },
    { ...source, id: 'missing', locator: { url: '/other/path', layerId: 4 } }];
  assert.deepEqual((await picker.list()).map(value => value.id), ['roads']);
  await assert.rejects(picker.add('missing'), /refresh|choose/i);
  assert.deepEqual(calls, []);
});

test('ambiguous catalog identities cannot be selected', async () => {
  const { picker, app, calls } = fixture();
  app.listRecipeSources = async () => [source, { ...source, title: 'Other' }];
  await assert.rejects(picker.list(), /ambiguous/i);
  await assert.rejects(picker.add('roads'), /refresh/i);
  assert.deepEqual(calls, []);
});

test('a renewed lease reloads the catalog before exposing choices', async () => {
  const { picker, app } = fixture();
  app.session = { csrfToken: 'old' };
  let finish, reads = 0;
  app.listRecipeSources = () => ++reads === 1 ? new Promise(resolve => { finish = resolve; }) : Promise.resolve([]);
  const pending = picker.list();
  app.session = { csrfToken: 'new' };
  finish([source]);
  assert.deepEqual(await pending, []);
  assert.equal(reads, 2);
});
