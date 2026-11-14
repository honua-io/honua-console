import test from 'node:test';
import assert from 'node:assert/strict';
import { mountStudioDraftCreate } from '../../src/Honua.Console.Web/wwwroot/js/studio-draft-create.mjs';

function element(tagName) {
    return { tagName, children: [], value: '', isConnected: true, listeners: {},
        append(...children) { this.children.push(...children); },
        setAttribute() {}, addEventListener(name, listener) { this.listeners[name] = listener; }, remove() {} };
}

test('unreadable-handoff recovery is offered only for a corrupt stored record', () => {
    const original = globalThis.document;
    globalThis.document = { createElement: element };
    try {
        for (const [binding, stored, mayForget] of [
            [undefined, null, false],
            [{ principalId: 'human', environmentId: 'local' }, '{broken', true],
        ]) {
            let form;
            const host = { isConnected: true, querySelector: () => ({ before: value => { form = value; } }) };
            const app = { isConnected: true, workspaceCookieBinding: binding,
                workspace: { workspaceId: 'workspace', storage: { getItem: () => stored } } };
            const mounted = mountStudioDraftCreate({ app, host });
            const reset = form.children.find(child => child.textContent === 'Forget unreadable local handoff');
            assert.equal(reset.hidden, !mayForget);
            assert.equal(form.children.find(child => child.type === 'submit').disabled, true);
            mounted.dispose();
        }
    } finally { globalThis.document = original; }
});

test('editing a completed map key labels the next action as creation', () => {
    const original = globalThis.document;
    globalThis.document = { createElement: element };
    try {
        let form;
        const host = { isConnected: true, querySelector: () => ({ before: value => { form = value; } }) };
        const app = { isConnected: true, workspaceCookieBinding: { principalId: 'human', environmentId: 'local' },
            workspace: { workspaceId: 'workspace', storage: { getItem: () => JSON.stringify({
                packageKey: 'map', commandId: 'command', status: 'completed', draftId: 'draft',
            }) } } };
        const mounted = mountStudioDraftCreate({ app, host });
        const key = form.children.find(child => child.tagName === 'input');
        const submit = form.children.find(child => child.type === 'submit');
        assert.equal(submit.textContent, 'Open map');
        key.value = '';
        key.listeners.input?.();
        assert.equal(key.value, '');
        assert.equal(submit.textContent, 'New map');
        key.value = 'another-map';
        key.listeners.input?.();
        assert.equal(submit.textContent, 'New map');
        key.value = ' map ';
        key.listeners.input?.();
        assert.equal(submit.textContent, 'Open map');
        mounted.dispose();
    } finally { globalThis.document = original; }
});

test('creating a map does not require starting the assistant conversation', () => {
    const original = globalThis.document;
    globalThis.document = { createElement: element };
    try {
        let form;
        const host = { isConnected: true, querySelector: () => ({ before: value => { form = value; } }) };
        const app = { isConnected: true, workspaceCookieBinding: { principalId: 'human', environmentId: 'local' },
            workspace: { storage: { getItem: () => null } } };
        const mounted = mountStudioDraftCreate({ app, host });
        assert.equal(form.children.find(child => child.type === 'submit').disabled, false);
        mounted.dispose();
    } finally { globalThis.document = original; }
});

test('the approval handoff link stays inside the static Console base', () => {
    const original = globalThis.document;
    const ownerDocument = { baseURI: 'https://ops.example/console/' };
    globalThis.document = { createElement: tag => ({ ...element(tag), ownerDocument }) };
    try {
        let form;
        const host = { isConnected: true, querySelector: () => ({ before: value => { form = value; } }) };
        const app = { isConnected: true, workspaceCookieBinding: { principalId: 'human', environmentId: 'local' },
            workspace: { workspaceId: 'workspace', storage: { getItem: () => JSON.stringify({
                packageKey: 'map', commandId: 'command', status: 'awaiting-approval',
                receipt: { operationInstanceId: 'operation-1', proposalId: 'proposal_1' },
            }) } } };
        const mounted = mountStudioDraftCreate({ app, host });
        const inbox = form.children.find(child => child.textContent === 'Review approval in Inbox');
        assert.equal(inbox.hidden, false);
        assert.equal(inbox.href, '/console/inbox?proposalId=proposal_1');
        mounted.dispose();
    } finally { globalThis.document = original; }
});
