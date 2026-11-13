import test from 'node:test';
import assert from 'node:assert/strict';
import { StudioDraftCreateSession } from '../../src/Honua.Console.Web/wwwroot/js/studio-draft-create.mjs';

function fixture() {
    const records = new Map();
    const storage = { getItem: key => records.get(key) ?? null, setItem: (key, value) => records.set(key, value), removeItem: key => records.delete(key) };
    const calls = [], attached = [];
    const app = { workspace: { storage, workspaceId: 'workspace' },
        workspaceCookieBinding: { principalId: 'human', environmentId: 'local', sourcePathBase: '' },
        createWorkspaceDraft: async (key, commandId) => {
            assert.ok([...records.values()].some(raw => JSON.parse(raw).commandId === commandId));
            calls.push({ key, commandId });
            return { status: 'awaiting-approval', operationInstanceId: 'operation', proposalId: 'proposal' };
        },
        attachWorkspaceDraft: async (draftId, guard) => { guard?.(); attached.push(draftId); },
    };
    let operation = { operationInstanceId: 'operation', status: 4, proposalId: 'proposal' };
    const options = { uuid: () => 'original-command', readOperation: async () => operation };
    const session = new StudioDraftCreateSession(app, options);
    return { app, session, calls, attached, options, records, complete: value => { operation = value; } };
}

test('retains approval across reload and blocks repeat creates until canonical completion', async () => {
    const f = fixture();
    await Promise.all([f.session.submit('map'), f.session.submit('map')]);
    assert.equal(f.calls.length, 1);
    const restored = new StudioDraftCreateSession(f.app, f.options);
    assert.equal(restored.read().receipt.proposalId, 'proposal');
    await restored.submit('map');
    await assert.rejects(restored.submit('other-map'), /original/i);
    assert.equal(f.calls.length, 1);
    await restored.refresh();
    assert.deepEqual(f.attached, []);
    f.complete({ operationInstanceId: 'operation', status: 1, resourceIds: { draftId: 'canonical-draft' } });
    await restored.refresh();
    await restored.refresh();
    assert.deepEqual(f.attached, ['canonical-draft']);
    assert.equal(restored.read().status, 'completed');
});

test('response loss retries the original command without replacing its input', async () => {
    const f = fixture();
    let attempts = 0;
    f.app.createWorkspaceDraft = async (key, commandId) => {
        f.calls.push({ key, commandId });
        if (++attempts === 1) throw new Error('response lost');
        return { draftId: 'immediate', generation: 0, packageKey: key };
    };
    await assert.rejects(f.session.submit('map'), /response lost/);
    await assert.rejects(f.session.submit('changed'), /original/i);
    await new StudioDraftCreateSession(f.app, f.options).submit('map');
    assert.deepEqual(f.calls, Array(2).fill({ key: 'map', commandId: 'original-command' }));
    assert.equal(f.session.read().status, 'completed');
});

test('unverified, mismatched and rejected operations never attach a draft', async () => {
    for (const operation of [
        { operationInstanceId: 'other', status: 1, resourceIds: { draftId: 'draft' } },
        { operationInstanceId: 'operation', status: 1 },
        { operationInstanceId: 'operation', status: 7, resourceIds: { draftId: 'draft' } },
    ]) {
        const f = fixture();
        await f.session.submit('map');
        f.complete(operation);
        await f.session.refresh().catch(() => {});
        assert.deepEqual(f.attached, []);
        await f.session.submit('map');
        assert.equal(f.calls.length, 1);
    }
});

test('authority or view changes during a status read cannot attach or overwrite the receipt', async () => {
    const f = fixture();
    await f.session.submit('map');
    const session = new StudioDraftCreateSession(f.app, { ...f.options, readOperation: async () => {
        f.app.workspaceCookieBinding.environmentId = 'another';
        return { operationInstanceId: 'operation', status: 1, resourceIds: { draftId: 'draft' } };
    } });
    await assert.rejects(session.refresh(), /changed/i);
    assert.deepEqual(f.attached, []);
    assert.equal(session.read(), null);
});

test('storage failure prevents dispatch and corrupted receipts fail closed', async () => {
    const f = fixture();
    f.app.workspace.storage.setItem = () => { throw new Error('storage full'); };
    await assert.rejects(f.session.submit('map'), /storage full/);
    assert.equal(f.calls.length, 0);
    f.app.workspace.storage.getItem = () => '{broken';
    assert.throws(() => f.session.read());
});


