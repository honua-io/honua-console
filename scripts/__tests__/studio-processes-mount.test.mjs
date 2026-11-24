import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import vm from 'node:vm';
import {connectStudioHost} from '../../src/Honua.Console.Web/wwwroot/js/studio-bundle.mjs';
import {processTask} from '../../src/Honua.Console.Web/wwwroot/js/workflow-task.mjs';

const source=await readFile(new URL('../../src/Honua.Console.Web/wwwroot/js/studio-embed.mjs',import.meta.url),'utf8');
const mount=source.slice(source.indexOf('async function mountProcesses()'),source.indexOf('new MutationObserver(mountProcesses)')).replaceAll('import.meta.url',JSON.stringify('https://console.example/js/studio-embed.mjs'));

const localTask=source.slice(source.indexOf('function localProcessTask('),source.indexOf("window.addEventListener('honua-workspace-local-process-task'"));

function processHost() {
 const slots={builtin:{children:[],append(child){this.children.push(child);}},custom:{append(child){this.child=child;},hidden:false}};
 const status={textContent:''};
 const host={dataset:{},isConnected:true,querySelector:selector=>selector==='[data-processes-status]' ? status : slots[selector.includes('builtin') ? 'builtin' : 'custom']};
 return {host,slots,status};
}

function processPage({native=true,current=true,workspace,openStudio=async()=>({}),readSession=async session=>session,bundleReady=Promise.resolve(),prepare=()=>{}}={}) {
 let active=processHost();
 const session={principalId:'actor',environmentId:'default',environmentName:'default',
  ...(native ? {transport:{assertCurrent:async()=>{if(!current) throw new Error('Console session changed');}}} : {})};
 class Workspace {prepare(...args){return prepare(...args);}}
 const assets=bundleReady.then(()=>({registerAllStudioElements(){},ServerProcessWorkspace:Workspace,LocalProcessWorkspace:Workspace}));
 const navigations=[];
 const context=vm.createContext({assets,
  document:{querySelector:()=>active.host,createElement:()=>({
   listeners:new Map(),
   addEventListener(name,handler){this.listeners.set(name,handler);},
  })},
  window:{honuaAgentWorkspace:workspace,Blazor:{navigateTo:path=>navigations.push(path)}},consoleHref:path=>path,
  readStudioSession:()=>readSession(session),studioElement:openStudio,capturedProcessFetch:()=>()=>{},
  connectStudioHost,processTask,processView:undefined,StudioSignInRequired:Error});
 // The fixture supplies the bundle without a production dynamic import.
 vm.runInContext(localTask,context);
 vm.runInContext(mount,context);
 return {
  get active(){return active;},
  get currentHost(){return context.processView?.host;},
  navigations,
  mount:()=>vm.runInContext('mountProcesses()',context),
  reopen(){
   active.host.isConnected=false;
   active=processHost();
   return active;
  },
 };
}

async function fixture(options) {
 const page=processPage(options);
 await page.mount();
 return page.active;
}

test('native Processes mounts without a legacy workspace session',async()=>{
 const {slots,status}=await fixture({workspace:{session:null}});
 assert.equal(slots.builtin.children.length,1);
 assert.equal(slots.custom.hidden,true);
 assert.equal(status.textContent,'Connected to default');
});

test('adding a published layer from the active Processes page opens Studio',async()=>{
 const workspace={session:{principalId:'actor',environmentId:'default'},select:async()=>{}};
 const added=[];
 const page=processPage({native:false,workspace,openStudio:async()=>({
  async addPublishedLayer(layer){added.push(layer);return {ok:true};},
 })});
 await page.mount();

 const layer={layerId:1,serviceName:'rivers'};
 await page.active.slots.custom.child.listeners.get('honua-processes-layer-ready')({detail:{layer}});

 assert.deepEqual(added,[layer]);
 assert.deepEqual(page.navigations,['/studio']);
});

