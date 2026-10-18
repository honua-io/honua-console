import test from 'node:test';
import assert from 'node:assert/strict';
import { AgentWorkspace, WorkspaceConflict, selectedResource } from '../../src/Honua.Console.Web/wwwroot/js/agent-workspace.mjs';
const storage = () => { const data = new Map(); return { getItem: k => data.get(k), setItem: (k,v) => data.set(k,v) }; };
const response = (value, status = 200) => ({ ok: status < 300, status, json: async () => value });
function client(request) { return new AgentWorkspace({ request, storage: storage(), uuid: () => 'fixed-id' }); }

test('prompt binds original authorized revision while navigation changes in flight', async () => {
    let release; const calls = [];
    const workspace = client(async (path, init) => {
        const body = JSON.parse(init.body); calls.push({path,body});
        if (path.endsWith('/turns')) await new Promise(resolve => { release = resolve; });
        return response(path.endsWith('/context') ? { revision: 8, targetId: 'target-b' } : { turnId: 'fixed-id', status: 'accepted' });
    });
    workspace.workspaceId = 'workspace'; workspace.context = { revision: 7, targetId: 'target-a' };
    const turn = workspace.submit('publish this layer');
    await new Promise(resolve => setImmediate(resolve));
    const navigation=workspace.select([{kind:'layer',id:'other'}]);
    release(); await turn; await navigation;
    assert.equal(calls[0].body.contextRevision, 7);
    assert.equal(calls[0].body.viewId, 'fixed-id');
    assert.equal(workspace.context.revision, 8);
});

test('concurrent remote draft refresh preserves local field edits; stale save retains edits', async () => {
    const workspace = client(async () => response({revision:9},409));
    workspace.workspaceId = 'workspace';
    workspace.draft = { id:'draft', revision:1,fields:{serviceName:'old',layerName:'original'} };
    workspace.edit('serviceName','local');
    await workspace.reconcileDraft({id:'draft',revision:2,fields:{serviceName:'remote',layerName:'changed'}});
    assert.deepEqual(workspace.projectDraft(),{serviceName:'local',layerName:'changed'});
    await assert.rejects(workspace.saveDraft(),WorkspaceConflict);
    assert.equal(workspace.dirty.get('serviceName'),'local');
});

test('save uses field patch and keeps edits made while request is pending', async () => {
    let release; let body;
    const workspace = client(async (_path, init) => { body=JSON.parse(init.body); await new Promise(resolve=>{release=resolve;}); return response({id:'draft',revision:2,fields:{serviceName:'first'}}); });
    workspace.workspaceId='workspace'; workspace.draft={id:'draft',revision:1,fields:{}};
    workspace.edit('serviceName','first'); const save=workspace.saveDraft();
    workspace.edit('serviceName','second'); release(); await save;
    assert.deepEqual(body.changes,{serviceName:'first'});
    assert.equal(workspace.dirty.get('serviceName'),'second');
});

test('secrets and arbitrary presentation URLs cannot enter context', () => {
    const workspace=client(async()=>response({}));
    assert.throws(()=>workspace.edit('password','secret'));
    assert.equal(workspace.presentation({view:'https://attacker.example'}),null);
    assert.equal(workspace.presentation({view:'layer',resourceId:'../admin'}),null);
    assert.deepEqual(selectedResource('/operate/layers/connection-layer-17?token=secret'),[]);
    assert.deepEqual(selectedResource('/operate/layers/connection-layer-17'),[{kind:'layer',id:'17'}]);
    workspace.edit('layerName','dirty');
    assert.equal(workspace.presentation({view:'studio'}),null);
});

test('views are scoped to a tab and workspace conversation survives reconnection', async () => {
    const tabA=client(async()=>response({})); const tabB=new AgentWorkspace({request:async()=>response({}),storage:storage(),uuid:()=> 'other-view'});
    assert.notEqual(tabA.viewId,tabB.viewId);
});

