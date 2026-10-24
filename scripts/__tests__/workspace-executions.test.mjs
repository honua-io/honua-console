import test from 'node:test';
import assert from 'node:assert/strict';
import {AgentWorkspace} from '../../src/Honua.Console.Web/wwwroot/js/agent-workspace.mjs';
import {readExecution,executionSummary,submissionDigest} from '../../src/Honua.Console.Web/wwwroot/js/workspace-executions.mjs';
import {openStudioExecutionResult} from '../../src/Honua.Console.Web/wwwroot/js/workspace-result-navigation.mjs';

const itemId='11111111-1111-4111-8111-111111111111';
const draftId='22222222-2222-4222-8222-222222222222';
const artifact={kind:'studio-composition',itemId,draftId,generation:5,family:'map'};
const session={environmentId:'demo',principalId:'human-partition',environmentName:'Demo',
    releaseId:'candidate',sourcePathBase:'',csrfToken:'private-csrf'};
const response=(value,status=200)=>({ok:status<300,status,json:async()=>value});
const storage=()=>{const data=new Map();return {data,getItem:key=>data.get(key),setItem:(key,value)=>data.set(key,value)};};
const execution=(overrides={})=>({schemaVersion:1,executionId:'original-execution',state:'queued',stateRevision:1,
    createdAt:'2026-10-23T10:00:00Z',updatedAt:'2026-10-23T10:00:00Z',operations:[],artifacts:[],...overrides});
const turn=(turnId,value=execution())=>({turnId,status:'accepted',context:{workspaceId:'workspace',targetId:'target',
    viewId:'original-view',revision:3,selectedResources:[{kind:'layer',id:'7',displayName:'Parcels'}]},execution:value});
function client(request=async()=>response({}),retained=storage()) {
    let next=0;
    const workspace=new AgentWorkspace({request,storage:retained,uuid:()=>`id-${++next}`});
    workspace.workspaceId='workspace';workspace.session={...session};
    workspace.context={workspaceId:'workspace',targetId:'target',revision:3,selectedResources:[]};
    return workspace;
}

test('expired local clock and model prose cannot change a native running observation',()=>{
    const value=readExecution(execution({state:'running',claimExpiresAt:'2000-01-01T00:00:00Z',
        response:'Everything succeeded',href:'https://untrusted.example'}));
    assert.equal(value.state,'running');assert.equal(value.href,undefined);
    assert.equal(executionSummary({acknowledged:true,execution:value}),'Working');
    assert.equal(executionSummary({acknowledged:true,execution:null}),'Message saved. Execution status is not available.');
});

for (const [name,patch] of [
    ['unknown schema',{schemaVersion:2}],['unknown state',{state:'success'}],['invalid revision',{stateRevision:-1}],
    ['unbounded revision',{stateRevision:Number.MAX_SAFE_INTEGER+1}],['invalid timestamp',{updatedAt:'unknown'}],
    ['unsafe execution ID',{executionId:'../another-turn'}],['unknown operation',{operations:[{kind:'url',id:'x',status:'done'}]}],
    ['unsafe proposal',{operations:[{kind:'operation-proposal',id:'x',status:'pending',proposalId:'../../admin'}]}],
    ['foreign artifact kind',{artifacts:[{...artifact,kind:'html'}]}],['invalid draft',{artifacts:[{...artifact,draftId:'../draft'}]}]
]) test(`refuses ${name} rather than displaying completion`,()=>assert.throws(()=>readExecution(execution(patch))));

test('canonical operation names and proposal/job references are projected without arbitrary URLs',()=>{
    const result=readExecution(execution({operations:[{kind:'operation-proposal',id:'operation',operationId:'studio.item.publish',
        proposalId:'proposal',jobId:'native-job',status:'RequiresApproval',href:'https://untrusted.example'}]}));
    assert.equal(result.operations[0].operationId,'studio.item.publish');
    assert.equal(result.operations[0].href,undefined);
});

