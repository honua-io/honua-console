import test from 'node:test';
import assert from 'node:assert/strict';
import {AgentWorkspace} from '../../src/Honua.Console.Web/wwwroot/js/agent-workspace.mjs';
import {GaSpecReview, reviewSummary, reviewDetails} from '../../src/Honua.Console.Web/wwwroot/js/ga-spec-review.mjs';
function fixture(api) {
    const data = new Map();
    return new GaSpecReview({workspaceId:'w',session:{gaSpecWorkflow:true},authorityKey:()=> 'human',uuid:()=> 'command',
        storage:{getItem:k=>data.get(k),setItem:(k,v)=>data.set(k,v)},api});
}
const plan = {planId:'p',revision:'hash',verdict:'create',execution:'planned'};
test('Apply ignores acceptance success and reads durable evidence; reconnect reads again', async()=>{
    const calls=[];
    const review=fixture(async(path,method,body)=>{calls.push({path,method,body});return path.includes('/apply') ? {runId:'run',execution:'succeeded',evidence:'verified'} : path.includes('/runs/') ? {runId:'run',execution:'interrupted',evidence:'unverified'} : plan;});
    await review.open('p','hash');
    assert.equal((await review.apply()).execution,'interrupted');
    await review.reconnect();
    assert.equal(calls.filter(c=>c.path==='ga/runs/run').length,2);
    await assert.rejects(review.apply(),/already submitted/);
});
test('lost response retains command and reopening never enables blind replay', async()=>{
    const review=fixture(async path=>{if(path.includes('/apply')) throw new Error('lost response');if(path.includes('/runs/')) return {runId:'p',execution:'succeeded',evidence:'verified'};return plan;});
    await review.open('p','hash');await assert.rejects(review.apply(),/lost/);
    await review.open('p','hash');await assert.rejects(review.apply(),/already submitted/);
    assert.equal((await review.reconnect()).execution,'succeeded');
    await assert.rejects(review.apply(),/already submitted/);
});
test('disabled workflow cannot access server and stale handoff fails closed', async()=>{
    const review=fixture(async()=>({...plan,revision:'new'}));
    await assert.rejects(review.open('p','hash'),/reference changed/);
    review.workspace.session.gaSpecWorkflow=false;
    await assert.rejects(review.open('p','hash'),/disabled/);
});
test('navigation cannot adopt a late plan under another authority', async()=>{
    let release;
    const review=fixture(()=>new Promise(resolve=>{release=resolve;}));
    const pending=review.open('p','hash');
    review.workspace.authorityKey=()=> 'other-human';
    release(plan);
    await assert.rejects(pending,/authority changed/);
    assert.equal(review.plan,null);
});
test('Console plan consumes server bundle and canonical saved draft with exact context', async()=>{
    let intent;
    const review=fixture(async(path,method,body)=>{
        if(path==='ga/bundle') return {bundleVersion:'server-version',grammar:{},pathways:[]};
        if(path.includes('/drafts/')) return {revision:3,fields:{connectionId:'connection',schema:'public',table:'roads',layerName:'Roads',serviceName:'Transport'}};
        if(path==='ga/plans') {intent=body;return plan;}
        return plan;
    });
    Object.assign(review.workspace,{draft:{id:'draft',revision:3},dirty:new Map(),viewId:'view',context:{revision:8,targetId:'target'},contextReference:async()=>({workspaceId:'w',viewId:'view',targetId:'target',contextRevision:8})});
    review.workspace.session.environmentId='environment';
    await review.planCurrentDraft();
    assert.deepEqual(intent,{bundleVersion:'server-version',workspaceId:'w',viewId:'view',targetId:'target',environment:'environment',contextRevision:'8',connectionId:'connection',schema:'public',table:'roads',layerName:'Roads',serviceName:'Transport'});
});
for (const execution of ['succeeded','failed','cancelled']) test(`new plan follows durable ${execution} outcome and preserves original command history`, async()=>{
    let active='first';
    const review=fixture(async path=>{
        if(path.includes('/apply')) return {runId:active};
        if(path.includes('/runs/')) return {runId:active,execution,evidence:'unverified'};
        return {...plan,planId:active};
    });
    await review.open('first','hash');await review.apply();
    active='second';
    // The status reader must return the original run, even after next-plan navigation.
    const api=review.workspace.api;
    review.workspace.api=(path,...args)=>path==='ga/runs/first' ? Promise.resolve({runId:'first',execution,evidence:'unverified'}) : api(path,...args);
    await review.open('second','hash');
    assert.equal(review.plan.planId,'second');
    assert.equal(JSON.parse(review.workspace.storage.getItem(`${review.key}.history.command`)).planId,'first');
    assert.equal((await review.apply()).runId,'second');
});
for (const execution of ['queued','running','interrupted','unknown']) test(`new plan remains blocked while prior execution is ${execution}`, async()=>{
    const review=fixture(async path=>path.includes('/apply') ? {runId:'p'} : path.includes('/runs/') ? {runId:'p',execution} : {...plan,planId:path.includes('/second?')?'second':'p'});
    await review.open('p','hash');await review.apply();
    await assert.rejects(review.open('second','hash'),/Resolve the original Apply/);
    assert.equal(review.plan.planId,'p');
});
test('an Apply response claiming terminal success cannot release recovery when durable read is missing', async()=>{
    const review=fixture(async path=>{
        if(path.includes('/apply')) return {runId:'p',execution:'succeeded'};
        if(path.includes('/runs/')) throw new Error('status unavailable');
        return {...plan,planId:path.includes('/second?')?'second':'p'};
    });
    await review.open('p','hash');await assert.rejects(review.apply(),/status unavailable/);
    await assert.rejects(review.open('second','hash'),/status unavailable/);
    assert.equal(review.plan.planId,'p');
});