test('lost publication response reuses the persisted command identity', async () => {
    let calls=0; const identifiers=[];
    const workspace=client(async (_path,init)=>{
        if (init.method==='GET') return response({status:'published'});
        identifiers.push(JSON.parse(init.body).commandId);
        if (++calls===1) throw new Error('connection lost after commit');
        return response({status:'published'});
    });
    workspace.workspaceId='workspace'; workspace.draft={id:'draft',revision:3,fields:{}};
    await assert.rejects(workspace.submitDraft(),/connection lost/);
    assert.equal((await workspace.submitDraft()).status,'published');
    assert.equal(identifiers.length,1);
    assert.equal(identifiers[0],'fixed-id');
});

test('one native SDK session serializes turns and sees only agent-authorized context', async () => {
    const {attachNativeAgent}=await import('../../src/Honua.Console.Web/wwwroot/js/agent-workspace.mjs');
    let instances=0; let historyReads=0; const seen=[]; const requests=[]; const drafts=[]; let turnReads=0;
    const workspace=client(async(path,init)=>{
        requests.push({path,init});
        if (path.endsWith('/turn')) return response({context:{targetId:'target',tenantId:'tenant',selectedResources:[{kind:'layer',id:'allowed'}],task:++turnReads===1?{kind:'studio-composition',id:'draft',revision:2}:null},localProcessSnapshot:turnReads===2?{projectId:'agent-project',snapshotId:'authorized-snapshot'}:undefined});
        if (path.endsWith('/capabilities')) return response({transcriptSigning:{keys:[{keyId:'trusted-key'}]}});
        if (path.endsWith('/conversation')&&init.method==='GET') return response({messages:++historyReads===1?[]:[{role:'assistant',content:'External participant update'}]});
        if (path.endsWith('/conversation')) return response({messages:[]});
        return response({});
    });
    workspace.workspaceId='workspace'; workspace.session={nativeAgentAvailable:true,releaseId:'release',tenantId:'tenant',environmentId:'environment',csrfToken:'csrf',principalId:'original-session'};
    await attachNativeAgent(workspace,async()=>({
        createWorkspaceTranscriptVerifier:manifest=>({manifest}),
        createWorkspaceAgentSession:options=>{
            instances++;
            return {attachDraft(value){drafts.push(value);},chat:async(prompt)=>{
                seen.push({prompt,system:options.system(),turn:options.certification.actionId});
                workspace.workspaceId='later-workspace';
                workspace.session={environmentId:'later-environment',csrfToken:'later-csrf'};
                await options.fetchImpl('/api/mcp',{method:'POST',body:'{}'});
                return {status:'completed',text:'Done'};
            }};
        }
    }));
    await Promise.all([
        workspace.agentSession.submitTurn({turnId:'turn-1',prompt:'first',context:{secret:'human-only'},localProcessSnapshot:{projectId:'human-private-project'}}),
        workspace.agentSession.submitTurn({turnId:'turn-2',prompt:'second',context:{secret:'human-only'},localProcessSnapshot:{projectId:'human-private-project'}})
    ]);
    assert.equal(instances,1);
    assert.deepEqual(drafts,[{draftId:'draft',generation:2},undefined]);
    assert.equal(historyReads,2);
    assert.ok(seen[1].system.includes('External participant update'));
    assert.ok(seen[1].system.includes('authorized-snapshot'));
    assert.ok(!seen[0].system.includes('authorized-snapshot'));
    assert.deepEqual(seen.map(row=>row.turn),['turn-1','turn-2']);
    assert.ok(seen.every(row=>row.system.includes('allowed')&&!row.system.includes('human-only')&&!row.system.includes('human-private-project')));
    const mcp=requests.filter(row=>row.path.endsWith('/mcp'));
    assert.deepEqual(mcp.map(row=>row.init.headers['X-Honua-Turn']),['turn-1','turn-2']);
    assert.ok(mcp.every(row=>!row.init.headers.Authorization&&!row.init.headers['X-API-Key']));
    assert.ok(mcp.every(row=>row.init.headers['X-Honua-Environment']==='environment' &&
        row.init.headers['X-Honua-Workspace']==='workspace' && row.init.headers['X-Honua-Session-Partition']==='original-session' && row.init.headers['X-CSRF-Token']==='csrf'));
});

