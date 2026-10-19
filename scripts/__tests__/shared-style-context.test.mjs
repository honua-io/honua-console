import test from 'node:test';
import assert from 'node:assert/strict';
import {readSharedStyleFocus,prepareSharedStyleFocus,watchSharedStyleEdits} from '../../src/Honua.Console.Web/wwwroot/js/shared-style-context.mjs';
const dataset=()=>({styleTaskId:'catalog-style',styleTaskRevision:'7',styleContentHash:'a'.repeat(64),styleImpactHash:'b'.repeat(64),styleDirty:'false',stylePending:'false'});
const root=data=>({querySelector:()=>({dataset:data})});
test('shared style context captures only immutable actual review references',()=>{
 const data=dataset();data.stylesheet='secret';const focus=readSharedStyleFocus(root(data));
 data.styleTaskRevision='8';data.styleContentHash='c'.repeat(64);
 assert.deepEqual(prepareSharedStyleFocus(focus),{kind:'shared-style',id:'catalog-style',revision:7,contentHash:'a'.repeat(64),impactHash:'b'.repeat(64)});
 assert.equal(JSON.stringify(focus).includes('secret'),false);
});
test('dirty, pending and unversioned styles cannot lend a prior map context',()=>{
 for(const [field,value] of [['styleDirty','true'],['stylePending','true'],['styleTaskRevision','0'],['styleContentHash','bad']]){
  const data=dataset();data[field]=value;assert.throws(()=>prepareSharedStyleFocus(readSharedStyleFocus(root(data))));
 }
});
test('other views do not create a shared-style focus',()=>assert.equal(readSharedStyleFocus({querySelector:()=>null}),undefined));

test('browser input blocks prompt before Razor update and ordinary rerender cannot clear it',()=>{
 const editor={dataset:{...dataset(),styleObservationEpoch:'1'}};
 let input;const document={querySelector:()=>editor,addEventListener:(name,callback)=>{assert.equal(name,'input');input=callback;}};
 watchSharedStyleEdits(document);assert.equal(readSharedStyleFocus(document).dirty,false);
 input({target:{closest:()=>({closest:()=>editor})}});
 assert.throws(()=>prepareSharedStyleFocus(readSharedStyleFocus(document)),/Save or discard/);
 editor.dataset.styleDirty='false';assert.equal(readSharedStyleFocus(document).dirty,true);
 editor.dataset.styleObservationEpoch='2';assert.equal(readSharedStyleFocus(document).dirty,false);
 assert.equal(prepareSharedStyleFocus(readSharedStyleFocus(document)).id,'catalog-style');
});


test('style observations cannot be combined with another workspace connection',()=>{
 const data=dataset();data.styleAuthorityBinding='original-partition';
 const focus=readSharedStyleFocus(root(data),'replacement-partition');
 assert.equal(focus.task,null);
 assert.throws(()=>prepareSharedStyleFocus(focus,'replacement-partition'),/another connection/);
 const accepted=readSharedStyleFocus(root(data),'original-partition');
 assert.equal(prepareSharedStyleFocus(accepted,'original-partition').id,'catalog-style');
 assert.equal(Object.hasOwn(prepareSharedStyleFocus(accepted,'original-partition'),'authorityBinding'),false);
});
