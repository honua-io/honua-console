import test from 'node:test';
import assert from 'node:assert/strict';
import { StudioPublicationSession } from '../../src/Honua.Console.Web/wwwroot/js/studio-publication-controls.mjs';

function fixture() {
    const records = new Map();
    const storage = { getItem: key => records.get(key) ?? null, setItem: (key, value) => records.set(key, value), removeItem: key => records.delete(key) };
    const version = { itemId: 'item', versionId: 'version', contentHash: 'hash', versionNumber: 1, packageKey: 'map' };
    const calls = [];
    const app = {
        workspace: { storage, workspaceId: 'workspace' }, workspaceDraftIdentity: { draftId: 'draft' },
        workspaceCookieBinding: { principalId: 'human', environmentId: 'local', sourcePathBase: '/honua' },
        reviewWorkspacePublication: async id => { assert.equal(id, version.versionId); return version; },
        publishWorkspaceVersion: async (selected, input, command) => {
            assert.equal(records.size, 1, 'persist the command before dispatch');
            calls.push({ selected, input, command });
            return { status: 'awaiting-approval', operationInstanceId: 'operation', proposalId: 'proposal' };
        },
    };
    const session = new StudioPublicationSession(app, { uuid: () => 'original-command' });
    return { app, records, calls, session, version, storage };
}
const input = { intent: { route: '/maps/parcels', visibility: 'organization' } };

test('review is read-only; publication stores its command and pending receipt without claiming completion', async () => {
    const { session, calls, records } = fixture();
    await session.review('version');
    assert.equal(calls.length, 0); assert.equal(records.size, 0);
    assert.equal((await session.submit(input)).status, 'awaiting-approval');
    assert.equal(session.read().commandId, 'original-command');
    assert.equal(session.read().receipt.proposalId, 'proposal');
    assert.throws(() => session.acknowledge(), /confirmed outcome/);
});

test('lost response survives reload and retries exactly the original command and settings', async () => {
    const { session, app, calls, records } = fixture();
    await session.review('version');
    const publish = app.publishWorkspaceVersion;
    app.publishWorkspaceVersion = async (...args) => { await publish(...args); throw new Error('response lost'); };
    await assert.rejects(session.submit(input), /response lost/);
    assert.equal(calls.length, 1);
    const restored = new StudioPublicationSession(app, { uuid: () => { throw new Error('must not mint another command'); } });
    app.publishWorkspaceVersion = publish;
    assert.equal((await restored.retry()).status, 'awaiting-approval');
    assert.deepEqual(calls[0], calls[1]);
    assert.equal(records.size, 1);
    await assert.rejects(restored.review('version'), /original publication/);
    await assert.rejects(restored.submit({ intent: { route: '/another', visibility: 'public' } }), /original submission/);
    assert.equal(calls.length, 2);
});

test('storage failure prevents the mutation from being sent', async () => {
    const { session, storage, calls } = fixture();
    await session.review('version');
    storage.setItem = () => { throw new Error('storage unavailable'); };
    await assert.rejects(session.submit(input), /storage unavailable/);
    assert.equal(calls.length, 0);
});

for (const change of ['principal', 'environment', 'map']) {
    test(`restored publication refuses a changed ${change} without dispatch`, async () => {
        const { session, app, calls } = fixture();
        await session.review('version'); await session.submit(input);
        if (change === 'map') app.workspaceDraftIdentity.draftId = 'another-draft';
        else app.workspaceCookieBinding[change === 'principal' ? 'principalId' : 'environmentId'] = 'another';
        const restored = new StudioPublicationSession(app);
        await assert.rejects(restored.retry(), /original map, account and environment/);
        assert.equal(calls.length, 1);
    });
}

test('restored publication verifies the original immutable bytes before retry', async () => {
    const { session, app, version, calls } = fixture();
    await session.review('version'); await session.submit(input);
    app.reviewWorkspacePublication = async () => ({ ...version, contentHash: 'changed' });
    await assert.rejects(new StudioPublicationSession(app).retry(), /saved version could not be verified/);
    assert.equal(calls.length, 1);
});

test('double clicks dispatch once and ignore a response after authority changes', async () => {
    const { session, app, calls } = fixture();
    await session.review('version');
    const publish = app.publishWorkspaceVersion;
    let finish;
    app.publishWorkspaceVersion = async (...args) => {
        const receipt = await publish(...args);
        await new Promise(resolve => { finish = resolve; }); return receipt;
    };
    const first = session.submit(input), second = session.submit(input);
    const refusals = [assert.rejects(first, /workspace changed/), assert.rejects(second, /workspace changed/)];
    await new Promise(resolve => setImmediate(resolve));
    app.workspaceCookieBinding.principalId = 'another'; finish();
    await Promise.all(refusals);
    assert.equal(calls.length, 1);
    assert.equal(session.read().receipt, undefined);
});

test('a receipt for another version cannot claim success or clear the pending submission', async () => {
    const { session, app } = fixture();
    await session.review('version');
    app.publishWorkspaceVersion = async () => ({ status: 'accepted', itemId: 'item', versionId: 'other', requestId: 'request' });
    await assert.rejects(session.submit(input), /outcome is unknown/);
    assert.throws(() => session.acknowledge(), /confirmed outcome/);
});

test('a confirmed accepted result can be acknowledged before reviewing another publication', async () => {
    const { session, app } = fixture();
    await session.review('version');
    app.publishWorkspaceVersion = async () => ({ status: 'accepted', itemId: 'item', versionId: 'version', requestId: 'request' });
    await session.submit(input);
    session.acknowledge();
    assert.equal(session.read(), null);
    await session.review('version');
});

test('corrupt recovery records and invalid routes do not dispatch', async () => {
    const { session, storage, calls } = fixture();
    await session.review('version');
    await assert.rejects(session.submit({ intent: { route: '//elsewhere.test', visibility: 'public' } }), /local publication route/);
    storage.setItem(session.key(), '{invalid');
    await assert.rejects(session.retry(), /unreadable/);
    assert.equal(calls.length, 0);
});
