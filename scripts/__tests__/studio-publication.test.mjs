import test from 'node:test';
import assert from 'node:assert/strict';
import { StudioPublicationSession } from '../../src/Honua.Console.Web/wwwroot/js/studio-publication-controls.mjs';

function fixture() {
    const records = new Map();
    const storage = { get length() { return records.size; }, key: index => [...records.keys()][index] ?? null,
        getItem: key => records.get(key) ?? null, setItem: (key, value) => records.set(key, value), removeItem: key => records.delete(key) };
    const version = { itemId: 'item', versionId: 'version', contentHash: 'hash', versionNumber: 1, packageKey: 'map' };
    const calls = [];
    const app = {
        workspace: { storage, workspaceId: 'workspace' }, workspaceDraftIdentity: { draftId: 'draft' },
        workspaceCookieBinding: { principalId: 'human', environmentId: 'local', sourcePathBase: '/honua' },
        reviewWorkspacePublication: async id => { assert.equal(id, version.versionId); return version; },
        publishWorkspaceVersion: async (selected, input, command) => {
            assert.ok([...records.values()].some(raw => JSON.parse(raw).commandId === command), 'persist the command before dispatch');
            calls.push({ selected, input, command });
            return { status: 'awaiting-approval', operationInstanceId: 'operation', proposalId: 'proposal' };
        },
    };
    const session = new StudioPublicationSession(app, { uuid: () => 'original-command' });
    return { app, records, calls, session, version, storage };
}
const input = { intent: { route: '/maps/parcels', visibility: 'organization' } };

test('audience and definition links appear only for a confirmed publication in its original context', async () => {
    const {session, app} = fixture();
    assert.equal(session.audienceLinks(), undefined);
    await session.review('version');
    await session.submit(input);
    assert.equal(session.audienceLinks(), undefined, 'a pending approval is not a published map');
    app.publishWorkspaceVersion = async () => ({status: 'accepted', itemId: 'item', versionId: 'version', requestId: 'request'});
    await session.retry();
    assert.deepEqual(session.audienceLinks(), {
        map: '/maps/published/maps/parcels', definition: '/api/studio/viewer/published/maps/parcels',
    });
    app.workspaceCookieBinding.environmentId = 'another-target';
    assert.equal(session.audienceLinks(), undefined);
    app.workspaceCookieBinding.environmentId = 'local';
    assert.ok(session.audienceLinks());
    session.acknowledge();
    assert.equal(session.audienceLinks(), undefined);
});

for (const change of ['principal', 'environment', 'source-path', 'map']) {
    test(`pending publication permits independent work after changing ${change} and recovers on return`, async () => {
        const { session, app, calls } = fixture();
        await session.review('version'); await session.submit(input);
        const original = session.read();
        const originalBinding = { ...app.workspaceCookieBinding };
        if (change === 'map') app.workspaceDraftIdentity.draftId = 'other-draft';
        else app.workspaceCookieBinding[change === 'principal' ? 'principalId' : change === 'environment' ? 'environmentId' : 'sourcePathBase'] = 'other';
        const independent = new StudioPublicationSession(app, { uuid: () => 'other-command' });
        assert.equal(independent.read(), null);
        await independent.review('version'); await independent.submit(input);
        assert.equal(independent.read().commandId, 'other-command');
        app.workspaceCookieBinding = originalBinding; app.workspaceDraftIdentity.draftId = 'draft';
        assert.deepEqual(session.read(), original);
        await assert.rejects(session.review('version'), /original publication/);
        assert.equal(calls.length, 2);
    });
}

test('legacy workspace journal is recoverable only by its original account and acknowledgement clears both locations', async () => {
    const { session, app, storage, calls, records } = fixture();
    await session.review('version'); await session.submit(input);
    const legacyKey = 'honua.workspace.studio-publication.workspace';
    const raw = JSON.stringify({ ...session.read(), context: JSON.stringify([legacyKey, session.authority(), 'draft']) });
    storage.removeItem(session.key());
    storage.setItem(legacyKey, raw);
    app.workspace.workspaceId = 'another-conversation';
    app.workspaceCookieBinding.principalId = 'other-human';
    const independent = new StudioPublicationSession(app);
    assert.equal(independent.read(), null);
    await independent.review('version');
    assert.equal(storage.getItem(legacyKey), raw);
    assert.equal(calls.length, 1);
    app.workspaceCookieBinding.principalId = 'human';
    assert.equal(session.read().commandId, 'original-command');
    app.publishWorkspaceVersion = async (_version, _input, command) => {
        assert.equal(command, 'original-command');
        return { status: 'accepted', itemId: 'item', versionId: 'version', requestId: 'request' };
    };
    await session.retry(); session.acknowledge();
    assert.equal(session.read(), null); assert.equal(records.size, 0);
});

test('a new conversation recovers the same map command without opening a second proposal', async () => {
    const { session, app, calls } = fixture();
    await session.review('version');
    const publish = app.publishWorkspaceVersion;
    app.publishWorkspaceVersion = async (...args) => { await publish(...args); throw new Error('response lost'); };
    await assert.rejects(session.submit(input), /response lost/);
    app.workspace.workspaceId = 'new-conversation';
    const restored = new StudioPublicationSession(app, { uuid: () => { throw new Error('must retain original command'); } });
    assert.equal(restored.read().commandId, 'original-command');
    await assert.rejects(restored.review('version'), /original publication/);
    app.publishWorkspaceVersion = publish;
    await restored.retry();
    assert.deepEqual(calls[0], calls[1]);
});