test('prompt flushes shared edits and captures advanced revision before queued navigation', async () => {
    const calls=[];
    const workspace=client(async(path,init)=>{
        const body=init.body ? JSON.parse(init.body):undefined; calls.push({path,body});
        if (init.method==='PATCH') return response({id:'draft',revision:2,fields:{serviceName:'updated'}});
        if (init.method==='GET') return response({revision:4,targetId:'original',task:{id:'draft',revision:2}});
        if (path.endsWith('/turns')) return response({turnId:'turn',status:'accepted'});
        return response({revision:5,targetId:'original',selectedResources:body.selectedResources});
    });
    workspace.workspaceId='workspace'; workspace.context={revision:3,targetId:'original',task:{id:'draft',revision:1}};
    workspace.draft={id:'draft',revision:1,fields:{serviceName:'old'}}; workspace.edit('serviceName','updated');
    await Promise.all([workspace.submit('publish it'),workspace.select([{kind:'layer',id:'next-layer'}])]);
    assert.deepEqual(calls.map(row=>row.path),[
        '/api/agent-workspace/workspace/drafts/draft',
        '/api/agent-workspace/workspace?viewId=fixed-id',
        '/api/agent-workspace/workspace/turns',
        '/api/agent-workspace/workspace/context'
    ]);
    assert.equal(calls[2].body.contextRevision,4);
    assert.equal(workspace.context.selectedResources[0].id,'next-layer');
});

test('assistant persistence retries its stable message without rerunning the model', async () => {
    const {attachNativeAgent}=await import('../../src/Honua.Console.Web/wwwroot/js/agent-workspace.mjs');
    let modelCalls=0; let writeCalls=0; const messages=[];
    const workspace=client(async(path,init)=>{
        if (path.endsWith('/turn')) return response({context:{targetId:'target',tenantId:'tenant'}});
        if (path.endsWith('/capabilities')) return response({transcriptSigning:{keys:[{}]}});
        if (path.endsWith('/conversation')&&init.method==='POST') {
            messages.push(JSON.parse(init.body));
            if (++writeCalls===1) throw new Error('response lost');
            return response({id:messages.at(-1).messageId});
        }
        return response({messages:[]});
    });
    workspace.workspaceId='workspace'; workspace.session={nativeAgentAvailable:true,releaseId:'release',tenantId:'tenant'};
    await attachNativeAgent(workspace,async()=>({
        createWorkspaceTranscriptVerifier:()=>({}),
        createWorkspaceAgentSession:()=>({attachDraft(){},chat:async()=>{modelCalls++;return {status:'completed',text:'Assistant response'};}})
    }));
    await assert.rejects(workspace.agentSession.submitTurn({turnId:'turn',prompt:'hello'}),/response lost/);
    await workspace.agentSession.reconcilePending();
    assert.equal(modelCalls,1);
    assert.deepEqual(messages.map(message=>message.messageId),['turn-assistant','turn-assistant']);
});

test('Studio selection projects exact service/layer references and excludes feature data and URLs', async () => {
    const {studioResourceRefs}=await import('../../src/Honua.Console.Web/wwwroot/js/agent-workspace.mjs');
    const targets=[{kind:'layer',id:'display-layer'},{kind:'feature',sourceId:'source',featureId:'private-feature',geometry:{coordinates:[1,2]}},{kind:'component',id:'chart'}];
    const references=studioResourceRefs(targets,[{id:'display-layer',sourceId:'source'}],[{
        id:'source',title:'Human-only title',locator:{serviceId:'catalog-service',layerId:4,url:'https://private.example?secret=value'}
    }]);
    assert.deepEqual(references,[{kind:'layer',id:'4',serviceId:'catalog-service'}]);
    assert.doesNotMatch(JSON.stringify(references),/secret|https|geometry|feature|title/i);
});

