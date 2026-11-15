import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import vm from 'node:vm';

const source=await readFile(new URL('../../src/Honua.Console.Web/wwwroot/js/studio-embed.mjs',import.meta.url),'utf8');
const mount=source.slice(source.indexOf('async function mountProcesses()'),source.indexOf('new MutationObserver(mountProcesses)')).replaceAll('import.meta.url',JSON.stringify('https://console.example/js/studio-embed.mjs'));

async function fixture({native=true,current=true,workspace}={}) {
 const slots={builtin:{children:[],append(child){this.children.push(child);}},custom:{append(){},hidden:false}};
 const status={textContent:''};
 const host={dataset:{},isConnected:true,querySelector:selector=>selector==='[data-processes-status]' ? status : slots[selector.includes('builtin') ? 'builtin' : 'custom']};
 const session={principalId:'actor',environmentId:'default',environmentName:'default',
  ...(native ? {transport:{assertCurrent:async()=>{if(!current) throw new Error('Console session changed');}}} : {})};
 class Workspace {prepare() {}}
 const context=vm.createContext({assets:Promise.resolve({registerAllStudioElements(){},ServerProcessWorkspace:Workspace,LocalProcessWorkspace:Workspace}),
  document:{querySelector:()=>host,createElement:()=>({addEventListener(){}})},window:{honuaAgentWorkspace:workspace},
  readStudioSession:async()=>session,studioElement:async()=>({}),capturedProcessFetch:()=>()=>{},
  processView:undefined,StudioSignInRequired:Error});
 // The resolved asset promise avoids the production dynamic import in this isolated DOM.
 await vm.runInContext(mount+';mountProcesses()',context);
 return {slots,status};
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
