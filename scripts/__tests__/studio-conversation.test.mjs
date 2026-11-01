import test from 'node:test';
import assert from 'node:assert/strict';
import { AgentWorkspace } from '../../src/Honua.Console.Web/wwwroot/js/agent-workspace.mjs';
const session = { conversationMode:'studio', environmentId:'env', environmentName:'Test', principalId:'human', csrfToken:'csrf' };
const response = value => ({ok:true,status:200,json:async()=>value});
const storage = () => { const values=new Map();return {getItem:k=>values.get(k),setItem:(k,v)=>values.set(k,v)}; };

test('Start conversation uses canonical Studio capabilities without creating an absent server workspace', async()=>{
 const calls=[];
 const workspace=new AgentWorkspace({storage:storage(),uuid:()=> 'tab',request:async(path)=>{
  calls.push(path);
  if(path.endsWith('/session'))return response(session);
  if(path.endsWith('/studio/capabilities'))return response({data:{enabled:true,providers:[{configured:true,toolSupport:true}]}});
  throw new Error(`Absent trunk contract: ${path}`);
 }});
 await workspace.create();
 assert.equal(workspace.session.conversationMode,'studio');
 assert.deepEqual(calls,['/api/agent-workspace/session','/api/agent-workspace/studio/capabilities']);
 await workspace.select([{kind:'layer',id:'4'}],{kind:'studio-composition',id:'draft',revision:2});
 assert.equal(workspace.context.task.id,'draft');
 assert.deepEqual((await workspace.conversation()).messages,[]);
});

test('disabled Studio AI reports configuration before any workspace or model call',async()=>{
 const workspace=new AgentWorkspace({storage:storage(),uuid:()=> 'tab',request:async path=>response(path.endsWith('/session')?session:{data:{enabled:false,providers:[]}})});
 await assert.rejects(workspace.create(),/StudioAiProxy.*Enabled/);
 assert.equal(workspace.context,null);
});

async function readyWorkspace({chat,prepare,request} = {}) {
 const calls=[];
 const workspace=new AgentWorkspace({storage:storage(),uuid:()=> 'tab',request:async(path,init)=>{
  calls.push({path,init});
  if(path.endsWith('/session'))return response(session);
  if(path.endsWith('/studio/capabilities'))return response({data:{enabled:true,providers:[{configured:true,toolSupport:true}]}});
  if(request)return request(path,init);
  throw new Error(`Unexpected route ${path}`);
 }});
 await workspace.create();
 let sdkOptions;
 const sdk={selectWorkflowView:async view=>{assert.equal(view,'setup');return {};},attachDraft(draft){this.draft=draft;},chat:chat||(async()=>({status:'complete',text:'Hello'}))};
 workspace.studioConversation.loadAssets=async()=>({createWorkspaceAgentSession:options=>{sdkOptions=options;return sdk;}});
 if(prepare)workspace.prepareContextProvider=()=>prepare;
 return {workspace,sdk,calls,options:()=>sdkOptions};
}

test('a Studio turn binds the current draft and uses the existing SDK model/tool loop',async()=>{
 const {workspace,sdk,options,calls}=await readyWorkspace();
 await workspace.select([],{kind:'studio-composition',id:'draft',revision:7});
 await workspace.submit('Make the map blue');
 assert.deepEqual(sdk.draft,{draftId:'draft',generation:7});
 assert.deepEqual((await workspace.conversation()).messages.map(message=>message.role),['user','assistant']);
 assert.deepEqual(options().toolFamilies,['honua.studio.composition']);
 assert.equal(options().certification,undefined);
 assert.ok(calls.every(call=>!call.path.includes('/turns')&&!call.path.includes('/context')));
 assert.equal(options().toolApproval({source:'metadata',classification:{family:'honua.studio.composition'}}),true);
 assert.equal(options().toolApproval({source:'metadata',classification:{family:'honua.workspace.devops'}}),false);
});

test('the client negotiates the shipped bounded MCP view and pins cookie authority',async()=>{
 const {workspace,calls,options}=await readyWorkspace({request:async()=>response({})});
 await workspace.submit('Hello');
 await options().fetchImpl('/api/mcp',{method:'POST',body:JSON.stringify({method:'initialize',params:{_meta:{'honua.io/workflow-view':'workspace'}}})});
 const wire=calls.at(-1);
 assert.equal(wire.path,'/api/agent-workspace/studio/mcp');
 assert.equal(JSON.parse(wire.init.body).params._meta['honua.io/workflow-view'],'setup');
 assert.equal(wire.init.headers['X-Honua-View'],'tab');
 assert.equal(wire.init.headers['X-Honua-Session-Partition'],'human');
 workspace.session={...session,principalId:'another-human'};
 await assert.rejects(options().fetchImpl('/api/mcp',{}),/Unbound/);
});

test('selection drift while preparing refuses before model execution',async()=>{
 let release,entered;
 const waiting=new Promise(resolve=>{entered=resolve;});
 let sends=0;
 const {workspace}=await readyWorkspace({prepare:async()=>{entered();await new Promise(resolve=>{release=resolve;});return {id:'draft',revision:7,kind:'studio-composition'};},chat:async()=>{sends++;}});
 await workspace.select([],{kind:'studio-composition',id:'draft',revision:7});
 const pending=workspace.submit('Change this map');await waiting;
 await workspace.select([],{kind:'studio-composition',id:'another',revision:1});release();
 await assert.rejects(pending,/selected draft changed/);
 assert.equal(sends,0);
 assert.equal(workspace.capturing,0);
});

