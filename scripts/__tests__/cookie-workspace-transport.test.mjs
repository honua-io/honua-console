import test from 'node:test';
import assert from 'node:assert/strict';
import {createConsoleBrowserTransport} from '../../src/Honua.Console.Web/wwwroot/js/console-browser-transport.mjs';
import {capturedProcessFetch, nativeProcessRoute, createStudioCookieSession, sameCookieAuthority, scheduleCookieSessionRenewal} from '../../src/Honua.Console.Web/wwwroot/js/cookie-workspace-transport.mjs';

function leaseClock(t) {
 let now=Date.parse('2026-10-24T00:00:00Z'),next=0;
 const timers=new Map();
 t.mock.method(Date,'now',()=>now);
 t.mock.method(globalThis,'setTimeout',(callback,delay)=>{
  const id=++next;timers.set(id,{callback,delay});return id;
 });
 t.mock.method(globalThis,'clearTimeout',id=>timers.delete(id));
 return {timers,advance(ms){now+=ms;},expiry(ms){return new Date(now+ms).toISOString();},
  next(){return [...timers.entries()].at(-1);},
  fire(id){const timer=timers.get(id);assert.ok(timer);timers.delete(id);return timer.callback();}};
}
const lease=expiresAt=>({principalId:'partition',environmentId:'environment',sourcePathBase:'/honua',csrfToken:'csrf',expiresAt});

test('renewal replaces a one-minute Studio cookie lease before its expiry notification',async t=>{
 const clock=leaseClock(t);let current=lease(clock.expiry(60000)),expired=0,renewals=0;
 let dispose=createStudioCookieSession(current).onExpired(()=>expired++);
 const stop=scheduleCookieSessionRenewal(()=>current,async()=>{
  renewals++;dispose();current={...current,expiresAt:clock.expiry(60000)};
  dispose=createStudioCookieSession(current).onExpired(()=>expired++);
 });
 let [id,timer]=clock.next();assert.equal(timer.delay,30000);
 clock.advance(30000);await clock.fire(id);assert.equal(renewals,1);
 [id,timer]=clock.next();assert.equal(timer.delay,30000);
 clock.advance(30000);await clock.fire(id);assert.equal(renewals,2);assert.equal(expired,0);
 stop();dispose();assert.equal(clock.timers.size,0);
});

test('renewal discovers a mounted session and retries failures without overlapping requests',async t=>{
 const clock=leaseClock(t);let current,calls=0,finish;
 const stop=scheduleCookieSessionRenewal(()=>current,()=>{
  calls++;return new Promise(resolve=>{finish=resolve;});
 });
 let [id,timer]=clock.next();assert.equal(timer.delay,1000);
 current=lease(clock.expiry(60000));clock.advance(1000);
 const pending=clock.fire(id);assert.equal(calls,1);assert.equal(clock.timers.size,0);
 finish();await pending;
 [id,timer]=clock.next();assert.equal(timer.delay,29500);
 stop();assert.equal(clock.timers.size,0);
 let attempts=0;
 const cancel=scheduleCookieSessionRenewal(()=>lease(clock.expiry(-1000)),async()=>{attempts++;throw new Error('offline');});
 [id,timer]=clock.next();assert.equal(timer.delay,1000);
 await clock.fire(id);assert.equal(attempts,1);assert.equal(clock.next()[1].delay,1000);
 cancel();assert.equal(clock.timers.size,0);
});

test('stopping during an in-flight renewal does not restart its timer',async t=>{
 const clock=leaseClock(t);let finish;
 const stop=scheduleCookieSessionRenewal(()=>lease(clock.expiry(60000)),()=>new Promise(resolve=>{finish=resolve;}));
 const pending=clock.fire(clock.next()[0]);stop();finish();await pending;
 assert.equal(clock.timers.size,0);
});

test('Studio cookie adapter snapshots only allowed fields and rejects invalid expiry',t=>{
 const clock=leaseClock(t);
 const session={...lease(clock.expiry(5000)),accessToken:'not-projected',fetchRequest:'not-projected',baseUrl:'https://untrusted.test'};
 const adapter=createStudioCookieSession(session);
 assert.deepEqual(Object.keys(adapter).sort(),['baseUrl','csrfToken','environmentId','expiresAt','kind','onExpired','principalId','sourcePathBase','tenantId'].sort());
 assert.equal(adapter.kind,'cookie');assert.equal(adapter.baseUrl,'/api/studio');assert.ok(Object.isFrozen(adapter));
 session.csrfToken='changed';session.expiresAt='invalid';
 assert.equal(adapter.csrfToken,'csrf');assert.equal(adapter.expiresAt,clock.expiry(5000));
 for(const expiresAt of [undefined,null,0,'','invalid','2026-99-99T00:00:00Z'])
  assert.throws(()=>createStudioCookieSession(lease(expiresAt)),/lease expiry is invalid/);
 assert.throws(()=>adapter.onExpired(undefined),/expiry listener/);
 assert.equal(clock.timers.size,0);
});

