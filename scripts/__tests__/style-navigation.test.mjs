import test from 'node:test';import assert from 'node:assert/strict';
import {mayLeaveStyleEditor} from '../../src/Honua.Console.Web/wwwroot/js/style-navigation.mjs';
test('cancel retains buffer; confirmed discard awaits real accepted reread',async()=>{
 let complete,calls=0;const app={hasUnsavedStyleChanges:true,async discardStyleChanges(){calls++;await new Promise(resolve=>complete=resolve);this.hasUnsavedStyleChanges=false;}};
 const current=()=>({app,binding:'same-authority/view/draft'});
 assert.equal(await mayLeaveStyleEditor({current,confirm:()=>false}),false);assert.equal(calls,0);
 let settled=false;const leave=mayLeaveStyleEditor({current,confirm:()=>true}).then(result=>(settled=true,result));
 await Promise.resolve();assert.equal(settled,false);assert.equal(calls,1);complete();assert.equal(await leave,true);
});
test('failed reread or changed view never permits stale navigation',async()=>{
 const failed={hasUnsavedStyleChanges:true,discardStyleChanges:async()=>{throw new Error('Forbidden');}};
 assert.equal(await mayLeaveStyleEditor({current:()=>({app:failed,binding:'old'}),confirm:()=>true}),false);
 let complete,binding='old';const app={hasUnsavedStyleChanges:true,async discardStyleChanges(){await new Promise(resolve=>complete=resolve);this.hasUnsavedStyleChanges=false;}};
 const leave=mayLeaveStyleEditor({current:()=>({app,binding}),confirm:()=>true});await Promise.resolve();binding='new';complete();assert.equal(await leave,false);
});