test('lost POST acknowledgement reads the original turn once and never launches the native model',async()=>{
    const calls=[];let committed;
    const workspace=client(async(path,init)=>{
        calls.push({path,init});
        if (init.method==='POST') {committed=turn(JSON.parse(init.body).turnId);throw new Error('response lost after commit');}
        return response(committed);
    });
    workspace.agentSession={submitTurn(){throw new Error('A second model must not launch');}};
    const result=await workspace.submit('Make a map of these parcels');
    assert.equal(result.externalExecution,true);
    assert.deepEqual(calls.map(call=>call.init.method),['POST','GET']);
    assert.ok(calls[1].path.endsWith(`/turns/${committed.turnId}`));
    const retained=[...workspace.storage.data.entries()].find(([key])=>key.startsWith('honua.workspace.executions:'))[1];
    assert.ok(!retained.includes('Make a map'));assert.ok(!retained.includes('private-csrf'));
    assert.ok(!retained.includes('queued'));assert.equal(JSON.parse(retained)[0].acknowledged,true);
});

test('double clicking the same submission makes one native turn',async()=>{
    let release,posts=0,started;
    const posted=new Promise(resolve=>{started=resolve;});
    const workspace=client(async(_path,init)=>{
        posts++;started();await new Promise(resolve=>{release=resolve;});return response(turn(JSON.parse(init.body).turnId));
    });
    const first=workspace.submit('Make a map');const second=workspace.submit('Make a map');
    await posted;
    await assert.rejects(workspace.submit('Make a different map'),/Wait for the current message/);
    release();const results=await Promise.all([first,second]);
    assert.equal(posts,1);assert.equal(results[0].turnId,results[1].turnId);
});

test('unknown acceptance survives reload and reconciles without replay or a replacement turn',async()=>{
    const retained=storage();let originalId,posts=0;
    const workspace=client(async(_path,init)=>{
        if (init.method==='POST') {posts++;originalId=JSON.parse(init.body).turnId;throw new Error('network lost');}
        return response({message:'not observed'},404);
    },retained);
    await assert.rejects(workspace.submit('Make a map'),/network lost/);
    await assert.rejects(workspace.submit('Make a map'),/previous message has not been confirmed/);
    const reads=[];
    const reloaded=client(async(path,init)=>{reads.push({path,init});return response(turn(originalId));},retained);
    reloaded.viewId='fresh-document-view';
    await reloaded.executions.refresh();
    assert.equal(posts,1);assert.equal(reads.length,1);assert.equal(reads[0].init.method,'GET');
    assert.ok(reads[0].path.endsWith(`/turns/${originalId}`));
    assert.equal(reloaded.executions.current()[0].execution.state,'queued');
});

test('lower revisions cannot overwrite completion and equal-revision contradictions refuse',()=>{
    const workspace=client(),binding=workspace.executions.begin('turn');
    workspace.executions.adopt(binding,turn('turn',execution({state:'completed',stateRevision:4,finalMessageId:'assistant-message'})));
    workspace.executions.adopt(binding,turn('turn',execution({state:'running',stateRevision:3})));
    assert.equal(binding.record.execution.state,'completed');
    assert.throws(()=>workspace.executions.adopt(binding,turn('turn',execution({state:'failed',stateRevision:4}))),/Conflicting/);
    assert.equal(binding.record.execution.state,'completed');
});

test('another execution ID or original workspace cannot replace the retained request',()=>{
    const workspace=client(),binding=workspace.executions.begin('turn');
    workspace.executions.adopt(binding,turn('turn'));
    assert.throws(()=>workspace.executions.adopt(binding,turn('turn',execution({executionId:'replacement'}))),/identity changed/);
    assert.throws(()=>workspace.executions.adopt(binding,{...turn('turn'),context:{workspaceId:'foreign'}}),/does not match/);
});