for(const phase of ['editor setup','adding the layer']){
 test(`leaving Processes during ${phase} keeps the current page`,async()=>{
  const started=deferred();
  const pending=deferred();
  const workspace={session:{principalId:'actor',environmentId:'default'},select:async()=>{}};
  let openings=0;
  let additions=0;
  const app={async addPublishedLayer(){
   additions++;
   if(phase==='adding the layer'){
    started.resolve();
    await pending.promise;
   }
   return {ok:true};
  }};
  const page=processPage({native:false,workspace,openStudio:async()=>{
   if(++openings===2 && phase==='editor setup'){
    started.resolve();
    await pending.promise;
   }
   return app;
  }});
  await page.mount();
  const addToMap=page.active.slots.custom.child.listeners.get('honua-processes-layer-ready');
  const handoff=addToMap({detail:{layer:{layerId:1,serviceName:'rivers'}}});
  await started.promise;
  const current=page.reopen();
  await page.mount();
  pending.resolve();
  await handoff;

  assert.equal(page.currentHost,current.host);
  assert.equal(additions,phase==='adding the layer' ? 1 : 0);
  assert.deepEqual(page.navigations,[]);
 });
}
test('native Processes refuses to mount after its Console capture changes',async()=>{
 const {slots,status}=await fixture({current:false});
 assert.equal(slots.builtin.children.length,0);
 assert.match(status.textContent,/session changed/);
});
test('legacy Processes still requires a matching workspace session',async()=>{
 const {slots}=await fixture({native:false,workspace:{session:{principalId:'actor',environmentId:'default'},select:async()=>{}}});
 assert.equal(slots.builtin.children.length,1);
 const legacy=await fixture({native:false,workspace:{session:null,select:async()=>{}}});
 assert.equal(legacy.slots.builtin.children.length,0);
});

function deferred() {
 let resolve;
 const promise=new Promise(complete=>{resolve=complete;});
 return {promise,resolve};
}

test('closing Processes while the bundle loads skips the abandoned session read',async()=>{
 const bundle=deferred();
 let reads=0;
 const page=processPage({bundleReady:bundle.promise,readSession:async session=>{
  reads++;
  return session;
 }});
 const abandoned=page.active;
 const oldMount=page.mount();
 const reopened=page.reopen();
 const newMount=page.mount();

 bundle.resolve();
 await Promise.all([oldMount,newMount]);

 assert.equal(reads,1);
 assert.equal(abandoned.slots.builtin.children.length,0);
 assert.equal(reopened.slots.builtin.children.length,1);
 assert.equal(reopened.status.textContent,'Connected to default');
});

test('reopening Processes during setup mounts only the current host',async()=>{
 const started=deferred();
 const delayedStudio=deferred();
 const selections=[];
 const workspace={session:{principalId:'actor',environmentId:'default'},select:async(...args)=>selections.push(args)};
 let openings=0;
 const page=processPage({workspace,openStudio:()=>{
  openings++;
  if(openings===1){
   started.resolve();
   return delayedStudio.promise;
  }
  return Promise.resolve({});
 }});
 const abandoned=page.active;
 const oldMount=page.mount();
 await started.promise;

 const reopened=page.reopen();
 await page.mount();
 delayedStudio.resolve({});
 await oldMount;
 await page.mount();

 assert.equal(openings,2);
 assert.equal(abandoned.slots.builtin.children.length,0);
 assert.equal(abandoned.slots.custom.hidden,false);
 assert.equal(reopened.slots.builtin.children.length,1);
 assert.equal(reopened.slots.custom.hidden,true);
 assert.equal(reopened.status.textContent,'Connected to default');
 assert.equal(page.currentHost,reopened.host);
 assert.equal(selections.length,1);
});

test('a session read from a closed Processes page cannot replace the reopened view',async()=>{
 const started=deferred();
 const delayedSession=deferred();
 let reads=0;
 const page=processPage({readSession:async session=>{
  if(++reads===1){
   started.resolve();
   await delayedSession.promise;
  }
  return session;
 }});
 const abandoned=page.active;
 const oldMount=page.mount();
 await started.promise;

 const reopened=page.reopen();
 await page.mount();
 delayedSession.resolve();
 await oldMount;

 assert.equal(page.currentHost,reopened.host);
 assert.equal(abandoned.slots.builtin.children.length,0);
 assert.equal(reopened.slots.builtin.children.length,1);
});

test('editing a process keeps an earlier preparation out of the workspace selection',async()=>{
 const pending=deferred();
 const selected=[];
 const workspace={session:{principalId:'actor',environmentId:'default'},select:async(_,task)=>selected.push(task)};
 const previous={packageId:'buffer',version:1,contentHash:'a'.repeat(64)};
 const current={packageId:'buffer',version:2,contentHash:'b'.repeat(64)};
 let preparations=0;
 const {slots}=await fixture({workspace,prepare:()=>++preparations===1 ? pending.promise : current});
 const [builtin]=slots.builtin.children;

 const stale=builtin.client.prepare();
 builtin.listeners.get('input')();
 pending.resolve(previous);
 assert.equal(await stale,previous);
 builtin.listeners.get('focusin')();
 assert.deepEqual(selected,[null,null,null]);

 assert.equal(await builtin.client.prepare(),current);
 assert.deepEqual(selected.at(-1),processTask(current));
});