test('a disposed create control cannot attach an approved map after navigation', async () => {
    const f = fixture();
    await f.session.submit('map');
    let finish;
    const session = new StudioDraftCreateSession(f.app, { ...f.options,
        readOperation: () => new Promise(resolve => { finish = resolve; }) });
    const refreshing = session.refresh();
    session.dispose();
    finish({ data: { operationInstanceId: 'operation', status: 1, resourceIds: { draftId: 'draft' } } });
    await assert.rejects(refreshing, /changed/i);
    assert.deepEqual(f.attached, []);
    assert.equal(f.session.read().status, 'awaiting-approval');
});


test('operation status uses the current cookie lease and exact read-only proxy path', async () => {
    const f = fixture();
    await f.session.submit('map');
    f.app.session = { ...f.app.workspaceCookieBinding, csrfToken: 'current-csrf' };
    const original = globalThis.fetch;
    try {
        globalThis.fetch = async (path, options) => {
            assert.equal(path, '/api/processes/operations/handles/operation');
            assert.equal(options.credentials, 'same-origin');
            assert.equal(options.headers.get('X-CSRF-Token'), 'current-csrf');
            assert.equal(options.headers.get('X-Honua-Session-Partition'), 'human');
            return Response.json({ data: { operationInstanceId: 'operation', status: 4 } });
        };
        await new StudioDraftCreateSession(f.app).refresh();
    } finally { globalThis.fetch = original; }
});


test('operation status on a native session reads api/v1 through the Console transport (K3)', async () => {
    const f = fixture();
    await f.session.submit('map');
    let route;
    const transport = { principalId: 'human', assertCurrent: async () => {}, fetch: async path => { route = path; return Response.json({ data: { operationInstanceId: 'operation', status: 4 } }); } };
    f.app.workspaceProcessBinding = { ...f.app.workspaceCookieBinding, transport };
    const original = globalThis.fetch;
    try {
        globalThis.fetch = () => assert.fail('the legacy /api/processes proxy must not be used');
        await new StudioDraftCreateSession(f.app).refresh();
    } finally { globalThis.fetch = original; }
    assert.equal(route, 'api/v1/operations/handles/operation');
});

test('a disconnected host cannot reconcile an in-flight approval', async () => {
    const f = fixture();
    await f.session.submit('map');
    let connected = true, finish;
    const session = new StudioDraftCreateSession(f.app, { ...f.options, isCurrent: () => connected,
        readOperation: () => new Promise(resolve => { finish = resolve; }) });
    const refreshing = session.refresh();
    connected = false;
    finish({ operationInstanceId: 'operation', status: 1, resourceIds: { draftId: 'draft' } });
    await assert.rejects(refreshing, /changed/i);
    assert.deepEqual(f.attached, []);
});


test('conversation reload preserves the original approval under the same authority', async () => {
    const f = fixture();
    await f.session.submit('map');
    f.app.workspace.workspaceId = 'replacement-view';
    const restored = new StudioDraftCreateSession(f.app, f.options);
    assert.equal(restored.read()?.receipt.proposalId, 'proposal');
    await restored.submit('map');
    assert.equal(f.calls.length, 1);
});

test('queued attachment rejects navigation before it can adopt the approved draft', async () => {
    const f = fixture();
    await f.session.submit('map');
    f.complete({ operationInstanceId: 'operation', status: 1, resourceIds: { draftId: 'draft' } });
    f.app.attachWorkspaceDraft = async (draftId, guard) => {
        await Promise.resolve();
        f.app.workspace.workspaceId = 'replacement-view';
        guard?.();
        f.attached.push(draftId);
    };
    await assert.rejects(f.session.refresh(), /changed/i);
    assert.deepEqual(f.attached, []);
});

test('a completed retained action opens its canonical draft again without another create', async () => {
    const f = fixture();
    await f.session.submit('map');
    f.complete({ operationInstanceId: 'operation', status: 1, resourceIds: { draftId: 'draft' } });
    await f.session.refresh();
    f.app.workspaceDraftIdentity = { draftId: 'another-map' };
    await f.session.submit('map');
    assert.deepEqual(f.attached, ['draft', 'draft']);
    assert.equal(f.calls.length, 1);
});

test('acknowledging a terminal outcome allows a fresh action and never resubmits the rejected one', async () => {
    const f = fixture();
    await f.session.submit('map');
    f.complete({ operationInstanceId: 'operation', status: 7 });
    await f.session.refresh();
    f.session.acknowledge();
    assert.equal(f.session.read(), null);
    await f.session.submit('another-map');
    assert.equal(f.calls.length, 2);
});