test('revocation removes previously visible private result references',async()=>{
    const workspace=client(async()=>response({},403)),binding=workspace.executions.begin('turn');
    workspace.executions.adopt(binding,turn('turn',execution({state:'completed',artifacts:[artifact]})));
    await assert.rejects(workspace.executions.read(binding));
    assert.equal(binding.record.execution,undefined);assert.equal(binding.record.context,undefined);
    assert.match(executionSummary(binding.record),/cannot currently be read/);
});

test('a delayed read cannot disclose an earlier principal result in a replacement session',async()=>{
    let release;
    const workspace=client(async()=>{await new Promise(resolve=>{release=resolve;});return response(turn('turn',execution({artifacts:[artifact]})));});
    const binding=workspace.executions.begin('turn');const read=workspace.executions.read(binding);
    workspace.session={...session,principalId:'another-human'};release();
    assert.equal(await read,null);assert.deepEqual(workspace.executions.current(),[]);
});

test('conversation replies do not create completion evidence; human turn IDs recover through native GET',async()=>{
    const calls=[];const workspace=client(async(path,init)=>{calls.push({path,init});return response(turn('human-turn',execution({state:'running'})));});
    await workspace.executions.refresh([{role:'user',turnId:'human-turn',content:'Make a map'},
        {role:'assistant',turnId:'human-turn',content:'Everything succeeded'},
        {role:'assistant',turnId:'untrusted-turn',content:'Done'}]);
    assert.equal(calls.length,1);assert.equal(workspace.executions.current()[0].execution.state,'running');
});

function navigation(family='map',overrides={}) {
    const calls=[];const workspace=client(async(path,init)=>{
        calls.push({path,init});
        if (init.method==='GET') return response({data:{draftId,itemId,generation:7,packageKey:'parcels',envelope:{family},...overrides}});
        const body=JSON.parse(init.body);return response({...workspace.context,task:body.task,selectedResources:body.selectedResources,revision:4});
    });
    const routes=[],tasks=[];
    return {workspace,calls,routes,tasks,options:{navigate:path=>routes.push(path),present:task=>tasks.push(task)}};
}
for (const family of ['map','dashboard']) test(`explicitly opens the same authorized ${family} draft and current revision`,async()=>{
    const {workspace,calls,routes,tasks,options}=navigation(family);
    workspace.follow=false;
    const generation=workspace.contextGeneration;
    await openStudioExecutionResult(workspace,{...artifact,family},()=>workspace.contextGeneration===generation,options);
    assert.deepEqual(routes,['/studio']);assert.equal(tasks[0].id,draftId);assert.equal(tasks[0].revision,7);
    assert.equal(calls[0].init.headers['X-Honua-Session-Partition'],session.principalId);
    assert.equal(calls[0].init.headers.Authorization,undefined);
    assert.equal(calls[0].init.headers['X-API-Key'],undefined);
    assert.equal(JSON.parse(calls[1].init.body).task.id,draftId);
});

test('result identity mismatch cannot select or navigate to another draft',async()=>{
    const {workspace,calls,routes,options}=navigation('map',{itemId:'33333333-3333-4333-8333-333333333333'});
    await assert.rejects(openStudioExecutionResult(workspace,artifact,()=>true,options),/does not match/);
    assert.equal(calls.length,1);assert.deepEqual(routes,[]);
});

test('an older native draft generation cannot replace the original result generation',async()=>{
    const {workspace,calls,routes,options}=navigation('map',{generation:4});
    await assert.rejects(openStudioExecutionResult(workspace,artifact,()=>true,options),/does not match/);
    assert.equal(calls.length,1);assert.deepEqual(routes,[]);
});