test('navigation during model execution cannot retarget the captured draft or adopt its result',async()=>{
 let release,entered;const waiting=new Promise(resolve=>{entered=resolve;});
 const {workspace,sdk}=await readyWorkspace({chat:async()=>{entered();await new Promise(resolve=>{release=resolve;});return {status:'complete',text:'Applied'};}});
 await workspace.select([],{kind:'studio-composition',id:'draft',revision:7});
 const pending=workspace.submit('Change this map');await waiting;
 await workspace.select([],{kind:'studio-composition',id:'another',revision:1});
 assert.equal(sdk.draft.draftId,'draft');release();await pending;
 assert.equal(workspace.context.task.id,'another');
});

test('the shipped SDK completes a canonical tool round trip against the trunk wire contract',async()=>{
 globalThis.HTMLElement ||= class {};
 const module=await import('../../src/Honua.Console.Web/wwwroot/_studio/console.js');
 let modelCalls=0;const toolCalls=[];
 const json=value=>new Response(JSON.stringify(value),{headers:{'Content-Type':'application/json'}});
 const capabilities={enabled:true,defaultProvider:'fixture',providers:[{provider:'fixture',model:'fixture-model',configured:true,toolSupport:true,isDefault:true}]};
 const workspace=new AgentWorkspace({storage:storage(),uuid:()=> 'tab',request:async(path,init)=>{
  if(path.endsWith('/session'))return json(session);
  if(path.endsWith('/capabilities'))return json({success:true,data:capabilities});
  if(path.endsWith('/mcp')){
   const rpc=JSON.parse(init.body);let result;
   if(rpc.method==='initialize'){
    assert.equal(rpc.params._meta['honua.io/workflow-view'],'setup');
    result={protocolVersion:'2025-03-26',capabilities:{tools:{}},serverInfo:{name:'fixture',version:'1'}};
   }else if(rpc.method==='tools/list'){
    assert.equal(rpc.params.view,'setup');
    result={tools:[{name:'honua_studio_create_draft',description:'Create a map',inputSchema:{type:'object',properties:{packageKey:{type:'string'}}},_meta:{'honua.studio':{family:'honua.studio.composition',view:'setup',revision:'setup.v1'}}}],_meta:{view:'setup'}};
   }else if(rpc.method==='tools/call'){
    toolCalls.push(rpc.params);
    result={content:[{type:'text',text:JSON.stringify({draftId:'real-draft',generation:0})}]};
   }else throw new Error(rpc.method);
   return json({jsonrpc:'2.0',id:rpc.id,result});
  }
  if(path.endsWith('/ai')){
   const body=JSON.parse(init.body);modelCalls++;
   if(modelCalls===1)assert.ok(body.tools.some(tool=>tool.name==='honua_studio_create_draft'));
   else assert.ok(body.messages.some(message=>message.role==='tool'&&message.content.includes('real-draft')));
   const events=modelCalls===1?[
    {type:'messageStart',provider:'fixture',model:'fixture-model'},
    {type:'toolCallStart',toolCallId:'call',toolName:'honua_studio_create_draft'},
    {type:'toolCallStop',toolCallId:'call',toolArguments:{packageKey:'my-map'}},
    {type:'messageStop',stopReason:'toolUse'}
   ]:[{type:'messageStart'},{type:'textDelta',text:'Created your map.'},{type:'messageStop',stopReason:'endTurn'}];
   return new Response(events.map(event=>`event: ${event.type.replace(/[A-Z]/g,char=>'_'+char.toLowerCase())}\ndata: ${JSON.stringify(event)}\n\n`).join(''),{headers:{'Content-Type':'text/event-stream'}});
  }
  throw new Error(path);
 }});
 await workspace.create();workspace.studioConversation.loadAssets=async()=>module;
 const result=await workspace.submit('Make a map');
 assert.equal(result.status,'completed',result.errorMessage);
 assert.equal(modelCalls,2);
 assert.equal(toolCalls[0].name,'honua_studio_create_draft');
 assert.equal(workspace.context.task.id,'real-draft');
 assert.equal((await workspace.conversation()).messages.at(-1).content,'Created your map.');
});

test('a rejected design selection releases capture and permits a corrected retry',async()=>{
 const {workspace}=await readyWorkspace();
 workspace.studioDesignFocusProvider=()=>{throw new Error('Review the selected design');};
 await assert.rejects(workspace.submit('Change the map'),/selected design/);
 assert.equal(workspace.capturing,0);
 assert.equal(workspace.studioConversation.busy,false);
 workspace.studioDesignFocusProvider=()=>undefined;
 assert.equal((await workspace.submit('Try again')).status,'complete');
});

test('each model turn receives the captured draft identity after changing drafts',async()=>{
 const {workspace,options}=await readyWorkspace();
 await workspace.select([],{kind:'studio-composition',id:'draft-a',revision:7});
 await workspace.submit('Edit this map');
 assert.match(options().system(),/draft-a.*generation.*7/);
 await workspace.select([],{kind:'studio-composition',id:'draft-b',revision:3});
 await workspace.submit('Edit this map instead');
 assert.match(options().system(),/draft-b.*generation.*3/);
 assert.doesNotMatch(options().system(),/draft-a/);
});
