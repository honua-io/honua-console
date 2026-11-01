import test from 'node:test';
import assert from 'node:assert/strict';
import { hasPendingStudioLeaseTurn, trackStudioLeaseTurns } from '../../src/Honua.Console.Web/wwwroot/js/studio-lease-turns.mjs';

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