test('a retained pending action cannot be acknowledged while its outcome is unknown', async () => {
    const f = fixture();
    await f.session.submit('map');
    assert.throws(() => f.session.acknowledge(), /outcome/i);
    assert.equal(f.session.read().status, 'awaiting-approval');
});


test('corrupt local handoffs require explicit acknowledgement before another action', async () => {
    const f = fixture();
    await f.session.submit('map');
    const storageKey = [...f.records.keys()][0];
    f.records.set(storageKey, '{broken');
    await assert.rejects(f.session.submit('map'));
    assert.equal(f.calls.length, 1);
    f.session.acknowledgeUnreadable();
    assert.equal(f.session.read(), null);
    assert.equal(f.calls.length, 1);
    await f.session.submit('map');
    assert.throws(() => f.session.acknowledgeUnreadable(), /readable/i);
    assert.equal(f.session.read().status, 'awaiting-approval');
});

test('a pending create retains its receipt when the selected draft changes before the response', async () => {
    const f = fixture();
    let finish;
    f.app.createWorkspaceDraft = (key, commandId) => {
        f.calls.push({ key, commandId });
        return new Promise(resolve => { finish = resolve; });
    };
    const submitting = f.session.submit('map');
    f.app.workspaceDraftIdentity = { draftId: 'another-map' };
    finish({ status: 'awaiting-approval', operationInstanceId: 'operation', proposalId: 'proposal' });
    await submitting;
    const restored = new StudioDraftCreateSession(f.app, f.options);
    assert.equal(restored.read().receipt.proposalId, 'proposal');
    assert.equal(restored.read().status, 'awaiting-approval');
    await restored.submit('map');
    assert.equal(f.calls.length, 1);
    assert.deepEqual(f.attached, []);
});


test('confirmed create rejection can be acknowledged after reload and corrected with a new command', async () => {
    const f = fixture();
    f.app.createWorkspaceDraft = async () => {
        throw Object.assign(new Error('Package validation failed'), { status: 400, createDraftRejected: true });
    };
    await assert.rejects(f.session.submit('map'), /validation/);
    const restored = new StudioDraftCreateSession(f.app, f.options);
    assert.equal(restored.read().status, 'rejected');
    restored.acknowledge();
    f.app.createWorkspaceDraft = async (key, commandId) => {
        f.calls.push({ key, commandId });
        return { draftId: 'corrected-draft' };
    };
    const corrected = new StudioDraftCreateSession(f.app, { ...f.options, uuid: () => 'corrected-command' });
    await corrected.submit('corrected-map');
    assert.deepEqual(f.calls, [{ key: 'corrected-map', commandId: 'corrected-command' }]);
});

test('unclassified HTTP failures retain the original command because creation may precede a failed attach', async () => {
    for (const status of [400, 403, 409, 500]) {
        const f = fixture();
        f.app.createWorkspaceDraft = async () => { throw Object.assign(new Error('Request failed'), { status }); };
        await assert.rejects(f.session.submit('map'), /failed/);
        assert.equal(f.session.read().status, 'submitting');
        assert.throws(() => f.session.acknowledge(), /confirmed outcome/);
        await assert.rejects(f.session.submit('changed-map'), /original/);
    }
});

test('New map recovery stays partitioned by tenant for the same principal and workspace', async () => {
    const f = fixture();
    f.app.workspaceCookieBinding.tenantId = 'first';
    await f.session.submit('map');
    assert.equal(f.session.read().receipt.proposalId, 'proposal');
    f.app.workspaceCookieBinding.tenantId = 'second';
    const next = new StudioDraftCreateSession(f.app, f.options);
    assert.equal(next.read(), null);
    await next.refresh();
    assert.deepEqual(f.attached, []);
    f.app.workspaceCookieBinding.tenantId = 'first';
    assert.equal(f.session.read().receipt.proposalId, 'proposal');
});


test('sessions without tenant identity keep existing New map recovery keys', async () => {
    const f = fixture();
    await f.session.submit('map');
    const key = `honua.studio.create.${encodeURIComponent(JSON.stringify(['human', 'local', '']))}`;
    assert.ok(f.records.has(key));
    f.app.workspaceCookieBinding.tenantId = undefined;
    assert.equal(new StudioDraftCreateSession(f.app, f.options).read().receipt.proposalId, 'proposal');
});