test('queued Studio task binding is retained when prompt selection follows immediately', async () => {
    const calls=[];
    const workspace=client(async(path,init)=>{
        const body=init.body ? JSON.parse(init.body):null;
        calls.push({path,body});
        if(path.endsWith('/context')) return response({revision:2,targetId:'target',selectedResources:body.selectedResources,task:body.task});
        return response({turnId:'turn',status:'accepted'});
    });
    workspace.workspaceId='workspace';workspace.context={revision:1,targetId:'target',selectedResources:[],task:null};
    const task={id:'real-draft',revision:4,kind:'studio-composition',packageKey:'real-package'};
    workspace.select([],task);
    workspace.selectionProvider=()=>[{kind:'layer',id:'1'}];
    await workspace.submit('change this layer');
    assert.deepEqual(calls[1].body.task,task);
    assert.equal(calls.at(-1).body.contextRevision,2);
});

test('pending accepted Studio edit is flushed before capture without retargeting later navigation', async()=>{
    let resolveEdit; const edited=new Promise(resolve=>{resolveEdit=resolve;}); const calls=[];
    const workspace=client(async(path,init)=>{
        const body=init.body?JSON.parse(init.body):null;calls.push({path,body});
        if(path.endsWith('/context'))return response({revision:body.expectedRevision+1,targetId:'target',selectedResources:body.selectedResources,task:body.task});
        return response({turnId:'turn',status:'accepted'});
    });
    const task={id:'draft',revision:1,kind:'studio-composition',packageKey:'package'};
    workspace.workspaceId='workspace';workspace.context={revision:1,targetId:'target',selectedResources:[{kind:'layer',id:'original'}],task};
    workspace.prepareContextProvider=()=>async()=>{await edited;return {...task,revision:2};};
    const turn=workspace.submit('change this layer');
    const navigation=workspace.select([{kind:'layer',id:'later'}]);
    resolveEdit();await Promise.all([turn,navigation]);
    assert.equal(calls[0].body.task.revision,2);
    assert.equal(calls[0].body.selectedResources[0].id,'original');
    assert.equal(calls[1].body.contextRevision,2);
    assert.equal(workspace.context.selectedResources[0].id,'later');
});

test('private native family is enabled only by the matching gateway capability binding',async()=>{
    const {attachNativeAgent}=await import('../../src/Honua.Console.Web/wwwroot/js/agent-workspace.mjs');
    for(const targetId of ['target','other-target']) {
        let families;
        const workspace=client(async path=>{
            if(path.endsWith('/turn'))return response({context:{targetId:'target',tenantId:'tenant'}});
            if(path.endsWith('/capabilities'))return response({transcriptSigning:{keys:[{}]},workspaceGateway:{schemaVersion:'honua.agent-gateway.capabilities.v1',targetId,workspaceId:'workspace',turnId:'turn',toolFamilies:['honua.workspace.devops']}});
            return response({messages:[]});
        });
        workspace.workspaceId='workspace';workspace.session={nativeAgentAvailable:true,releaseId:'release'};
        await attachNativeAgent(workspace,async()=>({createWorkspaceTranscriptVerifier:()=>({}),createWorkspaceAgentSession:options=>{families=options.toolFamilies;return {attachDraft(){},chat:async()=>({status:'completed'})};}}));
        await workspace.agentSession.submitTurn({turnId:'turn',prompt:'inspect infrastructure'});
        assert.equal(families.includes('honua.workspace.devops'),targetId==='target');
    }
});

test('local Processes mode clears the previous Studio task before capturing a prompt',async()=>{
    const calls=[];
    const workspace=client(async(path,init)=>{
        const body=JSON.parse(init.body);calls.push({path,body});
        return response(path.endsWith('/context')?{targetId:'target',revision:8,task:null}:{turnId:'turn'});
    });
    workspace.workspaceId='workspace';workspace.context={targetId:'target',revision:7,task:{kind:'studio-composition',id:'old-draft',revision:2}};
    await workspace.submit('explain the local process',{prepareContext:async()=>null});
    assert.equal(calls[0].body.task,null);
    assert.equal(calls[1].body.contextRevision,8);
    assert.equal(workspace.context.task,null);
});