test('native Studio delegates credentials, CSRF and origin to the captured Console transport',async()=>{
 for(const crossOrigin of [false,true]) {
  let sent;
  let snapshot={authenticated:true,principalId:'operator',revision:1,expiresAt:new Date(Date.now()+60000).toISOString(),
   csrfToken:'host-csrf',...(crossOrigin?{accessToken:'host-bearer'}:{})};
  const browser=createConsoleBrowserTransport({apiBaseUrl:crossOrigin?'https://api.test/deploy/':'https://console.test/deploy/',
   applicationBaseUrl:'https://console.test/',getSession:async()=>snapshot,request:async(url,init)=>{sent={url,init};return {ok:true};}});
  const session={...lease(snapshot.expiresAt),transport:await browser.capture(),fetchRequest:()=>assert.fail('untrusted callback'),baseUrl:'https://untrusted.test'};
  const adapter=createStudioCookieSession(session);
  assert.equal(adapter.baseUrl,'/api/v1/studio');assert.ok(Object.isFrozen(adapter));
  session.transport=undefined;
  await adapter.fetchRequest('api/v1/studio/drafts',{method:'POST',body:'{}',redirect:'follow',credentials:'include',
   headers:{Authorization:'Bearer injected',Cookie:'injected','X-CSRF-Token':'injected','X-Honua-CSRF':'injected','X-API-Key':'injected'}});
  assert.equal(sent.url,`${crossOrigin?'https://api.test':'https://console.test'}/deploy/api/v1/studio/drafts`);
  assert.equal(sent.init.headers.get('Authorization'),crossOrigin?'Bearer host-bearer':null);
  assert.equal(sent.init.headers.get('X-Honua-CSRF'),crossOrigin?null:'host-csrf');
  for(const header of ['Cookie','X-CSRF-Token','X-API-Key']) assert.equal(sent.init.headers.has(header),false);
  assert.equal(sent.init.credentials,crossOrigin?'omit':'same-origin');assert.equal(sent.init.redirect,'error');
  assert.equal(sent.init.body,'{}');
  sent=undefined;
  for(const route of ['https://untrusted.test/api/v1/studio/drafts','/api/v1/studio/drafts','../api/v1/studio/drafts'])
   await assert.rejects(adapter.fetchRequest(route),/relative Honua API route/);
  assert.equal(sent,undefined);
  snapshot={...snapshot,revision:2};
  await assert.rejects(adapter.fetchRequest('api/v1/studio/drafts'),/session changed/);
  assert.equal(sent,undefined);
 }
});

test('native Studio never falls back to browser fetch for an invalid captured transport',()=>{
 assert.throws(()=>createStudioCookieSession({...lease(new Date(Date.now()+60000).toISOString()),transport:{}}),TypeError);
});

test('Studio expiry rechecks the deadline and notifies once even if a timer fires early',t=>{
 const clock=leaseClock(t);let expired=0;
 const unsubscribe=createStudioCookieSession(lease(clock.expiry(5000))).onExpired(()=>expired++);
 let [id,timer]=clock.next();assert.equal(timer.delay,5000);
 clock.advance(4999);clock.fire(id);assert.equal(expired,0);
 [id,timer]=clock.next();assert.equal(timer.delay,1);
 const lateCallback=timer.callback;
 clock.advance(1);clock.fire(id);assert.equal(expired,1);assert.equal(clock.timers.size,0);
 lateCallback();unsubscribe();unsubscribe();assert.equal(expired,1);
});

test('Studio expiry chunks long leases instead of overflowing the browser timer',t=>{
 const clock=leaseClock(t),maximumDelay=2147483647;let expired=0;
 createStudioCookieSession(lease(clock.expiry(maximumDelay+5000))).onExpired(()=>expired++);
 let [id,timer]=clock.next();assert.equal(timer.delay,maximumDelay);
 clock.advance(maximumDelay);clock.fire(id);assert.equal(expired,0);
 [id,timer]=clock.next();assert.equal(timer.delay,5000);
 clock.advance(5000);clock.fire(id);assert.equal(expired,1);assert.equal(clock.timers.size,0);
});

