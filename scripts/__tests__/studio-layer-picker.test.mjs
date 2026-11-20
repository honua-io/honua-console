import test from 'node:test';
import assert from 'node:assert/strict';
import { StudioLayerPicker, mountHeldEdit, showsHoldRefusal } from '../../src/Honua.Console.Web/wwwroot/js/studio-layer-picker.mjs';

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

function heldFixture() {
  const { app, picker } = fixture();
  const attached = [];
  app.addPublishedLayer = async () => {
    app.pendingEditorApproval = { proposalId: 'proposal 1' };
    return { ok: false, reason: 'Waiting for separate human approval.', approval: { proposalId: 'proposal 1' } };
  };
  app.attachWorkspaceDraft = async draftId => { attached.push(draftId); };
  app.discardWorkspaceApprovalHold = () => { app.pendingEditorApproval = undefined; };
  return { app, picker, attached };
}

test('an addition the server holds reports the hold; a second addition waits for it', async () => {
  const { picker, app } = heldFixture();
  await picker.list();
  assert.deepEqual(await picker.add('roads'), { held: true });
  assert.deepEqual(picker.heldEdit(), { proposalId: 'proposal 1', draftId: 'draft', binding: picker.binding });
  app.addPublishedLayer = async () => assert.fail('a held map must not receive another addition');
  await assert.rejects(picker.add('roads'), /proposal 1 is waiting/i);
});

test('reloading reports whether Studio ended the hold, on the same map and authority only', async () => {
  const { picker, app, attached } = heldFixture();
  await picker.list();
  await picker.add('roads');
  const held = picker.heldEdit();
  assert.equal(await picker.reloadHeld(held), false);
  app.pendingEditorApproval = undefined;
  assert.equal(await picker.reloadHeld(held), true);
  app.workspaceCookieBinding = { ...app.workspaceCookieBinding, principalId: 'another' };
  await assert.rejects(picker.reloadHeld(held), /changed/i);
  assert.deepEqual(attached, ['draft', 'draft']);
});

test('stopping the wait drops the hold and reloads the same map only', async () => {
  const { picker, app, attached } = heldFixture();
  await picker.list();
  await picker.add('roads');
  const held = picker.heldEdit();
  app.workspaceDraftIdentity = { draftId: 'other' };
  assert.equal(picker.heldEdit().draftId, 'other');
  await assert.rejects(picker.discardHeld(held), /changed/i);
  assert.deepEqual(app.pendingEditorApproval, { proposalId: 'proposal 1' });
  app.workspaceDraftIdentity = { draftId: 'draft' };
  await picker.discardHeld(held);
  assert.equal(app.pendingEditorApproval, undefined);
  assert.deepEqual(attached, ['draft']);
});

function node(tagName) {
  return { tagName, children: [], listeners: {}, hidden: false, disabled: false, textContent: '', href: '',
    append(...children) { this.children.push(...children); }, setAttribute() {}, focus() { this.focused = true; },
    addEventListener(name, listener) { this.listeners[name] = listener; } };
}

function mountedHold(onRelease) {
  const { app, picker } = heldFixture();
  const listeners = {};
  app.addEventListener = (name, listener) => { listeners[name] = listener; };
  app.pendingEditorApproval = { proposalId: 'proposal 1' };
  const section = node('section');
  mountHeldEdit({ section, picker, app, onRelease });
  const [notice, inbox, reload, discard] = section.children;
  return { app, notice, inbox, reload, discard, listeners };
}

test('a hold that already exists when the view mounts shows its proposal and Inbox link', () => {
  const original = globalThis.document;
  globalThis.document = { createElement: node };
  try {
    const { app, notice, inbox, reload, listeners } = mountedHold();
    assert.match(notice.textContent, /Proposal proposal 1\./);
    assert.equal(inbox.hidden, false);
    assert.equal(reload.hidden, false);
    assert.match(inbox.href, /\/inbox\?proposalId=proposal%201$/);
    app.pendingEditorApproval = undefined;
    listeners['honua-studio-editor-approval-change']();
    assert.equal(inbox.hidden, true);
    assert.equal(notice.textContent, '');
  } finally { globalThis.document = original; }
});

test('a failed reload after stopping the wait reports the failure and leaves no stale controls', async () => {
  const original = globalThis.document;
  globalThis.document = { createElement: node };
  try {
    const { app, notice, discard } = mountedHold();
    app.attachWorkspaceDraft = async () => { throw new Error('The draft could not be read.'); };
    await discard.listeners.click();
    assert.equal(notice.textContent, 'The draft could not be read.');
    assert.equal(discard.hidden, true);
    assert.equal(notice.focused, true);
  } finally { globalThis.document = original; }
});

test('a reload that leaves the hold in place keeps its own result visible', async () => {
  const original = globalThis.document;
  globalThis.document = { createElement: node };
  try {
    const { app, notice, reload } = mountedHold();
    await reload.listeners.click();
    assert.equal(notice.textContent, 'Still waiting for separate human approval. Proposal proposal 1.');
    app.attachWorkspaceDraft = async () => { throw new Error('The session expired.'); };
    await reload.listeners.click();
    assert.equal(notice.textContent, 'The session expired.');
    assert.equal(reload.hidden, false);
  } finally { globalThis.document = original; }
});

test('stopping the wait tells the picker the hold ended, so its refusal line is cleared', async () => {
  const original = globalThis.document;
  globalThis.document = { createElement: node };
  try {
    let released = 0;
    const { notice, discard } = mountedHold(() => { released += 1; });
    assert.equal(released, 0);
    await discard.listeners.click();
    assert.equal(released, 1);
    assert.equal(notice.textContent, 'Stopped waiting for proposal proposal 1. The map shows the server draft.');
  } finally { globalThis.document = original; }
});

test('only a hold refusal is marked for clearing when the hold ends', () => {
  assert.equal(showsHoldRefusal({ dataset: { holdRefusal: '' } }), true);
  assert.equal(showsHoldRefusal({ dataset: {} }), false);
});

test('a second addition refused by a hold is marked as that hold\'s refusal', async () => {
  const { picker } = heldFixture();
  await picker.list();
  await picker.add('roads');
  const error = await picker.add('roads').catch(caught => caught);
  assert.match(error.message, /Proposal proposal 1 is waiting/);
  assert.equal(error.constructor.name, 'HeldEditRefusal');
});
