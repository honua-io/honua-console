import test from 'node:test';
import assert from 'node:assert/strict';
import {DatasetImportSubmissionController} from '../../src/Honua.Console.Web/wwwroot/js/dataset-import-submission.mjs';
const hash = 'a'.repeat(64), deadline = '2099-01-01T00:00:00Z';
const binding = {environmentId:'environment',principalId:'human',workspaceId:'workspace',targetId:'target',task:{id:'task',revision:1,contentHash:hash}};
const review = {taskId:'task',taskRevision:1,taskContentHash:hash,authorityRevision:2,authorityFingerprint:hash,policyContextFingerprint:hash,
    uploadAdmissionDeadline:deadline,executorActorId:'actual-agent',initiatingHumanActorId:'human',originatingTurnId:'original-turn',originalTurnHash:hash};
const prepared = () => ({taskId:'task',taskRevision:1,taskContentHash:hash,state:'prepared',operationId:'b'.repeat(32),authorityRevision:2,
    authorityFingerprint:hash,policyContextFingerprint:hash,acceptBefore:deadline});
class Store {
    value = null;
    async load() { return structuredClone(this.value); }
    async save(key, expected, value) {
        assert.equal(this.value?.revision ?? 0, expected, 'actual metadata CAS revision');
        this.value = structuredClone({...value,revision:expected+1}); return structuredClone(this.value);
    }
}
function setup({store = new Store(), submissions = {}, isCurrent = () => true} = {}) {
    const calls = []; let remote = {taskId:'task',taskRevision:1,taskContentHash:hash,state:'not-prepared'};
    const transport = {read:async()=>{calls.push('GET');return structuredClone(remote);},
        reconcile:async()=>{calls.push('GET');return structuredClone(remote);},
        submit:async(ref, reviewed)=>{calls.push('POST');return remote=prepared();},...submissions};
    const controller = new DatasetImportSubmissionController({binding, submissions:transport, store,isCurrent,uuid:()=> 'original-pending'});
    return {controller,store,calls,setRemote:value=>remote=value,transport};
}
test('human preparation is explicit awaiting-agent, never accepted or automatically repeated', async()=>{
    const {controller,calls} = setup();
    const value = await controller.prepare(review);
    assert.equal(value.state,'prepared'); assert.equal(value.job,undefined);
    assert.match(controller.message(),/Awaiting the actual agent/);
    assert.equal(controller.dirty(),false);
    await controller.prepare(review);
    assert.deepEqual(calls,['GET','POST','GET']);
});
test('lost preparation reply persists original review before POST; reload recovers with GET only', async()=>{
    const store = new Store();
    const first = setup({store,submissions:{submit:async()=>{
        assert.equal(store.value.pending.reviewed.authorityRevision,2);
        throw new TypeError('lost response');
    }}});
    await assert.rejects(first.controller.prepare(review),/lost response/);
    assert.equal(first.controller.dirty(),true);
    const later = setup({store}); later.setRemote(prepared());
    await later.controller.recover();
    assert.deepEqual(later.calls,['GET']); assert.equal(store.value.pending,null);
    assert.equal(later.controller.record.receipt.operationId,prepared().operationId);
});
test('unknown or absent original outcome blocks another submission without changing identity', async()=>{
    const store = new Store();
    const first = setup({store,submissions:{submit:async()=>{throw new TypeError('lost response');}}});
    await assert.rejects(first.controller.prepare(review));
    const later = setup({store}); later.setRemote(null);
    await later.controller.recover();
    await assert.rejects(later.controller.prepare(review),/unresolved/);
    assert.deepEqual(later.calls,['GET','GET']);
    assert.equal(store.value.pending.id,'original-pending');
});
test('click review is pinned before asynchronous original-status read', async()=>{
    let release; const wait = new Promise(resolve=>release=resolve);
    const {controller} = setup({submissions:{read:async()=>{await wait;return null;},submit:async(ref, captured)=>{
        assert.equal(captured.authorityRevision,2);assert.equal(captured.taskId,'task');return prepared();
    }}});
    const mutable = structuredClone(review), pending = controller.prepare(mutable);
    mutable.authorityRevision = 99; mutable.taskId = 'other-task'; release();
    assert.equal((await pending).state,'prepared');
});
test('an old response cannot clear a newer pending identity from another tab', async()=>{
    const store = new Store(); let release, entered;
    const wait = new Promise(resolve=>release=resolve), started = new Promise(resolve=>entered=resolve);
    const {controller} = setup({store,submissions:{submit:async()=>{entered();await wait;return prepared();}}});
    const pending = controller.prepare(review); await started;
    const original = await store.load();
    await store.save('same-task',original.revision,{...original,pending:{...original.pending,id:'newer-pending'}});
    release(); await assert.rejects(pending,/Another view/);
    assert.equal(store.value.pending.id,'newer-pending');
});
test('historical accepted output can be read without recapturing task source or executor authority', async()=>{
    const {controller,calls,setRemote} = setup();
    setRemote({...prepared(),state:'accepted',job:{jobId:prepared().operationId,status:'succeeded',leaseId:'original-lease',
        output:{schema:'actual_schema',physicalTableName:'actual_table',featureCount:3}}});
    await controller.recover();
    assert.match(controller.message(),/Imported 3 features into actual_schema.actual_table/);
    assert.deepEqual(calls,['GET']); assert.equal(controller.dirty(),false);
});
test('navigation before durable intent enters SDK prevents submission', async()=>{
    let current = true;
    const {controller,calls} = setup({isCurrent:()=>current,submissions:{read:async()=>{current=false;return null;}}});
    await assert.rejects(controller.prepare(review),/Return to the original/);
    assert.equal(calls.includes('POST'),false);
});
