// Synthetic boundary controls for the runner join, never live AI evidence.
import test from 'node:test';
import assert from 'node:assert/strict';
import { submittedDigest, validateBinding, validateProjection } from '../../e2e/playwright/workspace-live/private-execution-witness.mjs';

const hash = character => 'sha256:' + character.repeat(64);
function fixture() {
    const binding = {schemaVersion:1,candidateKey:hash('a'),runtimeId:'runtime-original',registrationDigest:hash('b'),
        evidence:{region:'us-east-1',bucket:'original-private-evidence'},release:{root:'/checked/release',commit:'c'.repeat(40)}};
    const capture = {workspaceId:'workspace-original',turnId:'turn-original',executionId:'execution-original',
        requestDigest:submittedDigest(Buffer.from('{"turnId":"turn-original","prompt":"Use this map."}')),
        turn:{context:{targetId:'target-original'}}};
    const execution = {state:'completed',stateRevision:4,finalMessageId:'message-original',
        operations:[{kind:'managed-job',id:'job-original',status:'completed'}],artifacts:[]};
    const original={key:'original/http/read',sha256:hash('1')};
    const witness={schema:'honua.workspace-execution-witness/v1',qualification:false,...capture,
        requestDigest:hash('4'),
        candidateKey:binding.candidateKey,runtimeId:binding.runtimeId,registrationDigest:binding.registrationDigest,
        targetId:'target-original',humanTurn:{submissionRequestDigest:capture.requestDigest,contextDigest:hash('d'),originatingActorId:'human-original'},
        worker:{contextDigest:hash('d'),taskPrincipal:{actorId:'task-original',credentialVersion:'issued-version-original'}},
        modelRounds:[{round:0,providerRequestId:'provider-original',requestDigest:hash('e'),responseDigest:hash('f'),
            pricingDigest:hash('2'),requestedModelId:'reviewed-model-profile',returnedModelId:'actual-model-id',
            usage:{inputTokens:1200,outputTokens:200},costUsd:'0.00726'}],native:structuredClone(execution),
        proof:{nativeOriginal:original,workerBindingOriginal:original,finalMessageOriginal:original,effectOriginals:[original]}};
    const result={reference:{bucket:binding.evidence.bucket,key:'agent-e2e/'+binding.candidateKey.slice(7)
        +'/execution-witnesses/'+binding.runtimeId+'/'+capture.executionId+'/42-'+'3'.repeat(64)+'.json',
        versionId:'original-immutable-version',sha256:hash('3')},witness};
    return {binding,capture,execution,result};
}
const verify = value => validateProjection(value.result,value.capture,value.execution,value.binding);

test('completed execution joins original identities and effects independently of object key order',()=>{
    const value=fixture();
    value.result.witness.native.operations=[{status:'completed',id:'job-original',kind:'managed-job'}];
    assert.equal(verify(value),value.result);
});

test('submission identity binds original UTF-8 bytes instead of parsed JSON equivalence',()=>{
    const original=Buffer.from('{"prompt":"Mālama","revision":1.0}');
    const normalized=Buffer.from(JSON.stringify(JSON.parse(original.toString('utf8'))));
    assert.notEqual(submittedDigest(original),submittedDigest(normalized));
    const value=fixture();value.result.witness.humanTurn.submissionRequestDigest=submittedDigest(normalized);
    assert.throws(()=>verify(value),/Task-authenticated context/);
});

test('foreign runtime, candidate, registration, target or human turn cannot borrow a valid worker result',()=>{
    for (const field of ['candidateKey','runtimeId','registrationDigest','targetId','workspaceId','turnId','executionId']) {
        const value=fixture();value.result.witness[field]='different-original';
        assert.throws(()=>verify(value),/identity differs/,field);
    }
});

test('human context alone cannot substitute for actual task-authenticated context and identity',()=>{
    for (const change of [
        value=>delete value.result.witness.worker,
        value=>value.result.witness.worker.contextDigest=hash('9'),
        value=>value.result.witness.worker.taskPrincipal.actorId='human-original',
        value=>delete value.result.witness.worker.taskPrincipal.credentialVersion,
    ]) {
        const value=fixture();change(value);assert.throws(()=>verify(value),/Task-authenticated context/);
    }
});

test('stale native revision, another final message or missing canonical effects are refused',()=>{
    for (const change of [
        value=>value.result.witness.native.stateRevision--,
        value=>value.result.witness.native.finalMessageId='another-message',
        value=>value.result.witness.native.operations=[],
        value=>value.result.witness.native.artifacts=[{kind:'studio-composition',itemId:'another-item'}],
    ]) {
        const value=fixture();change(value);assert.throws(()=>verify(value),/current native completion/);
    }
});

test('pending or interrupted execution cannot pass through completed assistant prose',()=>{
    for (const state of ['queued','running','awaiting-input','awaiting-approval','interrupted','failed']) {
        const value=fixture();value.execution.state=value.result.witness.native.state=state;
        assert.throws(()=>verify(value),/actual provider response/);
    }
});

test('mutable or foreign witness references are rejected',()=>{
    for (const change of [
        value=>value.result.reference.bucket='another-private-bucket',
        value=>value.result.reference.versionId='null',
        value=>value.result.reference.key=value.result.reference.key.replace('execution-original','other-execution'),
        value=>value.result.reference.sha256=hash('9'),
    ]) {
        const value=fixture();change(value);assert.throws(()=>verify(value),/private evidence reference|private witness names/);
    }
});

test('actual provider response, usage and positive settled cost cannot be omitted or invented as zero',()=>{
    for (const change of [
        value=>value.result.witness.modelRounds=[],
        value=>delete value.result.witness.modelRounds[0].providerRequestId,
        value=>delete value.result.witness.modelRounds[0].responseDigest,
        value=>value.result.witness.modelRounds[0].usage.inputTokens=0,
        value=>value.result.witness.modelRounds[0].costUsd='0',
        value=>value.result.witness.modelRounds[0].costUsd='9'.repeat(400),
    ]) {
        const value=fixture();change(value);assert.throws(()=>verify(value),/actual provider response/);
    }
});

test('original worker, final-message and effect receipts are all required',()=>{
    for (const field of ['nativeOriginal','workerBindingOriginal','finalMessageOriginal','effectOriginals']) {
        const value=fixture();delete value.result.witness.proof[field];
        assert.throws(()=>verify(value),/Original worker, effect or final-message proof/);
    }
});

test('private projection cannot label itself full qualification',()=>{
    const value=fixture();value.result.witness.qualification=true;
    assert.throws(()=>verify(value),/identity differs/);
});

test('runtime binding rejects missing original identities, floating source and extra caller inputs',()=>{
    for (const change of [
        binding=>delete binding.registrationDigest,
        binding=>binding.release.commit='trunk',
        binding=>binding.release.root='relative/source',
        binding=>binding.projector='arbitrary-command',
        binding=>binding.evidence.url='https://caller.example/witness.json',
    ]) {
        const value=fixture();change(value.binding);assert.throws(()=>validateBinding(value.binding));
    }
});
