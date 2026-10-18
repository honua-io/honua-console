import test from 'node:test';
import assert from 'node:assert/strict';
import {capturedProcessFetch} from '../../src/Honua.Console.Web/wwwroot/js/cookie-workspace-transport.mjs';
test('Processes renews CSRF under its original captured authority',async()=>{
 const binding={principalId:'partition',environmentId:'environment',sourcePathBase:'/honua',csrfToken:'old'};
 const renewed={...binding,csrfToken:'new'};let sent;
 const request=capturedProcessFetch(binding,{current:()=>renewed,request:async(input,init)=>{sent={input,init};}});
 await request('/api/processes/local/runs',{method:'POST',headers:{'X-Honua-Session-Partition':'untrusted','X-CSRF-Token':'stale'},body:'{}'});
 assert.equal(sent.init.headers.get('X-Honua-Session-Partition'),'partition');assert.equal(sent.init.headers.get('X-CSRF-Token'),'new');assert.equal(sent.init.body,'{}');
});
test('Processes never borrows another authority or origin',()=>{
 const binding={principalId:'partition',environmentId:'environment',sourcePathBase:'/honua'};
 for(const key of ['principalId','environmentId','sourcePathBase']) {
  const request=capturedProcessFetch(binding,{current:()=>({...binding,[key]:'changed'}),request:()=>assert.fail('must not send')});
  assert.throws(()=>request('/api/processes/local/runs'),/connection changed/);
 }
 assert.throws(()=>capturedProcessFetch(binding)('https://elsewhere.test/api/processes/local/runs'),/Unapproved/);
});
