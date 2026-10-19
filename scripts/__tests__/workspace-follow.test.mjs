import test from 'node:test';
import assert from 'node:assert/strict';
import {captureWorkspaceFollow,queueWorkspaceFollow} from '../../src/Honua.Console.Web/wwwroot/js/workspace-follow.mjs';
const task={kind:'workflow-package',id:'one',revision:1,contentHash:'a'.repeat(64)};
function fixture(){return {workspaceId:'workspace',contextGeneration:1,session:{principalId:'principal',environmentId:'environment',releaseId:'release',sourcePathBase:'/honua'},context:{task:{...task}}};}
test('follow survives identical lease renewal but refuses a new authority or context',()=>{
 const workspace=fixture();const current=captureWorkspaceFollow(workspace,task,()=>true);
 workspace.session={...workspace.session,csrfToken:'renewed'};assert.equal(current(),true);
 workspace.contextGeneration++;assert.equal(current(),false);
});
test('slow task A cannot report completion after B or navigation replaces its view',()=>{
 const workspace=fixture();let connected=true;const current=captureWorkspaceFollow(workspace,task,()=>connected);
 workspace.context.task={...task,id:'two'};assert.equal(current(),false);
 workspace.context.task={...task};connected=false;assert.equal(current(),false);
});
test('same task cannot follow into another workspace, principal or upstream path',()=>{
 for(const change of [w=>w.workspaceId='other',w=>w.session.principalId='other',w=>w.session.sourcePathBase='/other']) {
  const workspace=fixture();const current=captureWorkspaceFollow(workspace,task,()=>true);change(workspace);assert.equal(current(),false);
 }
});

const tick=()=>new Promise(resolve=>setImmediate(resolve));
const queueFixture=()=>({workspaceId:"w",viewId:"v",contextGeneration:1,session:{principalId:"p"},context:{task:{kind:"workflow-package",id:"a",revision:1}}});
test('identical polling shares pending observation and cannot starve it',async()=>{
 const workspace=queueFixture(),owner={};let release,count=0;const observe=async current=>{count++;await new Promise(resolve=>release=resolve);assert.equal(current(),true);};
 const first=queueWorkspaceFollow(owner,workspace,workspace.context.task,()=>true,observe,assert.fail);await tick();
 for(let i=0;i<10;i++) assert.equal(queueWorkspaceFollow(owner,workspace,workspace.context.task,()=>true,observe,assert.fail),first);
 release();await first;assert.equal(count,1);assert.equal(owner.workspaceFollowPending,false);
});
test('changed task invalidates pending completion and schedules latest observation',async()=>{
 const workspace=queueFixture(),owner={},accepted=[];let release;
 const first=queueWorkspaceFollow(owner,workspace,workspace.context.task,()=>true,async current=>{await new Promise(resolve=>release=resolve);if(current())accepted.push('a');},assert.fail);
 await tick();workspace.context.task={...workspace.context.task,id:'b'};
 const second=queueWorkspaceFollow(owner,workspace,workspace.context.task,()=>true,async current=>{if(current())accepted.push('b');},assert.fail);
 release();await Promise.all([first,second]);assert.deepEqual(accepted,['b']);
});
