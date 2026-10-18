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
    let instances=0; let historyReads=0; const seen=[]; const requests=[];
    const workspace=client(async(path,init)=>{
        requests.push({path,init});
        if (path.endsWith('/turn')) return response({context:{targetId:'target',tenantId:'tenant',selectedResources:[{kind:'layer',id:'allowed'}]}});
        if (path.endsWith('/capabilities')) return response({transcriptSigning:{keys:[{keyId:'trusted-key'}]}});
        if (path.endsWith('/conversation')&&init.method==='GET') return response({messages:++historyReads===1?[]:[{role:'assistant',content:'External participant update'}]});
        if (path.endsWith('/conversation')) return response({messages:[]});
        return response({});
    });
    workspace.workspaceId='workspace'; workspace.session={nativeAgentAvailable:true,releaseId:'release',tenantId:'tenant',environmentId:'environment',csrfToken:'csrf'};
    await attachNativeAgent(workspace,async()=>({
        createWorkspaceTranscriptVerifier:manifest=>({manifest}),
        createWorkspaceAgentSession:options=>{
            instances++;
            return {chat:async(prompt)=>{
                seen.push({prompt,system:options.system(),turn:options.certification.actionId});
                workspace.workspaceId='later-workspace';
                workspace.session={environmentId:'later-environment',csrfToken:'later-csrf'};
                await options.fetchImpl('/api/mcp',{method:'POST',body:'{}'});
                return {status:'completed',text:'Done'};
            }};
        }
    }));
    await Promise.all([
        workspace.agentSession.submitTurn({turnId:'turn-1',prompt:'first',context:{secret:'human-only'}}),
        workspace.agentSession.submitTurn({turnId:'turn-2',prompt:'second',context:{secret:'human-only'}})
    ]);
    assert.equal(instances,1);
    assert.equal(historyReads,2);
    assert.ok(seen[1].system.includes('External participant update'));
    assert.deepEqual(seen.map(row=>row.turn),['turn-1','turn-2']);
    assert.ok(seen.every(row=>row.system.includes('allowed')&&!row.system.includes('human-only')));
    const mcp=requests.filter(row=>row.path.endsWith('/mcp'));
    assert.deepEqual(mcp.map(row=>row.init.headers['X-Honua-Turn']),['turn-1','turn-2']);
    assert.ok(mcp.every(row=>!row.init.headers.Authorization&&!row.init.headers['X-API-Key']));
    assert.ok(mcp.every(row=>row.init.headers['X-Honua-Environment']==='environment' &&
        row.init.headers['X-Honua-Workspace']==='workspace' && row.init.headers['X-CSRF-Token']==='csrf'));
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
        createWorkspaceAgentSession:()=>({chat:async()=>{modelCalls++;return {status:'completed',text:'Assistant response'};}})
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
