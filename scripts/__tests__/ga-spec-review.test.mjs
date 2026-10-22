import test from 'node:test';
import assert from 'node:assert/strict';
import {GaSpecReview, reviewSummary} from '../../src/Honua.Console.Web/wwwroot/js/ga-spec-review.mjs';
function fixture(api) {
    const data = new Map();
    return new GaSpecReview({workspaceId:'w',session:{gaSpecWorkflow:true},authorityKey:()=> 'human',uuid:()=> 'command',
        storage:{getItem:k=>data.get(k),setItem:(k,v)=>data.set(k,v)},api});
}
const plan = {planId:'p',revision:'hash',verdict:'create',execution:'planned'};
test('Apply ignores acceptance success and reads durable evidence; reconnect reads again', async()=>{
    const calls=[];
    const review=fixture(async(path,method,body)=>{calls.push({path,method,body});return path.includes('/apply') ? {runId:'run',execution:'succeeded',evidence:'verified'} : path.includes('/runs/') ? {runId:'run',execution:'interrupted',evidence:'unverified'} : plan;});
    await review.open('p','hash');
    assert.equal((await review.apply()).execution,'interrupted');
    await review.reconnect();
    assert.equal(calls.filter(c=>c.path==='ga/runs/run').length,2);
    await assert.rejects(review.apply(),/already submitted/);
});
test('lost response retains command and reopening never enables blind replay', async()=>{
    const review=fixture(async path=>{if(path.includes('/apply')) throw new Error('lost response');if(path.includes('/runs/')) return {runId:'p',execution:'succeeded',evidence:'verified'};return plan;});
    await review.open('p','hash');await assert.rejects(review.apply(),/lost/);
    await review.open('p','hash');await assert.rejects(review.apply(),/already submitted/);
    assert.equal((await review.reconnect()).execution,'succeeded');
    await assert.rejects(review.apply(),/already submitted/);
});
test('disabled workflow cannot access server and stale handoff fails closed', async()=>{
    const review=fixture(async()=>({...plan,revision:'new'}));
    await assert.rejects(review.open('p','hash'),/reference changed/);
    review.workspace.session.gaSpecWorkflow=false;
    await assert.rejects(review.open('p','hash'),/disabled/);
});
test('navigation cannot adopt a late plan under another authority', async()=>{
    let release;
    const review=fixture(()=>new Promise(resolve=>{release=resolve;}));
    const pending=review.open('p','hash');
    review.workspace.authorityKey=()=> 'other-human';
    release(plan);
    await assert.rejects(pending,/authority changed/);
    assert.equal(review.plan,null);
});
test('Console plan consumes server bundle and canonical saved draft with exact context', async()=>{
    let intent;
    const review=fixture(async(path,method,body)=>{
        if(path==='ga/bundle') return {bundleVersion:'server-version',grammar:{},pathways:[]};
        if(path.includes('/drafts/')) return {revision:3,fields:{connectionId:'connection',schema:'public',table:'roads',layerName:'Roads',serviceName:'Transport'}};
        if(path==='ga/plans') {intent=body;return plan;}
        return plan;
    });
    Object.assign(review.workspace,{draft:{id:'draft',revision:3},dirty:new Map(),contextReference:async()=>({workspaceId:'w',viewId:'view',targetId:'target',contextRevision:8})});
    review.workspace.session.environmentId='environment';
    await review.planCurrentDraft();
    assert.deepEqual(intent,{bundleVersion:'server-version',workspaceId:'w',viewId:'view',targetId:'target',environment:'environment',contextRevision:'8',connectionId:'connection',schema:'public',table:'roads',layerName:'Roads',serviceName:'Transport'});
});
for (const execution of ['succeeded','failed','cancelled']) test(`new plan follows durable ${execution} outcome and preserves original command history`, async()=>{
    let active='first';
    const review=fixture(async path=>{
        if(path.includes('/apply')) return {runId:active};
        if(path.includes('/runs/')) return {runId:active,execution,evidence:'unverified'};
        return {...plan,planId:active};
    });
    await review.open('first','hash');await review.apply();
    active='second';
    // The status reader must return the original run, even after next-plan navigation.
    const api=review.workspace.api;
    review.workspace.api=(path,...args)=>path==='ga/runs/first' ? Promise.resolve({runId:'first',execution,evidence:'unverified'}) : api(path,...args);
    await review.open('second','hash');
    assert.equal(review.plan.planId,'second');
    assert.equal(JSON.parse(review.workspace.storage.getItem(`${review.key}.history.command`)).planId,'first');
    assert.equal((await review.apply()).runId,'second');
});
for (const execution of ['queued','running','interrupted','unknown']) test(`new plan remains blocked while prior execution is ${execution}`, async()=>{
    const review=fixture(async path=>path.includes('/apply') ? {runId:'p'} : path.includes('/runs/') ? {runId:'p',execution} : {...plan,planId:path.includes('/second?')?'second':'p'});
    await review.open('p','hash');await review.apply();
    await assert.rejects(review.open('second','hash'),/Resolve the original Apply/);
    assert.equal(review.plan.planId,'p');
});
test('an Apply response claiming terminal success cannot release recovery when durable read is missing', async()=>{
    const review=fixture(async path=>{
        if(path.includes('/apply')) return {runId:'p',execution:'succeeded'};
        if(path.includes('/runs/')) throw new Error('status unavailable');
        return {...plan,planId:path.includes('/second?')?'second':'p'};
    });
    await review.open('p','hash');await assert.rejects(review.apply(),/status unavailable/);
    await assert.rejects(review.open('second','hash'),/status unavailable/);
    assert.equal(review.plan.planId,'p');
});

test('review summary identifies its pinned environment and target independently of next-request context', ()=>{
    const summary=reviewSummary({...plan,spec:{environment:'production',workspaceId:'original-workspace',viewId:'original-tab',targetId:'original-target',contextRevision:'7'}},null);
    for (const value of ['Pinned layer publication','environment: production','workspaceId: original-workspace','viewId: original-tab','targetId: original-target','Context revision: 7'])
        assert.ok(summary.includes(value),value);
});
