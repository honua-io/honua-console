import test from 'node:test';
import assert from 'node:assert/strict';
import { READ_HOLD_MS, hasPendingStudioLeaseTurn, trackStudioLeaseTurns } from '../../src/Honua.Console.Web/wwwroot/js/studio-lease-turns.mjs';

function deferred() {
    let resolve, reject;
    const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
    return { promise, resolve, reject };
}

test('a human save retains its lease until the original action completes, without replay', async () => {
    const action = deferred(); let calls = 0;
    const app = { saveWorkspaceVersion(value) { assert.equal(this, app); assert.equal(value, 'draft'); calls++; return action.promise; } };
    trackStudioLeaseTurns(app); trackStudioLeaseTurns(app);
    const result = app.saveWorkspaceVersion('draft');
    assert.equal(result, action.promise);
    assert.equal(hasPendingStudioLeaseTurn(app), true);
    // The host may poll a fresh lease now, but must defer applying it.
    let rotations = 0;
    const rotate = () => { if (!hasPendingStudioLeaseTurn(app)) rotations++; };
    rotate(); assert.equal(rotations, 0);
    action.resolve('version'); assert.equal(await result, 'version');
    rotate(); assert.equal(rotations, 1); assert.equal(calls, 1);
});

test('a rejected mutation remains rejected and is never retried', async () => {
    const action = deferred(); let calls = 0;
    const app = { createRecipeDraft() { calls++; return action.promise; } };
    trackStudioLeaseTurns(app);
    const result = app.createRecipeDraft();
    const expired = new Error('The host session expired.');
    action.reject(expired);
    await assert.rejects(result, error => error === expired);
    assert.equal(hasPendingStudioLeaseTurn(app), false); assert.equal(calls, 1);
});

test('nested prepare and overlapping reopen keep renewal deferred until every action settles', async () => {
    const prepared = deferred(), saved = deferred(), reopened = deferred();
    const app = { prepareWorkspaceTurn() { return prepared.promise; },
        async saveWorkspaceVersion() { await this.prepareWorkspaceTurn(); return saved.promise; },
        reopenWorkspaceVersion() { return reopened.promise; } };
    trackStudioLeaseTurns(app);
    const save = app.saveWorkspaceVersion(), reopen = app.reopenWorkspaceVersion();
    prepared.resolve(); await prepared.promise;
    assert.equal(hasPendingStudioLeaseTurn(app), true);
    saved.resolve(); await save;
    assert.equal(hasPendingStudioLeaseTurn(app), true);
    reopened.resolve(); await reopen;
    assert.equal(hasPendingStudioLeaseTurn(app), false);
});

test('synchronous failure releases the turn and preserves the thrown error', () => {
    const failure = new Error('authority changed');
    const app = { attachWorkspaceDraft() { throw failure; } };
    trackStudioLeaseTurns(app);
    assert.throws(() => app.attachWorkspaceDraft(), error => error === failure);
    assert.equal(hasPendingStudioLeaseTurn(app), false);
});

test('a pending action in another retained editor cannot hold this editor lease', async () => {
    const action = deferred();
    const first = { createWorkspaceDraft() { return action.promise; } }, second = {};
    trackStudioLeaseTurns(first); trackStudioLeaseTurns(second);
    const result = first.createWorkspaceDraft();
    assert.equal(hasPendingStudioLeaseTurn(first), true);
    assert.equal(hasPendingStudioLeaseTurn(second), false);
    assert.equal(hasPendingStudioLeaseTurn(undefined), false);
    action.resolve(); await result;
});

test('a source read keeps its lease, so the layer picker never sees a mid-read rotation', async () => {
    const read = deferred(), signal = new AbortController().signal;
    const app = { listRecipeSources(value) { assert.equal(value, signal); return read.promise; },
        resolveRecipeSource() { return read.promise; } };
    trackStudioLeaseTurns(app);
    for (const method of ['listRecipeSources', 'resolveRecipeSource']) {
        const result = app[method](signal);
        assert.equal(hasPendingStudioLeaseTurn(app), true, method);
        read.resolve([]); assert.deepEqual(await result, []);
        assert.equal(hasPendingStudioLeaseTurn(app), false, method);
    }
});

test('a stalled source read stops holding the lease after the read hold, and its late settle is not counted', async t => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const stalled = deferred(), save = deferred();
    const app = { listRecipeSources() { return stalled.promise; }, saveWorkspaceVersion() { return save.promise; } };
    trackStudioLeaseTurns(app);
    const read = app.listRecipeSources();
    t.mock.timers.tick(READ_HOLD_MS - 1);
    assert.equal(hasPendingStudioLeaseTurn(app), true);
    t.mock.timers.tick(1);
    assert.equal(hasPendingStudioLeaseTurn(app), false, 'the stalled read no longer defers renewal');
    const saving = app.saveWorkspaceVersion();
    stalled.resolve([]); await read;
    assert.equal(hasPendingStudioLeaseTurn(app), true, 'a late read settle must not release the save');
    save.resolve(); await saving;
    assert.equal(hasPendingStudioLeaseTurn(app), false);
});
