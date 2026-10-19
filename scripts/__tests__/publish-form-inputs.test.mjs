import test from 'node:test';
import assert from 'node:assert/strict';
import {createPublishInputBinder} from '../../src/Honua.Console.Web/wwwroot/js/publish-form-inputs.mjs';
function input(field,value='') {const node=new EventTarget();node.dataset={workspaceField:field};node.value=value;return node;}
test('fields added after initial empty render join dirty tracking exactly once',async()=>{
 const fields=[],edits=[];const workspace={context:{},edit:(...args)=>edits.push(args)};
 const bind=createPublishInputBinder(workspace);const form={querySelectorAll:()=>fields};
 bind(form);await Promise.resolve();
 const field=input('layerName','Later input');fields.push(field);bind(form);bind(form);
 field.dispatchEvent(new Event('input'));assert.deepEqual(edits,[['layerName','Later input']]);
 const replacement=input('serviceName','Replacement');fields.splice(0,1,replacement);bind(form);
 replacement.dispatchEvent(new Event('input'));assert.deepEqual(edits.at(-1),['serviceName','Replacement']);
});
test('table bindings capture canonical schema/table instead of display value and ignore reconciliation',()=>{
 const edits=[];let reconciling=false;const workspace={context:{},edit:(...args)=>edits.push(args)};
 const field=input('table','display');field.selectedOptions=[{dataset:{schema:'actual_schema',table:'actual_table'}}];
 const bind=createPublishInputBinder(workspace,{shouldIgnore:()=>reconciling});bind({querySelectorAll:()=>[field]});
 reconciling=true;field.dispatchEvent(new Event('input'));assert.deepEqual(edits,[]);
 reconciling=false;field.dispatchEvent(new Event('input'));assert.deepEqual(edits,[['schema','actual_schema'],['table','actual_table']]);
 workspace.context=null;field.dispatchEvent(new Event('input'));assert.equal(edits.length,2);
});
test('disposed host stops forwarding retained input nodes',()=>{
 const edits=[],lifetime=new AbortController(),field=input('layerName','retained');
 createPublishInputBinder({context:{},edit:(...args)=>edits.push(args)},{signal:lifetime.signal})({querySelectorAll:()=>[field]});
 lifetime.abort();field.dispatchEvent(new Event('input'));assert.deepEqual(edits,[]);
});
