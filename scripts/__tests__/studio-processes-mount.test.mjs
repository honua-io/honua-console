import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import vm from 'node:vm';
import {connectStudioHost} from '../../src/Honua.Console.Web/wwwroot/js/studio-bundle.mjs';

const source=await readFile(new URL('../../src/Honua.Console.Web/wwwroot/js/studio-embed.mjs',import.meta.url),'utf8');
const mount=source.slice(source.indexOf('async function mountProcesses()'),source.indexOf('new MutationObserver(mountProcesses)')).replaceAll('import.meta.url',JSON.stringify('https://console.example/js/studio-embed.mjs'));

function processHost() {
 const slots={builtin:{children:[],append(child){this.children.push(child);}},custom:{append(){},hidden:false}};
 const status={textContent:''};
 const host={dataset:{},isConnected:true,querySelector:selector=>selector==='[data-processes-status]' ? status : slots[selector.includes('builtin') ? 'builtin' : 'custom']};
 return {host,slots,status};
}

function processPage({native=true,current=true,workspace,openStudio=async()=>({})}={}) {
 let active=processHost();
 const session={principalId:'actor',environmentId:'default',environmentName:'default',
  ...(native ? {transport:{assertCurrent:async()=>{if(!current) throw new Error('Console session changed');}}} : {})};
 class Workspace {prepare() {}}
 const context=vm.createContext({assets:Promise.resolve({registerAllStudioElements(){},ServerProcessWorkspace:Workspace,LocalProcessWorkspace:Workspace}),
  document:{querySelector:()=>active.host,createElement:()=>({addEventListener(){}})},window:{honuaAgentWorkspace:workspace},
  readStudioSession:async()=>session,studioElement:openStudio,capturedProcessFetch:()=>()=>{},
  connectStudioHost,processView:undefined,StudioSignInRequired:Error});
 // The resolved asset promise avoids the production dynamic import in this isolated DOM.
 vm.runInContext(mount,context);
 return {
  get active(){return active;},
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

test('reopening Processes during setup mounts only the current host',async()=>{
 const started=deferred();
 const delayedStudio=deferred();
 let openings=0;
 const page=processPage({openStudio:()=>{
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
});
