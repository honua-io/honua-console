import test from 'node:test';
import assert from 'node:assert/strict';
import { connectStudioHost } from '../../src/Honua.Console.Web/wwwroot/js/studio-bundle.mjs';
import { trackStudioLeaseTurns, hasPendingStudioLeaseTurn } from '../../src/Honua.Console.Web/wwwroot/js/studio-lease-turns.mjs';

class Editor extends EventTarget {
    saveEditorVersion(draft) {
        assert.equal(this instanceof Editor, true);
        assert.equal(draft, 'map-draft');
        this.calls = (this.calls ?? 0) + 1;
        return new Promise((resolve, reject) => { this.complete = resolve; this.fail = reject; });
    }
}

for (const method of ['saveWorkspaceVersion', 'saveEditorVersion']) {
    for (const failed of [false, true]) {
        test(`Studio ${method} preserves lease and ${failed ? 'failure without retry' : 'result'}`, async () => {
            const editor = connectStudioHost(new Editor());
            trackStudioLeaseTurns(editor);
            connectStudioHost(editor); // Reconnecting must not replace the lease-tracked alias.
            const saved = editor[method]('map-draft');
            assert.equal(hasPendingStudioLeaseTurn(editor), true);
            if (failed) {
                const failure = new Error('draft conflict');
                editor.fail(failure);
                await assert.rejects(saved, error => error === failure);
            } else {
                editor.complete({ versionId: 'v1' });
                assert.deepEqual(await saved, { versionId: 'v1' });
            }
            assert.equal(hasPendingStudioLeaseTurn(editor), false);
            assert.equal(editor.calls, 1);
        });
    }
}

test('renamed draft events reach the existing Console listener exactly once with original detail', () => {
    const editor = connectStudioHost(new Editor());
    connectStudioHost(editor);
    const received = [];
    editor.addEventListener('honua-workspace-draft-change', event => received.push(event.detail));
    const detail = { draftId: 'map-draft', packageKey: 'new-map' };
    editor.dispatchEvent(new CustomEvent('honua-studio-draft-change', { detail }));
    assert.equal(received.length, 1);
    assert.equal(received[0], detail);
});