test('changing conversation invalidates an unsubmitted review ticket', async () => {
    const { session, app, calls } = fixture();
    await session.review('version');
    app.workspace.workspaceId = 'new-conversation';
    await assert.rejects(session.submit(input), /Review this saved version/);
    assert.equal(calls.length, 0);
});

test('conflicting legacy commands for the same map refuse another submission', async () => {
    const { session, app, storage, calls } = fixture();
    await session.review('version'); await session.submit(input);
    const original = session.read(); storage.removeItem(session.key());
    for (const [workspace, commandId] of [['workspace', 'original-command'], ['other-workspace', 'other-command']]) {
        const key = `honua.workspace.studio-publication.${workspace}`;
        storage.setItem(key, JSON.stringify({ ...original, commandId, context: JSON.stringify([key, session.authority(), 'draft']) }));
    }
    app.workspace.workspaceId = 'new-conversation';
    await assert.rejects(session.review('version'), /Multiple original publication/);
    assert.equal(calls.length, 1);
});

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
    assert.equal(session.read(), null);
    app.workspaceCookieBinding.principalId = 'human';
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

async function submittedWithResources() {
    const f = fixture();
    f.app.publishWorkspaceVersion = async (...args) => {
        f.calls.push(args);
        return { status: 'awaiting-approval', operationInstanceId: 'operation', proposalId: 'proposal',
            itemId: 'item', versionId: 'version', requestId: 'request' };
    };
    await f.session.review('version'); await f.session.submit(input);
    return f;
}

test('after reload, refreshing a known receipt reads the original version without a review or publish', async () => {
    const { session, app, calls } = await submittedWithResources();
    assert.equal(session.read().receipt.requestId, 'request');
    app.reviewWorkspacePublication = async () => { throw new Error('refresh must not flush draft edits'); };
    let reads = 0;
    app.readWorkspacePublication = async (version, request) => {
        reads++; assert.equal(version, 'version'); assert.equal(request, 'request');
        return { status: 'accepted', itemId: 'item', versionId: 'version', requestId: 'request' };
    };
    const restored = new StudioPublicationSession(app, { uuid: () => { throw new Error('refresh must not mint a command'); } });
    assert.equal((await restored.refresh()).status, 'accepted');
    assert.equal(restored.read().commandId, 'original-command');
    assert.equal(reads, 1); assert.equal(calls.length, 1);
    restored.acknowledge(); assert.equal(restored.read(), null);
});

for (const [proposalStatus, expected, resolved] of [
    ['AwaitingApproval', 'awaiting-approval', false], ['Executing', 'approval-executing', false],
    ['Succeeded', 'approval-completed', false], ['Rejected', 'approval-rejected', true],
    ['Failed', 'approval-failed', true], ['Cancelled', 'approval-cancelled', true], ['RolledBack', 'approval-rolled-back', true],
]) {
    test(`missing publication reads ${proposalStatus} approval without inventing publication success`, async () => {
        const { session, app, calls } = await submittedWithResources();
        app.readWorkspacePublication = async () => { throw Object.assign(new Error('not created'), { status: 404 }); };
        session.readProposal = async id => { assert.equal(id, 'proposal'); return { proposalId: id, status: proposalStatus }; };
        assert.equal((await session.refresh()).status, expected);
        assert.equal(session.read().receipt.status, expected);
        assert.equal(calls.length, 1);
        if (resolved) { session.acknowledge(); assert.equal(session.read(), null); }
        else assert.throws(() => session.acknowledge(), /confirmed outcome/);
    });
}

test('unavailable, unknown or mismatched status reads retain the original pending record', async () => {
    const { session, app, calls } = await submittedWithResources();
    const original = session.read();
    app.readWorkspacePublication = async () => { throw Object.assign(new Error('forbidden'), { status: 403 }); };
    session.readProposal = async () => { throw new Error('must not bypass forbidden publication reads'); };
    await assert.rejects(session.refresh(), /forbidden/);
    app.readWorkspacePublication = async () => ({ status: 'accepted', itemId: 'other', versionId: 'version', requestId: 'request' });
    await assert.rejects(session.refresh(), /does not match/);
    app.readWorkspacePublication = async () => { throw Object.assign(new Error('missing'), { status: 404 }); };
    for (const proposal of [{ proposalId: 'other', status: 'Succeeded' }, ...['FutureStatus', 'constructor', '__proto__', 'toString'].map(status => ({ proposalId: 'proposal', status }))]) {
        session.readProposal = async () => proposal;
        await assert.rejects(session.refresh(), /does not match|outcome is unknown/);
    }
    assert.deepEqual(session.read(), original); assert.equal(calls.length, 1);
});

test('an authority change during status read cannot overwrite the retained record', async () => {
    const { session, app, calls } = await submittedWithResources();
    const original = session.read();
    app.readWorkspacePublication = async () => {
        app.workspaceCookieBinding.principalId = 'other-human';
        return { status: 'accepted', itemId: 'item', versionId: 'version', requestId: 'request' };
    };
    await assert.rejects(session.refresh(), /workspace changed/);
    assert.equal(session.read(), null);
    app.workspaceCookieBinding.principalId = 'human';
    assert.deepEqual(session.read(), original); assert.equal(calls.length, 1);
});

test('a pending receipt cannot attach another version or incomplete resource identifiers', async () => {
    const { session, app } = fixture(); await session.review('version');
    app.publishWorkspaceVersion = async () => ({ status: 'awaiting-approval', operationInstanceId: 'operation', proposalId: 'proposal',
        requestId: 'request', itemId: 'item', versionId: 'other' });
    await assert.rejects(session.submit(input), /resource references do not match/);
    assert.equal(session.read().receipt, undefined);
});