test('denied conversation reads remove all cached private execution references',()=>{
    const workspace=client(),binding=workspace.executions.begin('turn');
    workspace.executions.adopt(binding,turn('turn',execution({state:'completed',artifacts:[artifact]})));
    workspace.executions.unavailable({status:403});
    assert.equal(binding.record.execution,undefined);assert.equal(binding.record.context,undefined);
    assert.match(executionSummary(binding.record),/Reconnect/);
});

test('unsaved form or style edits refuse result navigation without discarding the edits',async()=>{
    for (const kind of ['form','style']) {
        const {workspace,calls,routes,options}=navigation();
        if (kind==='form') workspace.dirty.set('layerName','Unsaved');
        else workspace.sharedStyleProvider=()=>({dirty:true});
        await assert.rejects(openStudioExecutionResult(workspace,artifact,()=>true,options),/Save or discard/);
        assert.equal(calls.length,0);assert.deepEqual(routes,[]);
        if (kind==='form') assert.equal(workspace.dirty.get('layerName'),'Unsaved');
    }
});

test('a target change during draft read prevents the old result from taking over the view',async()=>{
    let release;
    const {workspace,routes,options}=navigation();
    const request=workspace.request;
    workspace.request=async(...args)=>{await new Promise(resolve=>{release=resolve;});return request(...args);};
    const generation=workspace.contextGeneration;
    const opening=openStudioExecutionResult(workspace,artifact,()=>workspace.contextGeneration===generation,options);
    await new Promise(resolve=>setImmediate(resolve));workspace.contextGeneration++;release();
    await assert.rejects(opening,/context changed/);assert.deepEqual(routes,[]);
});

const question={id:'question-1',providerRequestId:'provider-request',providerToolCallId:'tool-call',
    text:'Which parcel category should the map show?',contextDigest:'a'.repeat(64)};
function questionClient() {
    const calls=[];
    const original=turn('question-turn',execution({state:'awaiting-input',question}));
    const workspace=client(async(path,init)=>{
        calls.push({path,init});
        if (init.method==='POST') return response(turn(JSON.parse(init.body).turnId,execution({executionId:'new-execution'})));
        return response(original);
    });
    workspace.context=structuredClone(original.context);
    workspace.executions.adopt(workspace.executions.begin(original.turnId),original);
    return {workspace,calls,original};
}

test('native questions remain separate from human approval and cannot be replaced by later observations',()=>{
    assert.throws(()=>readExecution(execution({state:'awaiting-input'})),/question is unavailable/);
    assert.throws(()=>readExecution(execution({state:'awaiting-input',question:{...question,contextDigest:'unknown'}})),/could not be verified/);
    const {workspace,original}=questionClient();
    assert.equal(executionSummary(workspace.executions.current()[0]),'Awaiting your answer');
    const record=workspace.executions.current()[0];
    assert.throws(()=>workspace.executions.adopt({record,records:workspace.executions.records(),scope:workspace.executions.scope(),workspaceId:'workspace'},
        {...original,execution:{...original.execution,stateRevision:2,question:{...question,text:'Approve publication'}}}),/question changed/);
    assert.equal(record.execution.question.text,question.text);
});

test('a clarification reply fresh-reads the original question and links a distinct new turn without approval',async()=>{
    const {workspace,calls}=questionClient();
    workspace.agentSession={submitTurn(){throw new Error('Do not launch another model');}};
    await workspace.questionReplies.begin('question-turn');
    const reply=await workspace.submit('Residential parcels');
    const posted=calls.filter(call=>call.init.method==='POST');
    assert.equal(posted.length,1);
    const body=JSON.parse(posted[0].init.body);
    assert.deepEqual([body.replyToQuestionId,body.replyToTurnId,body.replyToExecutionId],
        ['question-1','question-turn','original-execution']);
    assert.notEqual(body.turnId,'question-turn');assert.equal(body.approved,undefined);
    assert.equal(calls.filter(call=>call.path.endsWith('/turns/question-turn')).length,2);
    assert.equal(reply.execution.executionId,'new-execution');assert.equal(workspace.questionReplies.current(),null);
});

