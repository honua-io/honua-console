import test from 'node:test';
import assert from 'node:assert/strict';
import {readSharedStyleFocus,prepareSharedStyleFocus} from '../../src/Honua.Console.Web/wwwroot/js/shared-style-context.mjs';
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
