import test from 'node:test';
import assert from 'node:assert/strict';
import {AgentWorkspace} from '../../src/Honua.Console.Web/wwwroot/js/agent-workspace.mjs';
function fixture() {
 const data=new Map(),calls=[];let status='Running',fail=true,next=0;
 const storage={getItem:key=>data.get(key),setItem:(key,value)=>data.set(key,value)};
 const draft={id:'draft',kind:'publish-layer',revision:3,fields:{layerName:'Original'}};
 const request=async(path,init)=>{
  calls.push({path,method:init.method});
  if(init.method==='POST'){if(fail) throw Error('lost response');}
  if(init.method==='PATCH') draft.revision++;
  return {ok:true,status:200,json:async()=>path.includes('/commands/')||init.method==='POST'
   ?{data:{operationId:'service.publish',operationInstanceId:'operation',status}}
   :path.includes('/drafts/')?draft:{revision:20,targetId:'target',task:{kind:'publish-layer',id:'draft',revision:draft.revision}}};
 };
 const make=()=>{
  const workspace=new AgentWorkspace({request,storage,uuid:()=>`id-${++next}`});
  workspace.workspaceId='workspace';workspace.session={principalId:'actor',environmentId:'environment',releaseId:'release'};
  workspace.context={targetId:'target',task:{kind:'publish-layer',id:'draft',revision:draft.revision}};
  workspace.draft={...draft};return workspace;
 };
 return {make,calls,setStatus:value=>status=value,setFail:value=>fail=value};
}
test('mounted lost publication blocks later Save and Publish without changing original revision',async()=>{
 const f=fixture(),workspace=f.make();
 await assert.rejects(workspace.submitDraft({save:false}),/lost response/);
 workspace.edit('layerName','Later typing');
 for(const publish of [false,true]) await assert.rejects(workspace.savePublishForm({layerName:'Later typing'},{publish}),/Previous publication Running/);
 assert.deepEqual(f.calls.map(call=>call.method),['POST','GET','GET']);
 assert.equal(workspace.publications.read().draftRevision,3);
 assert.equal(workspace.dirty.get('layerName'),'Later typing');
});
test('reload restores local candidate only under original authority and reconciles read-only',async()=>{
 const f=fixture(),first=f.make();
 await assert.rejects(first.submitDraft({save:false}),/lost response/);first.edit('layerName','Retained typing');
 const reloaded=f.make();assert.notEqual(first.viewId,reloaded.viewId);
 reloaded.publications.restoreCandidate(reloaded.draft);
 assert.equal(reloaded.dirty.get('layerName'),'Retained typing');
 await assert.rejects(reloaded.savePublishForm({layerName:'Retained typing'}),/Previous publication Running/);
 reloaded.session={...reloaded.session,principalId:'foreign'};
 await assert.rejects(reloaded.publications.reconcile(),/original publication connection/);
 assert.equal(f.calls.filter(call=>call.method==='POST').length,1);
 assert.ok(reloaded.publications.read());
});
test('terminal failure requires explicit current form review before a new command',async()=>{
 const f=fixture(),workspace=f.make();
 await assert.rejects(workspace.submitDraft({save:false}),/lost response/);
 workspace.edit('layerName','Later typing');f.setStatus('Failed');
 await assert.rejects(workspace.savePublishForm({layerName:'Later typing'},{publish:true}),/Review that result/);
 await workspace.publications.acknowledge({readFields:()=>({layerName:'Later typing'})});
 assert.equal(workspace.publications.read(),null);assert.equal(workspace.draft.revision,4);
 assert.equal(workspace.context.revision,20);assert.equal(workspace.context.task.revision,4);
 assert.equal(workspace.dirty.get('layerName'),'Later typing');
 f.setFail(false);await workspace.submitDraft({save:false});
 assert.equal(f.calls.filter(call=>call.method==='POST').length,2);
});
test('completed same revision remains read-only even after explicit acknowledgment',async()=>{
 const f=fixture(),workspace=f.make();await assert.rejects(workspace.submitDraft({save:false}),/lost response/);
 f.setStatus('Completed');await workspace.publications.acknowledge({readFields:()=>({layerName:'Original'})});
 await workspace.submitDraft({save:false});
 assert.equal(f.calls.filter(call=>call.method==='POST').length,1);
});
test('navigation during command status read cannot acknowledge or clear pending identity',async()=>{
 const f=fixture(),workspace=f.make();await assert.rejects(workspace.submitDraft({save:false}),/lost response/);
 const original=workspace.api.bind(workspace);workspace.api=async(...args)=>{const value=await original(...args);workspace.viewId='different-view';return value;};
 await assert.rejects(workspace.publications.reconcile(),/connection changed/);
 assert.ok(workspace.publications.read());
});
test('old acknowledgment cannot clear a replacement pending command during canonical draft read',async()=>{
 const f=fixture(),workspace=f.make();await assert.rejects(workspace.submitDraft({save:false}),/lost response/);
 f.setStatus('Failed');const original=workspace.api.bind(workspace);
 workspace.api=async(...args)=>{
  const value=await original(...args);
  if(args[0].includes('/drafts/')){
   const pending=workspace.publications.read();
   workspace.storage.setItem(workspace.publications.key(),JSON.stringify({...pending,commandId:'replacement-command'}));
  }
  return value;
 };
 await assert.rejects(workspace.publications.acknowledge({readFields:()=>({layerName:'Original'})}),/draft changed/);
 assert.equal(workspace.publications.read().commandId,'replacement-command');
 assert.equal(workspace.storage.getItem('honua.workspace.publication-retry.workspace.draft.3'),undefined);
});
test('reviewing a different clean draft cannot acknowledge the original publication',async()=>{
 const f=fixture(),workspace=f.make();await assert.rejects(workspace.submitDraft({save:false}),/lost response/);
 f.setStatus('Failed');workspace.draft={...workspace.draft,id:'another-draft'};
 const calls=f.calls.length;
 await assert.rejects(workspace.publications.acknowledge({readFields:()=>({layerName:'Original'})}),/current publish form/);
 assert.equal(f.calls.length,calls);assert.equal(workspace.publications.read().draftId,'draft');
});
test('command reconciliation carries original immutable draft binding after later revision changes',async()=>{
 const f=fixture(),workspace=f.make();await assert.rejects(workspace.submitDraft({save:false}),/lost response/);
 workspace.draft={...workspace.draft,revision:9};await workspace.publications.reconcile();
 assert.match(f.calls.at(-1).path,/\/commands\/id-2\?draftId=draft&draftRevision=3$/);
});
test('lost context read after reviewed retry CAS keeps original pending identity and local typing',async()=>{
 const f=fixture(),workspace=f.make();await assert.rejects(workspace.submitDraft({save:false}),/lost response/);
 f.setStatus('Failed');workspace.edit('layerName','Retained');const original=workspace.api.bind(workspace);
 workspace.api=async(path,...args)=>{if(path.includes('?viewId=')) throw Error('readback lost');return original(path,...args);};
 await assert.rejects(workspace.publications.acknowledge({readFields:()=>({layerName:'Retained'})}),/readback lost/);
 assert.equal(workspace.publications.read().draftRevision,3);assert.equal(workspace.dirty.get('layerName'),'Retained');
 assert.equal(workspace.draft.revision,3);assert.equal(f.calls.filter(call=>call.method==='POST').length,1);
});
test('an older concurrent receipt cannot replace a newly pinned operation identity',async()=>{
 const f=fixture(),workspace=f.make();await assert.rejects(workspace.submitDraft({save:false}),/lost response/);
 const unpinned=workspace.publications.read();
 workspace.publications.observe(unpinned,{operationId:'service.publish',operationInstanceId:'first-operation',status:'Running'});
 assert.throws(()=>workspace.publications.observe(unpinned,{operationId:'service.publish',operationInstanceId:'other-operation',status:'Completed'}),/original publication connection/);
 assert.equal(workspace.publications.read().operationInstanceId,'first-operation');
 assert.equal(workspace.publications.read().lastStatus,'Running');
});
for(const phase of ['PATCH','context']) test(`reload recovers accepted retry revision after lost ${phase} response without duplicate CAS`,async()=>{
 const f=fixture(),first=f.make();await assert.rejects(first.submitDraft({save:false}),/lost response/);
 f.setStatus('Failed');first.edit('layerName','Later candidate');const original=first.api.bind(first);
 first.api=async(path,method,...rest)=>{
  const value=await original(path,method,...rest);
  if(method==='PATCH'&&phase==='PATCH'||path.includes('?viewId=')&&phase==='context') throw Error('accepted response lost');
  return value;
 };
 await assert.rejects(first.publications.acknowledge({readFields:()=>({layerName:'Later candidate'})}),/accepted response lost/);
 assert.ok(first.publications.read());assert.equal(first.draft.revision,3);
 const reloaded=f.make();reloaded.draft=await reloaded.api('workspace/drafts/draft');
 reloaded.publications.restoreCandidate(reloaded.draft);
 assert.equal(reloaded.draft.revision,4);assert.equal(reloaded.dirty.get('layerName'),'Later candidate');
 await reloaded.publications.acknowledge({readFields:()=>({layerName:'Later candidate'})});
 assert.equal(reloaded.publications.read(),null);assert.equal(reloaded.draft.revision,4);
 assert.equal(f.calls.filter(call=>call.method==='PATCH').length,1);
 assert.equal(f.calls.filter(call=>call.method==='POST').length,1);
 assert.equal(reloaded.dirty.get('layerName'),'Later candidate');
});