test('disposing an old Studio lease subscription prevents expiry after same-authority renewal',t=>{
 const clock=leaseClock(t);let oldExpired=0,newExpired=0;
 const old=createStudioCookieSession(lease(clock.expiry(1000)));
 const unsubscribeOld=old.onExpired(()=>oldExpired++);
 const staleCallback=clock.next()[1].callback;
 unsubscribeOld();assert.equal(clock.timers.size,0);
 const renewed=createStudioCookieSession({...lease(clock.expiry(10000)),csrfToken:'renewed-csrf'});
 const unsubscribeNew=renewed.onExpired(()=>newExpired++);
 clock.advance(1000);staleCallback();assert.equal(oldExpired,0);assert.equal(newExpired,0);
 const [id,timer]=clock.next();assert.equal(timer.delay,10000);
 clock.advance(9000);clock.fire(id);assert.equal(newExpired,1);assert.equal(oldExpired,0);
 unsubscribeNew();assert.equal(clock.timers.size,0);
});

test('an already expired Studio lease uses a cancellable asynchronous notification',t=>{
 const clock=leaseClock(t);let expired=0;
 const adapter=createStudioCookieSession(lease(clock.expiry(-1)));
 const unsubscribe=adapter.onExpired(()=>expired++);
 const [id,timer]=clock.next();assert.equal(timer.delay,0);assert.equal(expired,0);
 unsubscribe();timer.callback();assert.equal(expired,0);assert.equal(clock.timers.has(id),false);
 adapter.onExpired(()=>expired++);clock.fire(clock.next()[0]);assert.equal(expired,1);
});

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

test('native Processes maps the legacy BFF routes onto api/v1 with its method rules',()=>{
 const ok=[['GET','console/workflow-node-registry'],['POST','console/workflow-packages'],['GET','console/workflow-packages'],
  ['POST','console/workflow-packages/pkg%20one/versions'],['GET','console/workflow-packages/pkg/versions/2'],
  ['POST','console/workflow-packages/pkg/versions/2/validate'],['POST','console/workflow-packages/pkg/versions/2/publish'],
  ['GET','console/workflow-publications'],['POST','console/workflow-publications/pub_1/runs'],['GET','operations/handles/op-1'],
  ['GET','admin/operations/op-1'],['POST','admin/operations/op-1/cancel'],['GET','admin/proposals/p1'],
  ['POST','admin/import/upload'],['GET','admin/import/jobs/j1'],['POST','admin/import/jobs/j1/cancel'],['GET','admin/connections/c1/layers']];
 for(const [method,path] of ok) assert.equal(nativeProcessRoute(`/api/processes/${path}`,method),`api/v1/${path}`,`${method} ${path}`);
 const refused=[['POST','admin/operations/op-1'],['GET','admin/operations/op-1/cancel'],['GET','console/workflow-packages/pkg/versions/2/publish'],
  ['POST','console/workflow-packages/pkg/versions/2'],['DELETE','console/workflow-packages'],['GET','admin/users'],['GET','admin/proposals/../secrets'],
  ['GET','console/workflow-packages/%2E%2E/versions'],['GET','console/workflow-packages/a%2Fb/versions'],['GET','console/workflow-packages/%20a/versions'],['GET','console/workflow-packages/p?x=/versions'],['GET','console/workflow-packages/p#x/versions']];
 for(const [method,path] of refused) assert.throws(()=>nativeProcessRoute(`/api/processes/${path}`,method),/Unapproved/,`${method} ${path}`);
 assert.throws(()=>nativeProcessRoute('https://elsewhere.test/api/processes/admin/proposals/p1'),/Unapproved/);
 assert.throws(()=>nativeProcessRoute('/api/processes/local/runs','POST'),/not available on this server/);
});

test('native Processes sends through the Console transport without legacy host headers',async()=>{
 let sent;
 const transport={principalId:'actor',fetch:async(route,init)=>{sent={route,init};return {ok:true};}};
 const binding={principalId:'actor',environmentId:'default',sourcePathBase:'',transport};
 const request=capturedProcessFetch(binding,{current:()=>binding,request:()=>assert.fail('legacy request must not be used')});
 await request('/api/processes/console/workflow-packages',{method:'POST',body:'{}',headers:{'X-Honua-Session-Partition':'actor','X-Honua-Environment':'default','X-CSRF-Token':'undefined'}});
 assert.equal(sent.route,'api/v1/console/workflow-packages');assert.equal(sent.init.method,'POST');assert.equal(sent.init.body,'{}');
 assert.equal(sent.init.headers.has('X-Honua-Session-Partition'),false);assert.equal(sent.init.headers.has('X-Honua-Environment'),false);
});

test('native Processes never borrows another authority or transport',()=>{
 const transport={principalId:'actor',fetch:()=>assert.fail('must not send')};
 const binding={principalId:'actor',environmentId:'default',sourcePathBase:'',transport};
 for(const key of ['principalId','environmentId','sourcePathBase']) {
  const request=capturedProcessFetch(binding,{current:()=>({...binding,[key]:'changed'})});
  assert.throws(()=>request('/api/processes/admin/proposals/p1'),/connection changed/);
 }
 const swapped=capturedProcessFetch(binding,{current:()=>({...binding,transport:{principalId:'other'}})});
 assert.throws(()=>swapped('/api/processes/admin/proposals/p1'),/connection changed/);
});