test('review summary identifies its pinned environment and target independently of next-request context', ()=>{
    const pinned={...plan,spec:{environment:'production',environmentName:'Production',workspaceId:'original-workspace',viewId:'original-tab',targetId:'original-target',targetName:'Roads catalog',contextRevision:'7',layerName:'Roads',serviceName:'Transport',schema:'public',table:'roads'}};
    const summary=reviewSummary(pinned,{execution:'succeeded',evidence:'verified'});
    for (const value of ['Create layer: Roads','Service: Transport','Source table: public.roads','Pinned environment: Production','Pinned target: Roads catalog','Execution: succeeded','Evidence: verified'])
        assert.ok(summary.includes(value),value);
    const details=reviewDetails(pinned,null);
    for (const value of ['Environment ID: production','Workspace ID: original-workspace','View ID: original-tab','Target ID: original-target','Context revision: 7'])
        assert.ok(details.includes(value),value);
    assert.ok(!summary.includes('workspaceId'));
    assert.ok(reviewSummary({...pinned,spec:{...pinned.spec,targetName:undefined}},null).includes('Pinned target: original-target'));
});
function planningFixture(api) {
    const review=fixture(api);
    Object.assign(review.workspace,{draft:{id:'draft',revision:3},dirty:new Map(),viewId:'view',contextGeneration:0,
        context:{revision:8,targetId:'target'},contextReference:async()=>({workspaceId:'w',viewId:'view',targetId:'target',contextRevision:8})});
    review.workspace.session.environmentId='environment';
    return review;
}
const bundle={bundleVersion:'server-version',grammar:{},pathways:[]};
const savedDraft={revision:3,fields:{connectionId:'connection',schema:'public',table:'roads',layerName:'Roads',serviceName:'Transport'}};
for(const phase of ['context','bundle','draft']) test(`planning pins the original draft while ${phase} is pending`,async()=>{
    let release,entered;
    const pendingRead=new Promise(resolve=>{release=resolve;});
    const readEntered=new Promise(resolve=>{entered=resolve;});
    const calls=[];
    const review=planningFixture(async path=>{
        calls.push(path);
        if(path==='ga/bundle') {if(phase==='bundle'){entered();return pendingRead;}return bundle;}
        if(path.includes('/drafts/')) {if(phase==='draft'){entered();return pendingRead;}return savedDraft;}
        return plan;
    });
    if(phase==='context')review.workspace.contextReference=()=>{entered();return pendingRead;};
    const pending=review.planCurrentDraft();
    await readEntered;
    review.workspace.draft={id:'other-draft',revision:3};
    release(phase==='context'?{workspaceId:'w',viewId:'view',targetId:'target',contextRevision:8}:phase==='bundle'?bundle:savedDraft);
    await assert.rejects(pending,/changed/);
    assert.ok(!calls.includes('ga/plans'));
    assert.ok(!calls.some(path=>path.includes('other-draft')));
});
for(const changed of ['authority','environment','view','target','revision','generation','draft-revision','dirty']) test(`planning refuses changed ${changed} before posting`,async()=>{
    let release,entered;
    const pendingRead=new Promise(resolve=>{release=resolve;});
    const readEntered=new Promise(resolve=>{entered=resolve;});
    const calls=[];
    const review=planningFixture(async path=>{calls.push(path);if(path==='ga/bundle'){entered();return pendingRead;}return savedDraft;});
    const pending=review.planCurrentDraft();await readEntered;
    const workspace=review.workspace;
    if(changed==='authority')workspace.authorityKey=()=> 'other-human';
    if(changed==='environment')workspace.session.environmentId='other';
    if(changed==='view')workspace.viewId='other-view';
    if(changed==='target')workspace.context.targetId='other-target';
    if(changed==='revision')workspace.context.revision++;
    if(changed==='generation')workspace.contextGeneration++;
    if(changed==='draft-revision')workspace.draft.revision++;
    if(changed==='dirty')workspace.dirty.set('table','other');
    release(bundle);await assert.rejects(pending,/changed/);
    assert.ok(!calls.includes('ga/plans'));
});
for(const status of [401,403,409]) test(`definitive BFF ${status} rejection retries the same original command after reconnect`,async()=>{
    const calls=[];let refused=false,ids=0;
    const review=fixture(async(path,method,body)=>{
        calls.push({path,body});
        if(path.includes('/apply')) {
            if(!refused){refused=true;const error=new Error('sign in again');error.status=status;error.notDispatched=true;throw error;}
            return {runId:'run'};
        }
        if(path.includes('/runs/'))return {runId:'run',execution:'succeeded'};
        return plan;
    });
    review.workspace.uuid=()=>`command-${++ids}`;
    await review.open('p','hash');await assert.rejects(review.apply(),/sign in/);
    assert.equal(JSON.parse(review.workspace.storage.getItem(review.key)).dispatch,'not-dispatched');
    await review.reconnect();
    assert.equal(calls.filter(call=>call.path.includes('/runs/')).length,0);
    await review.apply();
    assert.equal(ids,1);
    assert.deepEqual(calls.filter(call=>call.path.includes('/apply')).map(call=>call.body.commandId),['command-1','command-1']);
});
for(const status of [401,403,502]) test(`an unmarked ${status} response retains unknown command recovery`,async()=>{
    const review=fixture(async path=>{if(path.includes('/apply')){const error=new Error('upstream refused or unavailable');error.status=status;throw error;}return plan;});
    await review.open('p','hash');await assert.rejects(review.apply(),/upstream/);
    assert.equal(JSON.parse(review.workspace.storage.getItem(review.key)).dispatch,undefined);
    await assert.rejects(review.apply(),/already submitted/);
});
for(const status of [403,409])for(const marked of [false,true])test(`BFF predispatch evidence comes only from its response header (${status}, ${marked})`,async()=>{
    const values=new Map();
    const workspace=new AgentWorkspace({storage:{getItem:key=>values.get(key),setItem:(key,value)=>values.set(key,value)},uuid:()=> 'view',
        request:async()=>new Response(JSON.stringify({notDispatched:true,message:'denied'}),{status,headers:marked?{'X-Honua-Dispatch-Status':'not-dispatched'}:{}})});
    await assert.rejects(workspace.api('ga/plans/p/apply','POST',{}),error=>error.status===status && error.notDispatched===marked);
});
test('late predispatch refusal records only its original authority recovery slot',async()=>{
    let reject,entered,first=true;
    const response=new Promise((resolve,rejectResponse)=>{reject=rejectResponse;});
    const reached=new Promise(resolve=>{entered=resolve;});
    const review=fixture(async path=>{
        if(path.includes('/apply')) {if(first){first=false;entered();return response;}return {runId:'run'};}
        return path.includes('/runs/')?{runId:'run',execution:'succeeded'}:plan;
    });
    await review.open('p','hash');const original=review.key;
    const pending=review.apply();await reached;
    review.workspace.authorityKey=()=> 'other-human';
    const error=new Error('sign in again');error.notDispatched=true;reject(error);
    await assert.rejects(pending,/sign in/);
    assert.equal(JSON.parse(review.workspace.storage.getItem(original)).dispatch,'not-dispatched');
    assert.equal(review.workspace.storage.getItem(review.key),undefined);
    review.workspace.authorityKey=()=> 'human';
    assert.equal((await review.apply()).execution,'succeeded');
});
