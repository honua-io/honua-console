import test from 'node:test';
import assert from 'node:assert/strict';
import { AgentWorkspace, WorkspaceConflict, selectedResource, attachNativeAgent } from '../../src/Honua.Console.Web/wwwroot/js/agent-workspace.mjs';
const storage = () => { const data = new Map(); return { getItem: k => data.get(k), setItem: (k,v) => data.set(k,v) }; };
const response = (value, status = 200) => ({ ok: status < 300, status, json: async () => value });
function client(request) { return new AgentWorkspace({ request, storage: storage(), uuid: () => 'fixed-id' }); }
const nativeSession={nativeAgentAvailable:true,releaseId:'release',tenantId:'tenant',environmentId:'environment',csrfToken:'csrf',principalId:'original-session'};
function qualifiedNativeModule(value) {
 return {...value,workspaceAgentFeatures:{toolApproval:true},createWorkspaceAgentSession:options=>({reconnect(){},refreshTools:async()=>({}),...value.createWorkspaceAgentSession(options)})};
}


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

test('human-selected deployment target is sealed into one turn and cannot change while capture waits', async () => {
    const calls=[];
    const workspace=client(async (path,init)=>{
        calls.push({path,body:init.body && JSON.parse(init.body)});
        if (init.method==='GET') return response({revision:0,targetId:'server-target'});
        return response({turnId:'fixed-id',status:'accepted'});
    });
    workspace.workspaceId='workspace';workspace.context={revision:0,targetId:'server-target'};
    workspace.session={...nativeSession};
    workspace.deployTargetId='ecs-production';
    await workspace.submit('Deploy the reviewed task definition');
    assert.equal(calls.at(-1).body.deployTargetId,'ecs-production');
    const copied=await workspace.contextReference();
    assert.equal(copied.deployTargetId,'ecs-production');

    let release;
    workspace.queue=new Promise(resolve=>{release=resolve;});
    const pending=workspace.submit('Deploy a different target');
    workspace.deployTargetId='lambda-production';workspace.contextGeneration++;
    release();
    await assert.rejects(pending,/authority changed while submitting/);
    assert.equal(calls.filter(call=>call.path.endsWith('/turns')).length,1);
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
    workspace.edit('serviceName','second'); await new Promise(resolve=>setImmediate(resolve)); release(); await save;
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
        if (init.method==='GET') return response({data:{operationId:'service.publish',operationInstanceId:'operation',status:'Completed'}});
        identifiers.push(JSON.parse(init.body).commandId);
        if (++calls===1) throw new Error('connection lost after commit');
        return response({data:{operationId:'service.publish',operationInstanceId:'operation',status:'Completed'}});
    });
    workspace.workspaceId='workspace'; workspace.context={targetId:'target'}; workspace.session={...nativeSession}; workspace.draft={id:'draft',revision:3,fields:{}};
    await assert.rejects(workspace.submitDraft(),/connection lost/);
    assert.equal((await workspace.submitDraft()).data.status,'Completed');
    assert.equal(identifiers.length,1);
    assert.equal(identifiers[0],'fixed-id');
});

