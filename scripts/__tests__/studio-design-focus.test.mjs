import test from 'node:test';
import assert from 'node:assert/strict';
import {captureStudioDesignFocus, promptWithStudioDesignFocus, studioDesignFocusLabel} from '../../src/Honua.Console.Web/wwwroot/js/studio-design-focus.mjs';
import {AgentWorkspace} from '../../src/Honua.Console.Web/wwwroot/js/agent-workspace.mjs';

function editor() {
    return {isConnected:true, workspaceCookieBinding:{principalId:'alice',environmentId:'env'},
        workspaceDraftIdentity:{draftId:'draft',generation:2}, composition:{
            selection:[{kind:'component',id:'table-b'}],
            state:{layers:[{id:'layer-a',sourceId:'source-a'}], controls:[],
                widgets:[{id:'table-a',title:'Records',sourceId:'source-a'}, {id:'table-b',title:'Records',sourceId:'source-b'}],
                interactions:[{id:'filter-b',on:{ref:'widget:table-b',event:'selection'},do:{ref:'layer:layer-a',verb:'setFilter'}}]}
        }, selectedDesignRelationship:{interactionId:'filter-b',ownerRef:'widget:table-b'}};
}
const task = revision => ({kind:'studio-composition',id:'draft',revision});
function workspaceFor(app, calls, nativePrompts = []) {
    const workspace = new AgentWorkspace({storage:{getItem(){},setItem(){}}, uuid:()=> 'turn',
        request:async(path, init)=> {
            const body=init.body && JSON.parse(init.body); calls.push({path,body});
            return {ok:true,status:200,json:async()=>path.endsWith('/context')
                ? {...body,revision:5} : {turnId:'turn',status:'accepted'}};
        }});
    workspace.workspaceId='workspace'; workspace.context={revision:4,task:task(2)};
    workspace.session={principalId:'alice',environmentId:'env'};
    workspace.studioDesignFocusProvider=()=>captureStudioDesignFocus(app);
    workspace.agentSession={submitTurn:async turn=>{nativePrompts.push(turn.prompt);return turn;}};
    return workspace;
}

test('duplicate titles preserve source-qualified object and relationship identity',()=>{
    const app=editor(); const focus=captureStudioDesignFocus(app);
    assert.match(focus.label,/component table-b.*source reference source-b.*interaction filter-b/);
    const context=focus.bind(task(2));
    assert.deepEqual(context.targets,[{kind:'component',id:'table-b',sourceReference:'source-b'}]);
    assert.deepEqual(context.relationship,{interactionId:'filter-b',ownerRef:'widget:table-b'});
    assert.equal(context.draftId,'draft'); assert.equal(context.revision,2);
    assert.ok(!JSON.stringify(context).includes('alice'));
});

test('send freezes selected design before queued selection changes and uses prepared revision',async()=>{
    const app=editor(), calls=[], nativePrompts=[];
    const workspace=workspaceFor(app,calls,nativePrompts);
    let release;workspace.queue=new Promise(resolve=>{release=resolve;});
    workspace.prepareContextProvider=()=>async()=>{
        app.workspaceDraftIdentity={draftId:'draft',generation:3};return task(3);
    };
    const pending=workspace.submit('Make this interaction highlight the selection');
    app.composition.selection=[{kind:'component',id:'table-a'}];app.selectedDesignRelationship=undefined;
    release();await pending;
    const turn=calls.find(call=>call.path.endsWith('/turns')).body;
    assert.match(turn.prompt,/"id":"table-b"/);assert.match(turn.prompt,/"interactionId":"filter-b"/);
    assert.match(turn.prompt,/"revision":3/); assert.equal(turn.contextRevision,5);
    assert.equal(nativePrompts[0],turn.prompt);
    assert.deepEqual(calls.find(call=>call.path.endsWith('/context')).body.selectedResources,[]);
});