test('retained native Processes clients use a renewed same-authority capture without replay',async()=>{
 let oldCalls=0,newCalls=0;
 const binding={principalId:'actor',environmentId:'default',sourcePathBase:'',transport:{principalId:'actor',fetch:()=>{oldCalls++;throw new Error('expired capture');}}};
 const renewed={...binding,transport:{principalId:'actor',fetch:async()=>{newCalls++;return {ok:true};}}};
 const request=capturedProcessFetch(binding,{current:()=>renewed});
 await request('/api/processes/console/workflow-packages',{method:'POST',body:'{}'});
 assert.equal(oldCalls,0);assert.equal(newCalls,1);
});

for(const method of ['json','text']) test(`native Processes rejects session changes during ${method} consumption without replay`,async()=>{
 let active=true,calls=0;
 const transport={principalId:'actor',assertCurrent:async()=>{if(!active) throw new Error('Console session changed');},
  fetch:async()=>{calls++;return {[method]:async()=>{active=false;return method==='json' ? {data:'old graph'} : 'old graph';}};}};
 const binding={principalId:'actor',environmentId:'default',sourcePathBase:'',transport};
 const response=await capturedProcessFetch(binding)('/api/processes/console/workflow-packages',{method:'POST',body:'{}'});
 await assert.rejects(response[method](),/session changed/);
 assert.equal(calls,1);
});

test('native Processes returns current JSON and protects cloned response bodies',async()=>{
 let active=true;
 const transport={principalId:'actor',assertCurrent:async()=>{if(!active) throw new Error('Console session changed');},fetch:async()=>new Response('{"data":"graph"}')};
 const binding={principalId:'actor',environmentId:'default',sourcePathBase:'',transport};
 const response=await capturedProcessFetch(binding)('/api/processes/console/workflow-packages');
 const clone=response.clone();
 assert.equal(response.ok,true);
 assert.deepEqual(await response.json(),{data:'graph'});
 active=false;
 await assert.rejects(clone.json(),/session changed/);
});

for (const method of ['json','text']) test(`native Studio rejects a session switch during ${method} consumption without replaying a write`,async()=>{
 let active=true,finish,calls=0;
 const transport={assertCurrent:async()=>{if(!active) throw new Error('Console session changed');},
  fetch:async()=>{calls++;return {[method]:()=>new Promise(resolve=>{finish=resolve;})};}};
 const adapter=createStudioCookieSession({...lease(new Date(Date.now()+60000).toISOString()),transport});
 const response=await adapter.fetchRequest('api/v1/studio/drafts',{method:'POST',body:'{}'});
 const reading=response[method]();
 await Promise.resolve();
 active=false;finish(method==='json'?{draftId:'old'}:'old');
 await assert.rejects(reading,/session changed/);
 assert.equal(calls,1);
});

test('native Studio reconstructed responses reject a session switch during a streamed body',async()=>{
 let active=true,finish,calls=0;
 const source=new ReadableStream({pull(controller){return new Promise(resolve=>{finish=()=>{controller.enqueue(new TextEncoder().encode('{"draftId":"old"}'));controller.close();resolve();};});}},{highWaterMark:0});
 const transport={assertCurrent:async()=>{if(!active) throw new Error('Console session changed');},
  fetch:async()=>{calls++;return new Response(source);}};
 const adapter=createStudioCookieSession({...lease(new Date(Date.now()+60000).toISOString()),transport});
 const response=await adapter.fetchRequest('api/v1/studio/drafts',{method:'POST',body:'{}'});
 assert.equal(response.body,response.body);
 const reading=new Response(response.body).json();
 while(!finish) await new Promise(resolve=>setImmediate(resolve));
 active=false;finish();
 await assert.rejects(reading,/session changed/);
 assert.equal(calls,1);
});

test('a tenant switch under the same principal is a different authority',()=>{
 const base={principalId:'partition',environmentId:'environment',sourcePathBase:''};
 assert.equal(sameCookieAuthority({...base,tenantId:'a'},{...base,tenantId:'a'}),true);
 assert.equal(sameCookieAuthority({...base,tenantId:'a'},{...base,tenantId:'b'}),false);
 assert.equal(sameCookieAuthority(base,{...base,tenantId:'a'}),false);
 assert.equal(sameCookieAuthority({...base,tenantId:'a'},{...base,environmentId:'other',tenantId:'a'}),false);
});