for (const change of ['target','resources','task','unsaved']) test(`a ${change} change blocks a linked answer without sending or dropping edits`,async()=>{
    const {workspace,calls}=questionClient();
    await workspace.questionReplies.begin('question-turn');
    if (change==='target') workspace.context.targetId='other-target';
    if (change==='resources') workspace.context.selectedResources=[{kind:'layer',id:'8'}];
    if (change==='task') workspace.context.task={kind:'studio-composition',id:draftId,revision:1};
    if (change==='unsaved') workspace.dirty.set('layerName','Keep my change');
    await assert.rejects(workspace.submit('Residential parcels'),/before replying/);
    assert.equal(calls.filter(call=>call.init.method==='POST').length,0);
    assert.ok(workspace.questionReplies.current());
    if (change==='unsaved') assert.equal(workspace.dirty.get('layerName'),'Keep my change');
});

test('a newer generation of the same draft requires explicit current-context review before answering',async()=>{
    const {workspace,calls,original}=questionClient();
    original.context.task={kind:'studio-composition',id:draftId,revision:5,packageKey:'parcels'};
    workspace.context=structuredClone(original.context);
    await workspace.questionReplies.begin('question-turn');
    workspace.context.task.revision=6;
    await assert.rejects(workspace.submit('Residential parcels'),/Review the current target/);
    assert.equal(calls.filter(call=>call.init.method==='POST').length,0);
    await workspace.questionReplies.reviewCurrent();
    await workspace.submit('Residential parcels');
    assert.equal(calls.filter(call=>call.init.method==='POST').length,1);
});

test('opening an old question on an already newer draft still requires explicit review',async()=>{
    const {workspace,calls,original}=questionClient();
    original.context.task={kind:'studio-composition',id:draftId,revision:5,packageKey:'parcels'};
    workspace.context=structuredClone(original.context);
    workspace.context.task.revision=6;
    await workspace.questionReplies.begin('question-turn');
    assert.equal(workspace.questionReplies.current().reviewed,null);
    await assert.rejects(workspace.submit('Residential parcels'),/Review the current target/);
    assert.equal(calls.filter(call=>call.init.method==='POST').length,0);
    await workspace.questionReplies.reviewCurrent();
    await workspace.submit('Residential parcels');
    assert.equal(calls.filter(call=>call.init.method==='POST').length,1);
});

test('an unavailable original question cannot turn the typed answer into an unrelated request',async()=>{
    const {workspace,calls}=questionClient();
    await workspace.questionReplies.begin('question-turn');
    workspace.request=async()=>response({message:'Access denied'},403);
    await assert.rejects(workspace.submit('Residential parcels'),/Access denied/);
    assert.equal(calls.filter(call=>call.init.method==='POST').length,0);
    assert.ok(workspace.questionReplies.current());
    assert.equal(workspace.executions.current()[0].execution,undefined);
});

test('switching the question during an in-flight submission cannot coalesce two unrelated replies',async()=>{
    const {workspace}=questionClient();
    await workspace.questionReplies.begin('question-turn');
    let release;
    const request=workspace.request;
    workspace.request=async(...args)=>{await new Promise(resolve=>{release=resolve;});return request(...args);};
    const submitting=workspace.submit('Residential parcels');
    await new Promise(resolve=>setImmediate(resolve));
    workspace.questionReplies.cancel();
    await assert.rejects(workspace.submit('Residential parcels'),/Wait for the current message/);
    release();await assert.rejects(submitting,/original question is no longer available/);
});