for(const [name, change] of [
    ['draft',app=>{app.workspaceDraftIdentity={draftId:'other',generation:2};}],
    ['composition',app=>{app.composition.state={...app.composition.state,interactions:[]};}],
    ['authority',app=>{app.workspaceCookieBinding={principalId:'bob',environmentId:'env'};}],
    ['detached view',app=>{app.isConnected=false;}]
]) test(`changed ${name} refuses before a turn can be submitted`,async()=>{
    const app=editor(), calls=[];const workspace=workspaceFor(app,calls);
    let release;workspace.queue=new Promise(resolve=>{release=resolve;});
    const pending=workspace.submit('Change this');change(app);release();
    await assert.rejects(pending,/selected Studio design changed/);
    assert.equal(calls.filter(call=>call.path.endsWith('/turns')).length,0);
});

test('unresolved or unrelated relationship never becomes selected context',()=>{
    const app=editor();app.selectedDesignRelationship={interactionId:'missing',ownerRef:'widget:table-b'};
    assert.throws(()=>captureStudioDesignFocus(app),/design changed/);
    app.selectedDesignRelationship={interactionId:'filter-b',ownerRef:'widget:table-a'};
    assert.throws(()=>captureStudioDesignFocus(app),/design changed/);
    assert.match(studioDesignFocusLabel(()=>captureStudioDesignFocus(app)),/unavailable/);
});

test('cleared selection and other workspace tasks preserve the original prompt',async()=>{
    const app=editor();app.composition.selection=[];
    assert.equal(captureStudioDesignFocus(app),undefined);
    assert.equal(promptWithStudioDesignFocus('Keep my message',undefined,task(2)),'Keep my message');
    const calls=[];const workspace=workspaceFor(app,calls);
    await workspace.submit('Keep my message');
    assert.equal(calls[0].body.prompt,'Keep my message');
});

test('feature zero keeps its source identity and invalid feature values are refused',()=>{
    const app=editor();app.selectedDesignRelationship=undefined;
    app.composition.selection=[{kind:'feature',sourceId:'source-a',featureId:0}];
    assert.deepEqual(captureStudioDesignFocus(app).bind(task(2)).targets,[{kind:'feature',sourceId:'source-a',featureId:0}]);
    app.composition.state.layers=[]; // Table selections need no visual map layer.
    assert.equal(captureStudioDesignFocus(app).bind(task(2)).targets[0].featureId,0);
    app.composition.selection=[{kind:'feature',sourceId:'source-a',featureId:{secret:'not-an-id'}}];
    assert.throws(()=>captureStudioDesignFocus(app),/design changed/);
});

test('existing annotation selections remain valid authoring targets',()=>{
    const app=editor();app.selectedDesignRelationship=undefined;
    app.composition.state.annotations=[{id:'region-1',kind:'region',bbox:[0,0,1,1]}];
    app.composition.selection=[{kind:'region',id:'region-1'}];
    assert.deepEqual(captureStudioDesignFocus(app).bind(task(2)).targets,[{kind:'region',id:'region-1'}]);
    app.composition.selection=[{kind:'region',id:'missing'}];
    assert.throws(()=>captureStudioDesignFocus(app),/design changed/);
});

test('focus cannot be rebound to another task or silently truncate a long user message',()=>{
    const focus=captureStudioDesignFocus(editor());
    assert.throws(()=>focus.bind({kind:'workflow-package',id:'draft',revision:2}),/design changed/);
    assert.throws(()=>promptWithStudioDesignFocus('x'.repeat(16000),focus,task(2)),/Shorten the message/);
});

test('shared-style focus takes precedence over retained Studio selection',async()=>{
    const calls=[];const workspace=workspaceFor(editor(),calls);
    workspace.sharedStyleProvider=()=>({task:{kind:'shared-style',id:'style',revision:1}});
    workspace.studioDesignFocusProvider=()=>{throw new Error('Retained map selection must not be read');};
    workspace.context.task={kind:'shared-style',id:'style',revision:1};
    await workspace.submit('Adjust this shared style',{prepareContext:async()=>workspace.context.task});
    assert.equal(calls.at(-1).body.prompt,'Adjust this shared style');
});

test('non-Studio turns have no additional focus or prompt changes',async()=>{
    const calls=[];const workspace=workspaceFor(editor(),calls);
    workspace.studioDesignFocusProvider=()=>undefined;
    workspace.context.task={kind:'workflow-package',id:'workflow',revision:7};
    await workspace.submit('Inspect this process graph');
    assert.equal(calls.at(-1).body.prompt,'Inspect this process graph');
});