test('the latest Processes preparation stays selected when the earlier response arrives last',async()=>{
 const first=deferred();
 const second=deferred();
 const selected=[];
 const workspace={session:{principalId:'actor',environmentId:'default'},select:async(_,task)=>selected.push(task)};
 const previous={packageId:'buffer',version:1,contentHash:'a'.repeat(64)};
 const current={packageId:'buffer',version:2,contentHash:'b'.repeat(64)};
 const responses=[first.promise,second.promise];
 const {slots}=await fixture({workspace,prepare:()=>responses.shift()});
 const [builtin]=slots.builtin.children;

 const earlier=builtin.client.prepare();
 const latest=builtin.client.prepare();
 second.resolve(current);
 assert.equal(await latest,current);
 first.resolve(previous);
 assert.equal(await earlier,previous);
 builtin.listeners.get('focusin')();

 const task=processTask(current);
 assert.deepEqual(selected,[null,task,task]);
});

test('switching to a local process keeps it selected while built-in preparation completes',async()=>{
 const pending=deferred();
 const selected=[];
 const workspace={session:{principalId:'actor',environmentId:'default'},select:async(_,task)=>selected.push(structuredClone(task))};
 const builtInIdentity={packageId:'buffer',version:1,contentHash:'a'.repeat(64)};
 const localIdentity={projectId:'b'.repeat(32),revision:1,sourceDigest:'c'.repeat(64)};
 const localSelection={kind:'local-process-workspace',id:localIdentity.projectId,revision:1,contentHash:localIdentity.sourceDigest};
 const {slots}=await fixture({native:false,workspace,prepare:()=>pending.promise});
 const [builtin]=slots.builtin.children;
 const custom=slots.custom.child;

 const preparation=builtin.client.prepare();
 custom.listeners.get('focusin')();
 custom.listeners.get('honua-local-process-task-change')({detail:localIdentity});
 assert.deepEqual(selected,[null,null,localSelection]);

 pending.resolve(builtInIdentity);
 assert.equal(await preparation,builtInIdentity);
 assert.deepEqual(selected,[null,null,localSelection]);

 builtin.listeners.get('focusin')();
 custom.listeners.get('focusin')();
 assert.deepEqual(selected,[null,null,localSelection,processTask(builtInIdentity),localSelection]);
});

test('retrying a failed Processes preparation selects the successful result',async()=>{
 const selected=[];
 const workspace={session:{principalId:'actor',environmentId:'default'},select:async(_,task)=>selected.push(task)};
 const prepared={packageId:'buffer',version:1,contentHash:'a'.repeat(64)};
 const failure=new Error('Process preparation service unavailable');
 let attempts=0;
 const {slots}=await fixture({workspace,prepare:async()=>{
  if(++attempts===1) throw failure;
  return prepared;
 }});
 const [builtin]=slots.builtin.children;

 await assert.rejects(builtin.client.prepare(),error=>error===failure);
 assert.deepEqual(selected,[null]);

 assert.equal(await builtin.client.prepare(),prepared);
 assert.deepEqual(selected,[null,processTask(prepared)]);
});

test('a preparation from a closed Processes page leaves the reopened selection intact',async()=>{
 const pending=deferred();
 const selected=[];
 const workspace={session:{principalId:'actor',environmentId:'default'},select:async(_,task)=>selected.push(task)};
 const previous={packageId:'buffer',version:1,contentHash:'a'.repeat(64)};
 const current={packageId:'buffer',version:2,contentHash:'b'.repeat(64)};
 const responses=[pending.promise,current];
 const page=processPage({workspace,prepare:()=>responses.shift()});
 await page.mount();
 const [abandoned]=page.active.slots.builtin.children;
 const earlier=abandoned.client.prepare();

 const reopened=page.reopen();
 await page.mount();
 const [builtin]=reopened.slots.builtin.children;
 assert.equal(await builtin.client.prepare(),current);
 pending.resolve(previous);
 assert.equal(await earlier,previous);

 assert.deepEqual(selected,[null,null,processTask(current)]);
});
