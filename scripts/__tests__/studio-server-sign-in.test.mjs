import test from 'node:test';
import assert from 'node:assert/strict';
import { mountStudioServerSignIn } from '../../src/Honua.Console.Web/wwwroot/js/studio-server-sign-in.mjs';

function host() {
    const children = [];
    return { children, ownerDocument: {createElement: () => ({dataset:{}})},
        querySelector: () => children[0], append: action => children.push(action) };
}

test('Studio keeps a Server sign-in action available when editing later needs reauthentication', () => {
    const view = host();
    const path = '/auth/server/login?profileId=environment&authority=Serving&returnTo=%2Fstudio';
    mountStudioServerSignIn(view, path);
    const action = view.children[0];
    assert.equal(action.textContent, 'Sign in to the Server');
    assert.equal(action.href, path);
    mountStudioServerSignIn(view, path.replace('environment', 'other'));
    assert.equal(view.children.length, 1);
    assert.equal(action.href, path.replace('environment', 'other'));
});

test('Studio does not render arbitrary or absent sign-in destinations', () => {
    for (const path of [undefined, '', '//external.example/', 'https://external.example/', 'javascript:alert(1)']) {
        const view = host();
        mountStudioServerSignIn(view, path);
        assert.equal(view.children.length, 0);
    }
});