for (const cachedDraft of [null,{id:'other-draft',kind:'publish-layer',revision:1,fields:{name:'Other draft'}}])
test(`opening an original publication task survives editor focus with ${cachedDraft ? 'another' : 'no'} cached draft`,async()=>{
    const {workspace,original}=questionClient(),routes=[];
    original.context.task={kind:'publish-layer',id:'publish-draft',revision:2};
    workspace.context=structuredClone(original.context);
    await workspace.questionReplies.begin('question-turn');
    workspace.draft=cachedDraft;
    workspace.context={...workspace.context,task:{kind:'publish-layer',id:'other-draft',revision:1}};
    workspace.request=async(path,init)=>{
        if (path.endsWith('/drafts/publish-draft')) return response({id:'publish-draft',kind:'publish-layer',revision:4});
        if (init.method==='PUT') return response({...workspace.context,...JSON.parse(init.body),revision:5});
        return response(original);
    };
    await workspace.questionReplies.openOriginal({navigate:path=>routes.push(path)});
    assert.deepEqual(routes,['/operate/publishing/quick']);
    assert.equal(workspace.draft.id,'publish-draft');
    await workspace.focusPublishDraft();
    assert.equal(workspace.context.task.id,'publish-draft');assert.equal(workspace.context.task.revision,4);
    assert.deepEqual(workspace.context.selectedResources,original.context.selectedResources);
    assert.equal(workspace.questionReplies.current().reviewed,null);
    await assert.rejects(workspace.submit('Use this table'),/Review the current target/);
    await workspace.questionReplies.reviewCurrent();
    assert.ok(workspace.questionReplies.current().reviewed);
});

for (const kind of ['legacy','failed','completed','refused']) test(`bounded history retires observed ${kind} turns`,()=>{
    const workspace=client();
    for (let i=0;i<100;i++) {
        const id=`turn-${i}`,requestDigest=`sha256:${'a'.repeat(64)}`;
        const binding=workspace.executions.begin(id,requestDigest);
        const observed=kind==='refused'
            ? {turnId:id,status:'not-dispatched',submission:{turnId:id,requestDigest,disposition:'not-dispatched',code:'context-changed'}}
            : turn(id,kind==='legacy' ? null : execution({state:kind}));
        workspace.executions.adopt(binding,observed);
    }
    const next=workspace.executions.begin('next-turn');
    assert.equal(next.records.size,100);
    assert.equal(next.records.has('turn-0'),false);
    assert.equal(next.record.acknowledged,false);
});

test('bounded history preserves active and unresolved records and requires fresh observations after reload',async()=>{
    const retained=storage(),workspace=client(undefined,retained);
    for (let i=0;i<100;i++) {
        const id=`turn-${i}`,binding=workspace.executions.begin(id);
        workspace.executions.adopt(binding,turn(id,execution({state:'running'})));
    }
    assert.throws(()=>workspace.executions.begin('next-turn'),/Check your existing requests/);
    const reloaded=client(async(path)=>response(turn(path.split('/').at(-1),null)),retained);
    assert.throws(()=>reloaded.executions.begin('next-turn'),/Check your existing requests/);
    await reloaded.executions.refresh();
    const next=reloaded.executions.begin('next-turn');
    assert.equal(next.records.size,100);
    assert.throws(()=>reloaded.executions.begin('unconfirmed-followup'),/previous message has not been confirmed/);
});

test('failed storage after history eviction preserves every original request in memory',()=>{
    const workspace=client();
    for (let i=0;i<100;i++) {
        const id=`turn-${i}`,binding=workspace.executions.begin(id);
        workspace.executions.adopt(binding,turn(id,null));
    }
    const before=workspace.executions.current();
    workspace.storage.setItem=()=>{throw new Error('storage full');};
    assert.throws(()=>workspace.executions.begin('next-turn'),/storage full/);
    assert.deepEqual(workspace.executions.current(),before);
});

test('submission digest binds the exact UTF-8 bytes including non-ASCII prompt text',async()=>{
    assert.equal(await submissionDigest('{"turnId":"turn-1","viewId":"view-1","contextRevision":2,"prompt":"human😀"}'),
        'sha256:4013f1fd75be4e0e24f769dd90d564eb22331b709be474f671e235ff42c4cc11');
});

