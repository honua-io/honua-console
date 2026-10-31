import test from 'node:test';
import assert from 'node:assert/strict';
import {capturedProcessFetch, createStudioCookieSession, scheduleCookieSessionRenewal} from '../../src/Honua.Console.Web/wwwroot/js/cookie-workspace-transport.mjs';

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
 assert.deepEqual(Object.keys(adapter).sort(),['baseUrl','csrfToken','environmentId','expiresAt','kind','onExpired','principalId','sourcePathBase'].sort());
 assert.equal(adapter.kind,'cookie');assert.equal(adapter.baseUrl,'/api/studio');assert.ok(Object.isFrozen(adapter));
 session.csrfToken='changed';session.expiresAt='invalid';
 assert.equal(adapter.csrfToken,'csrf');assert.equal(adapter.expiresAt,clock.expiry(5000));
 for(const expiresAt of [undefined,null,0,'','invalid','2026-99-99T00:00:00Z'])
  assert.throws(()=>createStudioCookieSession(lease(expiresAt)),/lease expiry is invalid/);
 assert.throws(()=>adapter.onExpired(undefined),/expiry listener/);
 assert.equal(clock.timers.size,0);
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
