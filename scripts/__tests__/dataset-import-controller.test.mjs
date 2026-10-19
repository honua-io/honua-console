import test from 'node:test';
import assert from 'node:assert/strict';
import {DatasetImportController} from '../../src/Honua.Console.Web/wwwroot/js/dataset-import-controller.mjs';
const hash = 'a'.repeat(64), deadline = '2099-01-01T00:00:00Z';
const task = {taskId:'task',revision:1,contentHash:hash,ownerActorId:'agent',tenantId:'tenant',workspaceId:'workspace',targetId:'target',
    destination:{schema:'public',table:'places'},source:{kind:'browser-file',fileName:'places.geojson',totalBytes:4,expectedSha256:hash},
    options:{loadMode:1,targetSrid:4326},uploadAdmissionDeadline:deadline};
const authority = {executorActorId:'agent',initiatingHumanActorId:'human',originatingTurnId:'turn',originalTurnHash:hash,authorityRevision:1,
    authorityFingerprint:hash,policyContextFingerprint:hash,uploadAdmissionDeadline:deadline};
const receipt = {taskId:'task',taskRevision:1,taskContentHash:hash,uploadId:'b'.repeat(32),version:1,state:'receiving',acceptedBytes:0,totalBytes:4,
    chunkBytes:4,chunkCount:0,expiresAt:deadline};
class Store {
    value = null;
    async load() { return structuredClone(this.value); }
    async save(key, expected, value) {
        assert.equal(this.value?.revision ?? 0, expected, 'atomic CAS expected revision');
        this.value = structuredClone({...value,revision:expected+1}); return structuredClone(this.value);
    }
}
function setup(overrides = {}) {
    const store = new Store(), calls = []; let remote = null, current = true;
    const uploads = {capabilities:async()=>({maximumFileBytes:1024,chunkBytes:4,importAcceptanceSupported:false}),
        read:async()=>{calls.push('GET');return remote;},reconcile:async()=>{calls.push('GET');return remote;},
        create:async()=>{calls.push('create');return remote=structuredClone(receipt);},
        appendChunk:async(ref,before,chunk)=>{calls.push('append');assert.equal(chunk.size,4);return remote={...before,version:2,acceptedBytes:4,chunkCount:1};},
        seal:async(ref,before)=>{calls.push('seal');return remote={...before,version:3,state:'sealed',contentSha256:hash};},
        retainSource:async(ref,before)=>{calls.push('source');return remote={...before,version:4,sourceRetention:{bytes:4,sha256:hash,expiresAt:deadline}};},...overrides.uploads};
    const controller = new DatasetImportController({binding:{environmentId:'environment',principalId:'human',workspaceId:'workspace',targetId:'target',
        task:{id:'task',revision:1,contentHash:hash}},tasks:{readTask:async()=>structuredClone(task),readAuthority:async()=>({...authority}),...overrides.tasks},
        uploads,hashFile:async()=>{calls.push('hash');return hash;},store,isCurrent:()=>current,uuid:()=>`request-${calls.length}`,...overrides.controller});
    return {controller,store,calls,uploads,setCurrent:value=>current=value,setRemote:value=>remote=value};
}
test('whole file is checked before transfer; retaining source never claims accepted job', async()=>{
    const {controller,store,calls}=setup(); await controller.initialize();
    await assert.rejects(controller.transfer(),/Check the original file/);
    await controller.verifyFile(new Blob(['data'])); await controller.transfer();
    assert.deepEqual(calls.filter(x=>x!=='GET'),['hash','create','append','seal','source']);
    assert.equal(controller.stage,'retained');assert.match(controller.message,/acceptance has not been confirmed/);
    assert.equal(store.value.pending,null);assert.equal(JSON.stringify(store.value).includes('csrf'),false);
    assert.equal(Object.values(store.value).some(x=>x instanceof Blob),false);
});
test('lost mutation reply persists original request and reload reconciles only through GET',async()=>{
    const state=setup(); await state.controller.initialize();await state.controller.verifyFile(new Blob(['data']));
    state.uploads.create=async()=>{state.calls.push('create');state.setRemote(structuredClone(receipt));throw new Error('lost reply');};
    await assert.rejects(state.controller.transfer(),/lost reply/);assert.equal(state.store.value.pending.action,'create');
    const before=state.calls.length;state.controller.file=null;await state.controller.recover();
    assert.deepEqual(state.calls.slice(before),['GET']);assert.equal(state.store.value.pending,null);
    await assert.rejects(state.controller.transfer(),/Check the original file/);assert.equal(state.calls.filter(x=>x==='create').length,1);
});
test('unknown create with no server receipt blocks another POST',async()=>{
    const state=setup({uploads:{create:async()=>{throw new Error('lost');}}});await state.controller.initialize();
    await state.controller.verifyFile(new Blob(['data']));await assert.rejects(state.controller.transfer(),/lost/);
    await state.controller.recover();assert.equal(state.controller.stage,'unknown');
    await assert.rejects(state.controller.transfer(),/Check the original receipt|unresolved/);assert.equal(state.store.value.pending.action,'create');
});
test('a previously known missing receipt preserves identity and blocks recreation',async()=>{
    const state=setup();await state.controller.initialize();state.setRemote(receipt);await state.controller.recover();
    state.setRemote(null);await state.controller.recover();assert.equal(state.store.value.receipt.uploadId,receipt.uploadId);
    assert.equal(state.controller.stage,'paused');await assert.rejects(state.controller.transfer(),/Check the original receipt/);
});
test('navigation during awaited task read discards response and does not persist recovery metadata',async()=>{
    let release;const state=setup({tasks:{readTask:()=>new Promise(resolve=>release=resolve)}});
    const promise=state.controller.initialize();state.setCurrent(false);release(task);await assert.rejects(promise,/paused/);assert.equal(state.store.value,null);
});
test('old response cannot clear a newer pending identity from another view',async()=>{
    const state=setup();await state.controller.initialize();
    state.store.value={...state.store.value,pending:{id:'new',action:'create'},revision:state.store.value.revision+1};
    await assert.rejects(state.controller.saveResponse('old',receipt),/Another view/);assert.equal(state.store.value.pending.id,'new');
});
test('wrong complete-file digest fails before any upload mutation',async()=>{
    const state=setup({controller:{hashFile:async()=>'c'.repeat(64)}});await state.controller.initialize();
    await assert.rejects(state.controller.verifyFile(new Blob(['data'])),/differs/);assert.equal(state.controller.file,null);assert.deepEqual(state.calls,['GET']);
});
test('sealed source may be retained after reload without re-reading file bytes',async()=>{
    const state=setup();state.setRemote({...receipt,version:3,acceptedBytes:4,state:'sealed',contentSha256:hash});
    await state.controller.initialize();await state.controller.transfer();assert.equal(state.controller.stage,'retained');assert.deepEqual(state.calls,['GET','GET','source']);
});
test('same authority revision cannot silently acquire a different fingerprint',async()=>{
    const state=setup();await state.controller.initialize();
    state.controller.tasks.readAuthority=async()=>({...authority,authorityFingerprint:'d'.repeat(64)});
    await assert.rejects(state.controller.recover(),/executor or policy binding changed/);
    assert.equal(state.store.value.authority.authorityFingerprint,hash);
});
test('prepared admission blocks new source mutation',async()=>{
    const state=setup({tasks:{readAuthority:async()=>({...authority,preparedOperationId:'operation'})}});
    await state.controller.initialize();await state.controller.verifyFile(new Blob(['data']));
    await assert.rejects(state.controller.transfer(),/awaiting import admission/);assert.deepEqual(state.calls,['GET','hash','GET']);
});