test('a persisted original refusal allows an explicit corrected request without replaying the refused one',async()=>{
    const posts=[],reads=[];let refusal;
    const workspace=client(async(path,init)=>{
        if (init.method==='POST') {
            const body=JSON.parse(init.body);posts.push(body);
            if (posts.length===1) {
                refusal={turnId:body.turnId,status:'not-dispatched',submission:{turnId:body.turnId,
                    requestDigest:await submissionDigest(init.body),disposition:'not-dispatched',code:'context-changed'}};
                return response(refusal,409);
            }
            return response(turn(body.turnId));
        }
        reads.push(path);return response(refusal);
    });
    await assert.rejects(workspace.submit('Make a map'),/Review the current selection/);
    assert.equal(posts.length,1);assert.equal(reads.length,1);
    assert.ok(reads[0].endsWith(`/turns/${posts[0].turnId}`));
    const record=workspace.executions.current()[0];
    assert.equal(record.acknowledged,true);assert.equal(record.submission.disposition,'not-dispatched');
    assert.match(executionSummary(record),/request was not started/);
    const corrected=await workspace.submit('Make the corrected map');
    assert.equal(posts.length,2);assert.notEqual(corrected.turnId,posts[0].turnId);
    assert.ok(workspace.executions.current().some(row=>row.turnId===posts[0].turnId));
});

test('a refusal for different request bytes cannot clear unknown acceptance',async()=>{
    let originalId,posts=0;
    const workspace=client(async(_path,init)=>{
        if (init.method==='POST') {posts++;originalId=JSON.parse(init.body).turnId;return response({},409);}
        return response({turnId:originalId,status:'not-dispatched',submission:{turnId:originalId,
            requestDigest:`sha256:${'0'.repeat(64)}`,disposition:'not-dispatched',code:'context-changed'}});
    });
    await assert.rejects(workspace.submit('Make a map'));
    assert.equal(workspace.executions.current()[0].acknowledged,false);
    await assert.rejects(workspace.submit('Try another map'),/previous message has not been confirmed/);
    assert.equal(posts,1);
});

test('unknown delivery retains only ID and digest and recovers a durable refusal after reload',async()=>{
    const retained=storage();let refusal;
    const workspace=client(async(_path,init)=>{
        if (init.method==='POST') {
            const body=JSON.parse(init.body);refusal={turnId:body.turnId,status:'not-dispatched',submission:{turnId:body.turnId,
                requestDigest:await submissionDigest(init.body),disposition:'not-dispatched',code:'runtime-unavailable'}};
        }
        throw new Error('network unavailable');
    },retained);
    await assert.rejects(workspace.submit('Private prompt text'),/network unavailable/);
    const serialized=[...retained.data.entries()].find(([key])=>key.startsWith('honua.workspace.executions:'))[1];
    assert.ok(serialized.includes(refusal.submission.requestDigest));assert.ok(!serialized.includes('Private prompt text'));
    assert.ok(!serialized.includes('not-dispatched'));
    const reloaded=client(async()=>response(refusal),retained);reloaded.viewId='new-view';
    await reloaded.executions.refresh();
    assert.equal(reloaded.executions.current()[0].acknowledged,true);
    assert.equal(reloaded.executions.current()[0].submission.code,'runtime-unavailable');
});

test('a current native reply block preserves the question without creating a new submission',async()=>{
    const {workspace,calls,original}=questionClient();
    original.execution.replyAdmission={state:'blocked',code:'execution-retirement-required'};
    original.execution.stateRevision++;
    await workspace.questionReplies.begin('question-turn');
    await assert.rejects(workspace.submit('Residential parcels'),/Previous work is still finishing/);
    assert.equal(calls.filter(call=>call.init.method==='POST').length,0);
    assert.ok(workspace.questionReplies.current());assert.equal(workspace.executions.current().length,1);
});
