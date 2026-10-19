import test from 'node:test';
import assert from 'node:assert/strict';
import {processTask} from '../../src/Honua.Console.Web/wwwroot/js/workflow-task.mjs';
const identity=(packageId,version=1)=>({packageId,version,contentHash:'a'.repeat(64)});
test('server-compatible opaque Unicode package identity and positive Int32 version',()=>{
 for(const id of ['pkg-ʻāina','東京','a b','a/b?#%','\ufeffa','a'.repeat(256)])
  assert.deepEqual(processTask(identity(id,2147483647)),{kind:'workflow-package',id,revision:2147483647,contentHash:'a'.repeat(64)});
});
test('server-compatible control and surrounding whitespace rejection',()=>{
 for(let n=0;n<=0x9f;n++) if(n<=0x1f||n>=0x7f) assert.throws(()=>processTask(identity(`a${String.fromCharCode(n)}b`)));
 for(const space of [' ','\u00a0','\u1680','\u2000','\u2028','\u2029','\u202f','\u205f','\u3000'])
  for(const id of [`${space}a`,`a${space}`]) assert.throws(()=>processTask(identity(id)));
 for(const id of ['', 'a'.repeat(257)]) assert.throws(()=>processTask(identity(id)));
});
test('immutable version and lowercase SHA256 pins remain mandatory',()=>{
 for(const version of [0,-1,1.5,2147483648,Number.MAX_SAFE_INTEGER,NaN,Infinity,'1']) assert.throws(()=>processTask(identity('a',version)));
 for(const hash of ['',null,'A'.repeat(64),'a'.repeat(63),'g'.repeat(64)]) assert.throws(()=>processTask({...identity('a'),contentHash:hash}));
});