test('one native SDK session serializes turns and sees only agent-authorized context', async () => {
    const {attachNativeAgent}=await import('../../src/Honua.Console.Web/wwwroot/js/agent-workspace.mjs');
    let instances=0; let historyReads=0; const seen=[]; const requests=[]; const drafts=[]; let turnReads=0;
    const workspace=client(async(path,init)=>{
        requests.push({path,init});
        if (path.endsWith('/session')) return response(nativeSession);
        if (path.endsWith('/turn')) return response({context:{targetId:'target',tenantId:'tenant',selectedResources:[{kind:'layer',id:'allowed'}],task:++turnReads===1?{kind:'studio-composition',id:'draft',revision:2}:null},localProcessSnapshot:turnReads===2?{projectId:'agent-project',snapshotId:'authorized-snapshot'}:undefined});
        if (path.endsWith('/capabilities')) return response({transcriptSigning:{keys:[{keyId:'trusted-key'}]}});
        if (path.endsWith('/conversation')&&init.method==='GET') return response({messages:++historyReads===1?[]:[{role:'assistant',content:'External participant update'}]});
        if (path.endsWith('/conversation')) return response({messages:[]});
        return response({});
    });
    workspace.workspaceId='workspace'; workspace.session={...nativeSession};
    await attachNativeAgent(workspace,async()=>qualifiedNativeModule({
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
        if (path.endsWith('/turns')) return response({turnId:body.turnId,status:'accepted'});
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
        if (path.endsWith('/session')) return response(nativeSession);
        if (path.endsWith('/turn')) return response({context:{targetId:'target',tenantId:'tenant'}});
        if (path.endsWith('/capabilities')) return response({transcriptSigning:{keys:[{}]}});
        if (path.endsWith('/conversation')&&init.method==='POST') {
            messages.push(JSON.parse(init.body));
            if (++writeCalls===1) throw new Error('response lost');
            return response({id:messages.at(-1).messageId});
        }
        return response({messages:[]});
    });
    workspace.workspaceId='workspace'; workspace.session={...nativeSession};
    await attachNativeAgent(workspace,async()=>qualifiedNativeModule({
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
        return response({turnId:body.turnId,status:'accepted'});
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
        return response({turnId:body.turnId,status:'accepted'});
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
        let families,allowed;
        const workspace=client(async path=>{
            if(path.endsWith('/session'))return response(nativeSession);
            if(path.endsWith('/turn'))return response({context:{targetId:'target',tenantId:'tenant'}});
            if(path.endsWith('/capabilities'))return response({transcriptSigning:{keys:[{}]},workspaceGateway:{schemaVersion:'honua.agent-gateway.capabilities.v1',targetId,workspaceId:'workspace',turnId:'turn',toolFamilies:['honua.workspace.devops']}});
            return response({messages:[]});
        });
        workspace.workspaceId='workspace';workspace.session={...nativeSession};
        await attachNativeAgent(workspace,async()=>qualifiedNativeModule({createWorkspaceTranscriptVerifier:()=>({}),createWorkspaceAgentSession:options=>{families=options.toolFamilies;return {attachDraft(){},chat:async()=>{allowed=options.toolApproval({source:'metadata',classification:{family:'honua.workspace.devops'}});return {status:'completed'};}};}}));
        await workspace.agentSession.submitTurn({turnId:'turn',prompt:'inspect infrastructure'});
        assert.ok(families.includes('honua.workspace.local-process'));
        assert.equal(allowed,targetId==='target');
    }
});

test('local Processes mode clears the previous Studio task before capturing a prompt',async()=>{
    const calls=[];
    const workspace=client(async(path,init)=>{
        const body=JSON.parse(init.body);calls.push({path,body});
        return response(path.endsWith('/context')?{targetId:'target',revision:8,task:null}:{turnId:body.turnId});
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
        return response(path.endsWith('/context') ? {...workspace.context,...body,revision:workspace.context.revision+1} : {turnId:body.turnId});
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

test('shared style prompt cannot inherit retained map selections or a prior map task', async()=>{
 const task={kind:'shared-style',id:'style',revision:7,contentHash:'a'.repeat(64),impactHash:'b'.repeat(64)};
 const calls=[];const workspace=client(async(path,init)=>{
  const body=JSON.parse(init.body);calls.push({path,body});
  return response(path.endsWith('/context') ? {...workspace.context,revision:workspace.context.revision+1,task:body.task,selectedResources:body.selectedResources} : {turnId:body.turnId});
 });
 workspace.workspaceId='workspace';workspace.context={revision:1,targetId:'target',selectedResources:[],task};
 workspace.selectionProvider=()=>[{kind:'layer',id:'unrelated-map-layer'}];
 workspace.sharedStyleProvider=()=>({task,authorityBinding:workspace.session?.principalId,dirty:false,pending:false});
 await workspace.submit('change this style');
 assert.deepEqual(calls[0].body.selectedResources,[]);assert.deepEqual(calls[0].body.task,task);
 assert.equal(JSON.stringify(calls).includes('unrelated-map-layer'),false);
 workspace.context.task={kind:'studio-composition',id:'old-map',revision:2};
 calls.length=0;await assert.rejects(workspace.submit('this style'),/task changed/);
 assert.equal(calls.length,0);
});

test('a slow connection cannot overwrite a later connection',async()=>{
 let sessionReads=0,release;const initial={environmentId:'old',principalId:'old',releaseId:'r'};
 const workspace=client(async(path)=>{
  if(path.endsWith('/session'))return response({environmentId:++sessionReads===1?'a':'b',principalId:'user',releaseId:'r'});
  if(path.includes('workspace-a?')){await new Promise(resolve=>release=resolve);return response({targetId:'a',revision:1});}
  return response({targetId:'b',revision:1});
 });
 workspace.session=initial;workspace.workspaceId='old';workspace.context={targetId:'old',revision:1};
 const first=workspace.connect('workspace-a');await new Promise(resolve=>setImmediate(resolve));
 await workspace.connect('workspace-b');release();await assert.rejects(first,/context changed/);
 assert.equal(workspace.workspaceId,'workspace-b');assert.equal(workspace.context.targetId,'b');assert.equal(workspace.session.environmentId,'b');
});

test('editing during a connection read preserves old authority and unsaved fields',async()=>{
 let release;let closes=0;const initial={environmentId:'old',principalId:'old',releaseId:'r'};
 const workspace=client(async(path)=>{
  if(path.endsWith('/session'))return response({environmentId:'new',principalId:'new',releaseId:'r',sourcePathBase:'/honua'});
  await new Promise(resolve=>release=resolve);return response({targetId:'new',revision:1});
 });
 workspace.session=initial;workspace.workspaceId='old';workspace.context={targetId:'old',revision:1};
 workspace.agentSession={sdkSession:{close(){closes++;}}};
 const pending=workspace.connect('new');await new Promise(resolve=>setImmediate(resolve));
 workspace.edit('layerName','unsaved');release();await assert.rejects(pending,/Save or pause your work/);
 assert.equal(workspace.session,initial);assert.equal(workspace.workspaceId,'old');assert.equal(workspace.context.targetId,'old');
 assert.equal(workspace.dirty.get('layerName'),'unsaved');assert.equal(closes,0);
});

test('failed nonroot profile connection cannot relabel prior context or discard chat',async()=>{
 const initial={environmentId:'old',principalId:'old',sourcePathBase:'/old'};let closes=0;
 const workspace=client(async(path)=>response(path.endsWith('/session')?{environmentId:'new',principalId:'new',sourcePathBase:'/honua'}:{message:'Forbidden'},path.endsWith('/session')?200:403));
 workspace.session=initial;workspace.workspaceId='old';workspace.context={targetId:'old',revision:1};
 const chat=workspace.agentSession={sdkSession:{close(){closes++;}}};
 await assert.rejects(workspace.connect('new'),/Forbidden/);
 assert.equal(workspace.session,initial);assert.equal(workspace.workspaceId,'old');assert.equal(workspace.context.targetId,'old');assert.equal(workspace.agentSession,chat);assert.equal(closes,0);
});

test('copy context is read-only and dirty draft requires an explicit save',async()=>{
 let requests=0,prepares=0;
 const workspace=client(async()=>{requests++;return response({targetId:'target',revision:7});});
 workspace.workspaceId='workspace';workspace.session={principalId:'user',environmentId:'env',csrfToken:'secret'};
 workspace.context={targetId:'target',revision:7,task:null};workspace.prepareContextProvider=()=>async()=>{prepares++;return undefined;};
 workspace.edit('layerName','unsaved');await assert.rejects(workspace.contextReference(),/Save your draft/);
 assert.equal(requests,0);assert.equal(prepares,0);assert.equal(workspace.dirty.get('layerName'),'unsaved');
 workspace.dirty.clear();const reference=await workspace.contextReference();
 assert.deepEqual(reference,{workspaceId:'workspace',viewId:'fixed-id',contextRevision:7,targetId:'target'});
 assert.equal(JSON.stringify(reference).includes('secret'),false);assert.equal(requests,1);
});

test('each native turn clears old catalog and rechecks private capabilities without resetting chat',async()=>{
 const {attachNativeAgent}=await import('../../src/Honua.Console.Web/wwwroot/js/agent-workspace.mjs');
 for(const invalid of ['absent','target','workspace','turn']) {
  let capabilitiesReads=0,instances=0,created,turnNumber=0;let sessionMetadata={...nativeSession};const seen=[],methods=[];
  const workspace=client(async(path,init)=>{
   if(path.endsWith('/session'))return response(sessionMetadata);
   if(path.endsWith('/turn'))return response({context:{targetId:'target',tenantId:'tenant'}});
   if(path.endsWith('/capabilities')){
    capabilitiesReads++;const second=capabilitiesReads===2;
    const gateway={schemaVersion:'honua.agent-gateway.capabilities.v1',targetId:second&&invalid==='target'?'wrong':'target',workspaceId:second&&invalid==='workspace'?'wrong':'workspace',turnId:second&&invalid==='turn'?'wrong':second?'second':'first',toolFamilies:['honua.workspace.devops']};
    return response({transcriptSigning:{keys:[{}]},...(second&&invalid==='absent'?{}:{workspaceGateway:gateway})});
   }
   if(path.endsWith('/mcp')){
    methods.push(JSON.parse(init.body).method);
    const descriptors=[{name:'canonical',family:'honua.workspace.canonical'},...(turnNumber===1?[{name:'local',family:'honua.workspace.local-process'}]:[]),{name:'private',family:'honua.workspace.devops'}];
    return response({tools:descriptors});
   }
   return response({messages:[]});
  });
  workspace.workspaceId='workspace';workspace.session={...nativeSession};
  await attachNativeAgent(workspace,async()=>qualifiedNativeModule({createWorkspaceTranscriptVerifier:()=>({}),createWorkspaceAgentSession:options=>{
   instances++;const messages=[];
   created={tools:[],messages,reconnect(){this.tools=[];},async refreshTools(){turnNumber++;const result=await (await options.fetchImpl('/api/mcp',{method:'POST',body:JSON.stringify({method:'tools/list'})})).json();this.tools=result.tools.filter(tool=>options.toolFamilies.includes(tool.family)&&options.toolApproval({source:'metadata',classification:{family:tool.family}}));return {};},attachDraft(){},async chat(prompt){messages.push(prompt);seen.push(this.tools.map(tool=>tool.name));return {status:'completed'};}};
   return created;
  }}));
  await workspace.agentSession.submitTurn({turnId:'first',prompt:'first'});
  await workspace.agentSession.submitTurn({turnId:'second',prompt:'second'});
  assert.equal(instances,1);assert.deepEqual(seen,[['canonical','local','private'],['canonical']]);
  assert.deepEqual(created.messages,['first','second']);assert.deepEqual(methods,['tools/list','tools/list']);assert.equal(capabilitiesReads,2);
  sessionMetadata={...nativeSession,releaseId:'other-release'};
  await assert.rejects(workspace.agentSession.submitTurn({turnId:'third',prompt:'stale'}),/session changed/);
  assert.deepEqual(created.tools,[]);assert.deepEqual(created.messages,['first','second']);assert.equal(capabilitiesReads,2);
 }
});

test('native discovery failure never falls back to a previous catalog',async()=>{
 const {attachNativeAgent}=await import('../../src/Honua.Console.Web/wwwroot/js/agent-workspace.mjs');let chats=0;
 const workspace=client(async path=>response(path.endsWith('/session')?nativeSession:path.endsWith('/turn')?{context:{targetId:'target',tenantId:'tenant'}}:path.endsWith('/capabilities')?{transcriptSigning:{keys:[{}]}}:{messages:[]}));
 workspace.workspaceId='workspace';workspace.session={...nativeSession};
 await attachNativeAgent(workspace,async()=>qualifiedNativeModule({createWorkspaceTranscriptVerifier:()=>({}),createWorkspaceAgentSession:()=>({attachDraft(){},refreshTools:async()=>({errorMessage:'unavailable'}),chat:async()=>{chats++;}})}));
 await assert.rejects(workspace.agentSession.submitTurn({turnId:'turn',prompt:'hello'}),/catalog could not be refreshed/);assert.equal(chats,0);
});

test('legacy factory without explicit tool approval support fails closed',async()=>{
 const {attachNativeAgent}=await import('../../src/Honua.Console.Web/wwwroot/js/agent-workspace.mjs');
 const workspace=client(async()=>response({}));workspace.session={...nativeSession};
 await assert.rejects(attachNativeAgent(workspace,async()=>({createWorkspaceAgentSession(){throw new Error('must not run');}})),/qualified native editor contract/);
});

test('copy context rejects navigation during its authorized read',async()=>{
 let release;
 const workspace=client(async(path,init)=>{
  if(init.method==='PUT')return response({targetId:'target',revision:8,selectedResources:JSON.parse(init.body).selectedResources});
  await new Promise(resolve=>release=resolve);return response({targetId:'target',revision:7});
 });
 workspace.workspaceId='workspace';workspace.session={principalId:'user',environmentId:'env'};workspace.context={targetId:'target',revision:7,selectedResources:[]};
 const reference=workspace.contextReference();await new Promise(resolve=>setImmediate(resolve));
 await workspace.select([{kind:'layer',id:'later'}]);release();await assert.rejects(reference,/context changed/);
 assert.equal(workspace.context.revision,8);
});

const startupTick=()=>new Promise(resolve=>setImmediate(resolve));
test('delayed creation cannot reconnect over an explicit later selection',async()=>{
 let release;const workspace=client(async(path,init)=>{
  if(path.endsWith('/session'))return response({environmentId:'environment',principalId:'user'});
  if(init.method==='POST'){await new Promise(resolve=>release=resolve);return response({workspaceId:'created-a'});}
  return response({targetId:'b',revision:1});
 });
 const creating=workspace.create();await startupTick();await workspace.connect('explicit-b');release();
 await assert.rejects(creating,/context changed/);assert.equal(workspace.workspaceId,'explicit-b');
});
test('delayed asset import cannot install over another connection or wrapper',async()=>{
 const workspace=client(async()=>response({}));workspace.session={nativeAgentAvailable:true,principalId:'a'};workspace.workspaceId='a';
 let release;const loading=attachNativeAgent(workspace,()=>new Promise(resolve=>release=resolve));
 workspace.contextGeneration++;workspace.workspaceId='b';workspace.session={nativeAgentAvailable:true,principalId:'b'};
 const replacement={sentinel:true};workspace.agentSession=replacement;
 release({workspaceAgentFeatures:{toolApproval:true}});await loading;assert.equal(workspace.agentSession,replacement);
});
test('style becoming dirty during final authority guard prevents adoption',async()=>{
 const workspace=client(async path=>response(path.endsWith('/session')?{environmentId:'new',principalId:'new'}:{targetId:'new',revision:1}));
 workspace.session={environmentId:'old',principalId:'old'};workspace.workspaceId='old';workspace.context={targetId:'old'};
 let dirty=false,calls=0;workspace.sharedStyleProvider=()=>({dirty});workspace.authorityChangeGuard=async()=>{if(++calls===2)dirty=true;};
 await assert.rejects(workspace.connect('new'),/editor changed/);assert.equal(workspace.workspaceId,'old');assert.equal(workspace.context.targetId,'old');
});

test('candidate connection sends its captured partition rather than the prior profile',async()=>{
 const calls=[];const workspace=client(async(path,init)=>{calls.push([path,init.headers]);return response(path.endsWith('/session')?{environmentId:'new',principalId:'new-partition'}:{targetId:'new',revision:1});});
 workspace.session={environmentId:'old',principalId:'old-partition'};workspace.workspaceId='old';
 await workspace.connect('new');
 assert.equal(calls[1][1]['X-Honua-Session-Partition'],'new-partition');
 assert.equal(calls[1][1]['X-Honua-Environment'],'new');
});


test('native turns renew the same authority lease without borrowing a changed partition',async()=>{
 const {attachNativeAgent}=await import('../../src/Honua.Console.Web/wwwroot/js/agent-workspace.mjs');
 let lease={...nativeSession,csrfToken:'renewed-first'};const requests=[];
 const workspace=client(async(path,init)=>{
  if(path.endsWith('/session'))return response(lease);
  requests.push({path,headers:init.headers});
  if(path.endsWith('/turn'))return response({context:{targetId:'target',tenantId:'tenant'}});
  if(path.endsWith('/capabilities'))return response({transcriptSigning:{keys:[{}]}});
  return response({messages:[]});
 });
 workspace.workspaceId='workspace';workspace.session={...nativeSession};
 await attachNativeAgent(workspace,async()=>qualifiedNativeModule({createWorkspaceTranscriptVerifier:()=>({}),createWorkspaceAgentSession:()=>({attachDraft(){},chat:async()=>({status:'completed'})})}));
 await workspace.agentSession.submitTurn({turnId:'first',prompt:'first'});
 const firstCount=requests.length;
 assert.ok(firstCount>0);assert.ok(requests.every(row=>row.headers['X-CSRF-Token']==='renewed-first'));
 lease={...lease,csrfToken:'renewed-second'};
 await workspace.agentSession.submitTurn({turnId:'second',prompt:'second'});
 assert.ok(requests.slice(firstCount).every(row=>row.headers['X-CSRF-Token']==='renewed-second'&&row.headers['X-Honua-Session-Partition']===nativeSession.principalId));
 const priorCount=requests.length;lease={...lease,principalId:'different-operator',csrfToken:'must-not-borrow'};
 await assert.rejects(workspace.agentSession.submitTurn({turnId:'third',prompt:'third'}),/session changed/);
 assert.equal(requests.length,priorCount);
});

test('visible publish form rebinds its latest canonical draft before prompt and preserves local edits',async()=>{
 const calls=[];
 const workspace=client(async(path,init)=>{
  const body=init.body?JSON.parse(init.body):undefined;calls.push({path,body,method:init.method});
  if(init.method==='GET') return response({id:'draft',kind:'publish-layer',revision:2,fields:{layerName:'Remote'}});
  if(init.method==='PATCH') return response({id:'draft',kind:'publish-layer',revision:3,fields:{layerName:'Local'}});
  if(path.endsWith('/turns')) return response({turnId:body.turnId,status:'accepted'});
  return response({revision:11,targetId:'target',task:body.task,selectedResources:body.selectedResources});
 });
 workspace.workspaceId='workspace';workspace.session={...nativeSession};
 workspace.context={revision:10,targetId:'target',task:{kind:'studio-composition',id:'map',revision:1}};
 workspace.draft={id:'draft',kind:'publish-layer',revision:1,fields:{layerName:'Old'}};
 workspace.edit('layerName','Local');workspace.publishViewCurrent=()=>true;
 // No edit is saved by changing focus; the dirty candidate remains until prompt/Save.
 await workspace.focusPublishDraft();assert.equal(workspace.draft.revision,2);assert.equal(workspace.dirty.get('layerName'),'Local');
 assert.equal(workspace.context.task.kind,'publish-layer');
 assert.deepEqual(calls[1].body.task,{id:'draft',revision:2,kind:'publish-layer'});
 assert.equal(calls[1].body.expectedRevision,10);assert.equal(calls.some(call=>call.method==='PATCH'),false);
 // Independently observed post-patch context is what the turn must capture.
 workspace.request=async(path,init)=>{
  if(init.method==='PATCH') return response({id:'draft',kind:'publish-layer',revision:3,fields:{layerName:'Local'}});
  if(init.method==='GET') return response({revision:12,targetId:'target',task:{kind:'publish-layer',id:'draft',revision:3}});
  calls.push({path,body:JSON.parse(init.body)});return response({turnId:JSON.parse(init.body).turnId,status:'accepted'});
 };
 await workspace.submit('Use my saved layer draft');assert.equal(calls.at(-1).body.contextRevision,12);
});

for(const replacement of ['navigation','principal','remote-context']) test(`delayed publish focus cannot reclaim ${replacement}`,async()=>{
 let release,started;const waiting=new Promise(resolve=>started=resolve),calls=[];
 const workspace=client(async(path,init)=>{
  const body=init.body?JSON.parse(init.body):undefined;calls.push({path,body});
  if(init.method==='GET') {started();await new Promise(resolve=>release=resolve);return response({id:'draft',kind:'publish-layer',revision:2,fields:{}});}
  return response({revision:12,targetId:'target',task:body.task,selectedResources:body.selectedResources});
 });
 workspace.workspaceId='workspace';workspace.session={...nativeSession};
 workspace.context={revision:10,targetId:'target',task:{kind:'studio-composition',id:'map',revision:1}};
 workspace.draft={id:'draft',kind:'publish-layer',revision:1,fields:{}};let visible=true;
 const focus=workspace.focusPublishDraft(()=>visible);await waiting;
 let later;
 if(replacement==='navigation'){visible=false;later=workspace.select([{kind:'layer',id:'other'}]);}
 if(replacement==='principal') workspace.session={...workspace.session,principalId:'other'};
 if(replacement==='remote-context') workspace.context={...workspace.context,revision:11,task:{kind:'workflow-package',id:'newer',revision:1}};
 release();assert.equal(await focus,false);await later;
 assert.equal(calls.filter(call=>call.body?.task?.kind==='publish-layer').length,0);
 assert.equal(workspace.draft.revision,1);
});

test('a new publish form clears a retained Studio task without creating or saving a draft',async()=>{
 const calls=[];const workspace=client(async(path,init)=>{const body=JSON.parse(init.body);calls.push({path,body});return response({revision:4,targetId:'target',task:body.task,selectedResources:body.selectedResources});});
 workspace.workspaceId='workspace';workspace.context={revision:3,targetId:'target',task:{kind:'studio-composition',id:'map',revision:1}};
 assert.equal(await workspace.focusPublishDraft(),true);assert.equal(workspace.context.task,null);assert.equal(workspace.draft,undefined);assert.equal(calls.length,1);
});

test('retained dirty publish fields cannot be silently saved by a prompt from another task',async()=>{
 let requests=0;const workspace=client(async()=>{requests++;throw Error('No request allowed');});
 workspace.workspaceId='workspace';workspace.context={revision:3,targetId:'target',task:{kind:'studio-composition',id:'map',revision:1}};
 workspace.draft={id:'draft',revision:1,fields:{}};workspace.edit('layerName','Unsaved');
 await assert.rejects(workspace.submit('Change the map'),/Return to the publish form/);
 assert.equal(requests,0);assert.equal(workspace.dirty.get('layerName'),'Unsaved');
});

test('first draft Save snapshots fields before its asynchronous creation and retains later typing',async()=>{
 let release,started;const waiting=new Promise(resolve=>started=resolve),patches=[];
 const workspace=client(async(path,init)=>{
  if(path.endsWith('/drafts')){started();await new Promise(resolve=>release=resolve);return response({id:'draft',kind:'publish-layer',revision:1,fields:{layerName:'Clicked'}});}
  if(init.method==='GET') return response({revision:2,targetId:'target',task:{kind:'publish-layer',id:'draft',revision:1}});
  patches.push(JSON.parse(init.body));return response({id:'draft',kind:'publish-layer',revision:2,fields:{layerName:'Clicked'}});
 });
 workspace.workspaceId='workspace';workspace.context={revision:1,targetId:'target'};
 workspace.edit('layerName','Clicked');const save=workspace.saveDraft();await waiting;
 workspace.edit('layerName','Later typing');release();await save;
 assert.deepEqual(patches[0].changes,{layerName:'Clicked'});assert.equal(workspace.dirty.get('layerName'),'Later typing');
});

test('remote draft selection cannot transfer unsaved fields to another canonical draft',async()=>{
 let requests=0;const workspace=client(async()=>{requests++;throw Error('No mutation allowed');});
 workspace.workspaceId='workspace';workspace.draft={id:'draft-a',revision:1,fields:{layerName:'Original'}};
 workspace.edit('layerName','Unsaved A');
 await assert.rejects(workspace.reconcileDraft({id:'draft-b',revision:3,fields:{layerName:'Remote B'}}),/Another publish draft/);
 assert.equal(workspace.draft.id,'draft-a');assert.equal(workspace.projectDraft().layerName,'Unsaved A');
 workspace.context={revision:4,targetId:'target',task:{kind:'publish-layer',id:'draft-b',revision:3}};
 workspace.publishViewCurrent=()=>true;
 await assert.rejects(workspace.submit('Use these fields'),/Return to the publish form/);
 assert.equal(requests,0);
 // Same-ID canonical observations may still advance while retaining that draft's field candidate.
 await workspace.reconcileDraft({id:'draft-a',revision:2,fields:{layerName:'Remote A'}});
 assert.equal(workspace.draft.revision,2);assert.equal(workspace.projectDraft().layerName,'Unsaved A');
});

test('unsaved first-form fields cannot attach themselves to a newly observed remote draft',async()=>{
 const workspace=client(async()=>{throw Error('No request allowed');});workspace.edit('layerName','Not yet saved');
 await assert.rejects(workspace.reconcileDraft({id:'remote',revision:1,fields:{}}),/Another publish draft/);
 assert.equal(workspace.draft,undefined);assert.equal(workspace.dirty.get('layerName'),'Not yet saved');
});

function publishClickFixture(phase,{existing=false}={}) {
 let release,started;const waiting=new Promise(resolve=>started=resolve),calls=[];
 let revision=existing?4:1,contextRevision=10;
 const workspace=client(async(path,init)=>{
  const body=init.body?JSON.parse(init.body):undefined;
  const operation=path.endsWith('/submit')?'submit':init.method==='PATCH'?'patch':path.endsWith('/drafts')?'create'
   :path.includes('/drafts/')?'focus-read':path.endsWith('/context')?'focus':'context-read';
  calls.push({operation,path,body});
  if(operation===phase){started();await new Promise(resolve=>release=resolve);}
  if(operation==='create') return response({id:'draft',kind:'publish-layer',revision,fields:{...body.fields}});
  if(operation==='focus-read') return response({id:'draft',kind:'publish-layer',revision,fields:{layerName:'Before'}});
  if(operation==='patch'){revision++;return response({id:'draft',kind:'publish-layer',revision,fields:{...body.changes}});}
  if(operation==='submit') return response({data:{operationId:'service.publish',status:'RequiresApproval',operationInstanceId:'operation'}});
  contextRevision++;
  return response({revision:contextRevision,targetId:'target',task:operation==='focus'&&!existing?null:{kind:'publish-layer',id:'draft',revision},selectedResources:[]});
 });
 workspace.workspaceId='workspace';workspace.session={...nativeSession};
 workspace.context={revision:10,targetId:'target',task:existing?{kind:'publish-layer',id:'draft',revision}:null};
 if(existing) workspace.draft={id:'draft',kind:'publish-layer',revision,fields:{layerName:'Before'}};
 workspace.edit('layerName','Clicked');let visible=true;
 return {workspace,calls,waiting,release:()=>release(),isCurrentView:()=>visible,leave:()=>visible=false};
}

for(const phase of ['focus','create','patch']) test(`mounted Save/Publish orchestration freezes original fields before ${phase}`,async()=>{
 const fixture=publishClickFixture(phase),{workspace,calls}=fixture;
 const fields={layerName:'Clicked',serviceName:'original-service'};
 const action=workspace.savePublishForm(fields,{publish:true,isCurrentView:fixture.isCurrentView});
 await fixture.waiting;fields.layerName='Changed caller object';workspace.edit('layerName','Later typing');
 fixture.release();const receipt=await action;
 assert.equal(receipt.data.status,'RequiresApproval');
 assert.equal(calls.find(call=>call.operation==='create').body.fields.layerName,'Clicked');
 assert.equal(calls.find(call=>call.operation==='patch').body.changes.layerName,'Clicked');
 assert.equal(workspace.dirty.get('layerName'),'Later typing');
 assert.equal(calls.filter(call=>call.operation==='patch').length,1,'publication must not auto-save later typing');
 assert.equal(calls.find(call=>call.operation==='submit').body.expectedRevision,2);
});

for(const phase of ['focus-read','create','context-read']) for(const replacement of ['navigation','principal','environment','release','source-path','workspace','view'])
 test(`mounted action refuses ${replacement} during ${phase}`,async()=>{
  const fixture=publishClickFixture(phase,{existing:phase==='focus-read'}),{workspace,calls}=fixture;
  const action=workspace.savePublishForm({layerName:'Clicked'},{publish:true,isCurrentView:fixture.isCurrentView});
  await fixture.waiting;
  if(replacement==='navigation') fixture.leave();
  if(replacement==='principal') workspace.session={...workspace.session,principalId:'different'};
  if(replacement==='environment') workspace.session={...workspace.session,environmentId:'different'};
  if(replacement==='release') workspace.session={...workspace.session,releaseId:'different'};
  if(replacement==='source-path') workspace.session={...workspace.session,sourcePathBase:'/different'};
  if(replacement==='workspace') workspace.workspaceId='different';
  if(replacement==='view') workspace.viewId='different';
  fixture.release();await assert.rejects(action,/changed|Review|review/);
  assert.equal(calls.some(call=>call.operation==='patch'||call.operation==='submit'),false);
  assert.equal(workspace.dirty.get('layerName'),'Clicked');
  if(phase!=='focus-read') assert.equal(workspace.draft,undefined);
 });

test('mounted Publish refuses an unreviewed newer canonical draft revision',async()=>{
 const fixture=publishClickFixture('focus-read',{existing:true}),{workspace,calls}=fixture;
 const action=workspace.savePublishForm({layerName:'Clicked'},{publish:true,isCurrentView:fixture.isCurrentView});
 await fixture.waiting;
 // A current canonical observation advances while this click is waiting.
 workspace.draft={...workspace.draft,revision:5};fixture.release();
 await assert.rejects(action,/changed|review/);
 assert.equal(calls.some(call=>call.operation==='patch'||call.operation==='submit'),false);
 assert.equal(workspace.dirty.get('layerName'),'Clicked');
});

test('actual chat submission captures verified human file metadata in its original retained turn', async () => {
    const {browserImportSelection}=await import('../../src/Honua.Console.Web/wwwroot/js/dataset-import-selection.mjs');
    browserImportSelection.clear();
    try {
        const calls=[]; const workspace=client(async(path,init)=>{
            const body=JSON.parse(init.body);calls.push({path,body});
            return response({turnId:body.turnId,context:{workspaceId:'workspace',targetId:'target',viewId:workspace.viewId,revision:7},
                browserFileSelection:body.browserFileSelection});
        });
        workspace.workspaceId='workspace';workspace.session={...nativeSession};workspace.context={revision:7,targetId:'target'};
        const file=new File(['data'],'places.geojson');
        const selected=await browserImportSelection.prepare(file,workspace,{hashFile:async()=> 'a'.repeat(64)});
        await workspace.submit('Import this file into public.places');
        assert.equal(calls.length,1);assert.deepEqual(calls[0].body.browserFileSelection,selected);
        assert.equal(browserImportSelection.record.turnId,'fixed-id');
        assert.equal(JSON.stringify(calls[0].body).includes('csrf'),false);
        assert.equal(calls[0].body.browserFileSelection.totalBytes,4);
    } finally {browserImportSelection.clear();}
});

test('actual chat refuses selected-file capture after authority changes while queued', async () => {
    const {browserImportSelection}=await import('../../src/Honua.Console.Web/wwwroot/js/dataset-import-selection.mjs');
    browserImportSelection.clear();
    try {
        const calls=[];const workspace=client(async(...args)=>{calls.push(args);return response({});});
        workspace.workspaceId='workspace';workspace.session={...nativeSession};workspace.context={revision:7,targetId:'target'};
        await browserImportSelection.prepare(new File(['data'],'places.geojson'),workspace,{hashFile:async()=> 'a'.repeat(64)});
        let release;workspace.queue=new Promise(resolve=>release=resolve);
        const submitting=workspace.submit('Import this file');workspace.session={...workspace.session,releaseId:'changed-release'};release();
        await assert.rejects(submitting,/earlier context/);assert.equal(calls.length,0);
    } finally {browserImportSelection.clear();}
});
