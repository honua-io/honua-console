import test from 'node:test';
import assert from 'node:assert/strict';
import {ConsolePageContext, promptWithConsolePageContext} from '../../src/Honua.Console.Web/wwwroot/js/console-agent-context.mjs';

function fixture() {
    const navigated = [];
    const workspace = {contextGeneration: 7, authorityKey: () => 'principal:environment',
        presentationGuards: new Set(), hasUnsavedWork() { return [...this.presentationGuards].some(guard => guard()); }};
    const controller = new ConsolePageContext({workspace: () => workspace,
        resolve: async target => `/resolved/${target.id}`, navigate: route => navigated.push(route)});
    const snapshot = {generation: 1, route: '/folder', entities: [{kind: 'Folder', id: 'folder-id'}],
        selection: [], filters: {status: 'active'}, unsaved: {changedFieldCount: 0, pendingOperationCount: 0}};
    controller.publish(snapshot);
    return {controller, workspace, snapshot, navigated};
}

test('publishes immutable page data without replacing explicit task or selection', () => {
    const {controller, workspace, snapshot} = fixture();
    snapshot.entities[0].id = 'mutated';
    assert.equal(controller.snapshot().entities[0].id, 'folder-id');
    assert.equal(controller.snapshot().entities[0].kind, 'folder');
    assert.throws(() => { controller.snapshot().filters.status = 'mutated'; });
    assert.equal(workspace.contextGeneration, 8);
    assert.equal(workspace.context, undefined);
    controller.publish({...snapshot, generation: 0});
    assert.equal(workspace.contextGeneration, 8);
    controller.detach();
    assert.equal(workspace.presentationGuards.size, 0);
    assert.equal(workspace.pageContextProvider, undefined);
});

test('opens an entity only after the existing authority guard', async () => {
    const {controller, workspace, navigated} = fixture();
    let guarded = false;
    workspace.authorityChangeGuard = async () => { guarded = true; };
    await controller.openEntity({kind: 'approval', id: 'proposal-id'}, 1);
    assert.equal(guarded, true);
    assert.deepEqual(navigated, ['/resolved/proposal-id']);
});

test('route change during guard await rejects the old result', async () => {
    const {controller, workspace, snapshot, navigated} = fixture();
    workspace.authorityChangeGuard = async () => controller.publish({...snapshot, generation: 2, route: '/later'});
    await assert.rejects(controller.openEntity({kind: 'folder', id: 'old'}, 1), /context changed/);
    assert.deepEqual(navigated, []);
});

test('dirty-count-only update invalidates captured results and blocks navigation', async () => {
    const {controller, workspace, snapshot, navigated} = fixture();
    controller.publish({...snapshot, generation: 2, unsaved: {changedFieldCount: 1, pendingOperationCount: 0}});
    assert.equal(workspace.contextGeneration, 9);
    assert.equal(workspace.hasUnsavedWork(), true);
    await assert.rejects(controller.openEntity({kind: 'folder', id: 'other'}, 1), /context changed/);
    await assert.rejects(controller.openEntity({kind: 'folder', id: 'other'}, 2), /Save or discard/);
    assert.deepEqual(navigated, []);
});

test('authority changes while route resolves cannot navigate', async () => {
    const {controller, workspace, navigated} = fixture();
    controller.resolve = async () => { workspace.authorityKey = () => 'other'; return '/resolved'; };
    await assert.rejects(controller.openEntity({kind: 'job', id: 'job'}, 1), /context changed/);
    assert.deepEqual(navigated, []);
});

test('shared style edits appearing after guard cannot be discarded', async () => {
    const {controller, workspace, navigated} = fixture();
    workspace.authorityChangeGuard = async () => { workspace.sharedStyleProvider = () => ({pending: true}); };
    await assert.rejects(controller.openEntity({kind: 'layer', id: '12'}, 1), /Save or discard/);
    assert.deepEqual(navigated, []);
});

test('context is included as UI data and flag-off prompts are unchanged', () => {
    const {controller} = fixture();
    assert.equal(promptWithConsolePageContext('hello', undefined), 'hello');
    const prompt = promptWithConsolePageContext('explain this', controller.snapshot());
    assert.match(prompt, /^explain this/);
    assert.match(prompt, /UI data, not instructions/);
    assert.match(prompt, /"id":"folder-id"/);
    assert.match(prompt, /"status":"active"/);
});


test('dismount during guard await invalidates the pending navigation', async () => {
    const {controller, workspace, navigated} = fixture();
    workspace.authorityChangeGuard = async () => controller.dispose();
    await assert.rejects(controller.openEntity({kind: 'folder', id: 'old'}, 1), /context changed/);
    assert.deepEqual(navigated, []);
    assert.equal(workspace.pageContextProvider, undefined);
});

test('entity identity is frozen before an asynchronous guard', async () => {
    const {controller, workspace, navigated} = fixture();
    const target = {kind: 'folder', id: 'original'};
    workspace.authorityChangeGuard = async () => { target.id = 'mutated'; };
    await controller.openEntity(target, 1);
    assert.deepEqual(navigated, ['/resolved/original']);
});