test('a delayed context observation never overwrites newer navigation or task binding',async()=>{
    let release;
    const workspace=client(async(path,init)=> path.endsWith('/context')
        ? response({revision:2,targetId:'target',task:{kind:'workflow-package',id:'actual-package'}})
        : new Promise(resolve=>{release=()=>resolve(response({revision:1,targetId:'target',task:{kind:'studio-composition',id:'old-draft'}}));}));
    workspace.workspaceId='workspace';workspace.context={revision:1,targetId:'target'};
    const observation=workspace.observeContext();
    await new Promise(resolve=>setImmediate(resolve));
    await workspace.select([],{kind:'workflow-package',id:'actual-package'});
    release();
    assert.equal(await observation,undefined);
    assert.equal(workspace.context.revision,2);
    assert.equal(workspace.context.task.id,'actual-package');
});
for (const navigate of [false,true]) test(`pending local Save ${navigate ? 'rejects navigation' : 'binds accepted same-editor revision'}`,async()=>{
    let saved;const pending=new Promise(resolve=>{saved=resolve;});const calls=[];
    const workspace=client(async(path,init)=>{
        const body=JSON.parse(init.body);calls.push({path,body});
        return response(path.endsWith('/context') ? {...workspace.context,...body,revision:workspace.context.revision+1} : {turnId:'turn'});
    });
    workspace.workspaceId='workspace';workspace.session={principalId:'operator',environmentId:'environment'};
    workspace.context={revision:3,targetId:'target',task:null,selectedResources:[]};
    const task={kind:'local-process-workspace',id:'a'.repeat(32),revision:2,contentHash:'b'.repeat(64)};
    const turn=workspace.submit('inspect accepted source',{prepareContext:async()=>{await pending;return task;}});
    await new Promise(resolve=>setImmediate(resolve));
    const navigation=navigate ? workspace.select([],null) : Promise.resolve();
    saved();
    if(navigate) {
        await assert.rejects(turn,/changed while submitting/);await navigation;
        assert.equal(calls.some(call=>call.path.endsWith('/turns')),false);
    } else {
        await turn;
        assert.deepEqual(calls[0].body.task,task);
        assert.equal(calls[1].body.contextRevision,4);
    }
});

for(const changed of ['principalId','environmentId','workspaceId','viewId']) test(`pending local Save rejects changed ${changed}`,async()=>{
    let saved;const pending=new Promise(resolve=>{saved=resolve;});const calls=[];
    const workspace=client(async(path,init)=>{calls.push(path);return response({turnId:'turn'});});
    workspace.workspaceId='workspace';workspace.session={principalId:'operator',environmentId:'environment'};
    workspace.context={revision:3,targetId:'target',task:null,selectedResources:[]};
    const turn=workspace.submit('inspect accepted source',{prepareContext:async()=>{
        await pending;return {kind:'local-process-workspace',id:'a'.repeat(32),revision:2,contentHash:'b'.repeat(64)};
    }});
    await new Promise(resolve=>setImmediate(resolve));
    if(changed==='principalId'||changed==='environmentId') workspace.session={...workspace.session,[changed]:'changed'};
    else workspace[changed]='changed';
    saved();await assert.rejects(turn,/changed while submitting/);
    assert.equal(calls.length,0);
});

test('a retained Studio task cannot lend a pending prompt to a changed principal',async()=>{
    let saved;const pending=new Promise(resolve=>{saved=resolve;});let calls=0;
    const workspace=client(async()=>{calls++;return response({});});
    workspace.workspaceId='workspace';workspace.session={principalId:'original',environmentId:'environment'};
    const task={kind:'studio-composition',id:'draft',revision:1,packageKey:'map'};
    workspace.context={revision:3,targetId:'target',task};
    const turn=workspace.submit('style this map',{prepareContext:async()=>{await pending;return {...task,revision:2};}});
    await new Promise(resolve=>setImmediate(resolve));
    workspace.session={...workspace.session,principalId:'different'};saved();
    await assert.rejects(turn,/authority changed while submitting/);assert.equal(calls,0);
});
