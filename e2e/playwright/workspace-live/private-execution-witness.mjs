// Read-only runner adapter. Native product responses and the private projector
// remain independent; browser context reads never become worker/model evidence.
import { execFile } from 'node:child_process';
import { createHash } from 'node:crypto';
import { readFileSync, lstatSync } from 'node:fs';
import { isAbsolute, resolve } from 'node:path';
import { isDeepStrictEqual, promisify } from 'node:util';

const run = promisify(execFile);
const id = value => typeof value === 'string' && /^[A-Za-z0-9_-]{1,128}$/.test(value);
const digest = value => typeof value === 'string' && /^sha256:[a-f0-9]{64}$/.test(value);
const require = (condition, reason) => { if (!condition) throw new Error(reason); };
const exact = (value, names) => value && typeof value === 'object' && !Array.isArray(value)
    && Object.keys(value).sort().join('|') === [...names].sort().join('|');
const sourceEnvironment = () => Object.fromEntries(Object.entries(process.env)
    .filter(([name]) => !name.startsWith('GIT_') && !['PYTHONPATH','PYTHONHOME'].includes(name)));

export function validateBinding(binding) {
    require(exact(binding, ['schemaVersion', 'candidateKey', 'runtimeId', 'registrationDigest', 'evidence', 'release'])
        && binding.schemaVersion === 1 && digest(binding.candidateKey) && id(binding.runtimeId)
        && digest(binding.registrationDigest), 'Installed execution requires its verified runtime binding.');
    require(exact(binding.evidence, ['region', 'bucket'])
        && /^[a-z]{2}-[a-z]+-[0-9]$/.test(binding.evidence.region)
        && /^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$/.test(binding.evidence.bucket),
        'Installed execution requires original private evidence custody.');
    require(exact(binding.release, ['root', 'commit']) && typeof binding.release.root === 'string'
        && isAbsolute(binding.release.root) && /^[a-f0-9]{40}$/.test(binding.release.commit),
        'The private projector requires an exact checked release source.');
    return binding;
}

export async function checkedBinding(path = process.env.HONUA_WORKSPACE_E2E_RUNTIME_BINDING) {
    require(typeof path === 'string' && path.length > 0, 'HONUA_WORKSPACE_E2E_RUNTIME_BINDING is required before a paid turn.');
    const file = lstatSync(path);
    require(file.isFile() && !file.isSymbolicLink() && file.size <= 32768, 'Runtime binding must be a bounded regular file.');
    const binding = validateBinding(JSON.parse(readFileSync(path, 'utf8')));
    const options = {cwd: binding.release.root, env:sourceEnvironment(), encoding: 'utf8', timeout: 15000, maxBuffer: 65536};
    const head = await run('git', ['rev-parse', 'HEAD'], options);
    require(head.stdout.trim() === binding.release.commit, 'Private projector source differs from the installed evaluation binding.');
    const status = await run('git', ['status', '--porcelain', '--untracked-files=all', '--',
        'tools', 'certification/agent-e2e', 'certification/hosted-pilot'], options);
    require(!status.stdout.trim(), 'Private projector source or original declarations have unreviewed changes.');
    const script = resolve(binding.release.root, 'tools/agent_e2e_execution_witness.py');
    require(lstatSync(script).isFile() && !lstatSync(script).isSymbolicLink(), 'The checked private projector is unavailable.');
    return binding;
}

export function submittedDigest(bytes) {
    require(Buffer.isBuffer(bytes) && bytes.length > 0 && bytes.length <= 1024 * 1024, 'Original submitted bytes are missing or oversized.');
    return 'sha256:' + createHash('sha256').update(bytes).digest('hex');
}

export function validateProjection(result, capture, execution, binding) {
    validateBinding(binding);
    require(exact(result, ['reference', 'witness']), 'The private projector did not return verified original evidence.');
    const {reference, witness} = result;
    require(exact(reference, ['bucket', 'key', 'versionId', 'sha256']) && reference.bucket === binding.evidence.bucket
        && typeof reference.versionId === 'string' && reference.versionId && reference.versionId !== 'null'
        && digest(reference.sha256), 'The private evidence reference is not immutable.');
    const prefix = 'agent-e2e/' + binding.candidateKey.slice(7) + '/execution-witnesses/' + binding.runtimeId + '/' + capture.executionId + '/';
    require(typeof reference.key === 'string' && reference.key.startsWith(prefix)
        && /^[1-9][0-9]*-[a-f0-9]{64}\.json$/.test(reference.key.slice(prefix.length))
        && reference.key.endsWith('-' + reference.sha256.slice(7) + '.json'), 'The private witness names a different execution.');
    require(witness?.schema === 'honua.workspace-execution-witness/v1' && witness.qualification === false
        && witness.candidateKey === binding.candidateKey && witness.runtimeId === binding.runtimeId
        && witness.registrationDigest === binding.registrationDigest && witness.executionId === capture.executionId
        && witness.workspaceId === capture.workspaceId && witness.turnId === capture.turnId
        && witness.targetId === capture.turn.context.targetId, 'The private witness identity differs from the original human turn.');
    require(witness.humanTurn?.submissionRequestDigest === capture.requestDigest
        && digest(witness.humanTurn?.contextDigest) && witness.worker?.contextDigest === witness.humanTurn.contextDigest
        && typeof witness.humanTurn.originatingActorId === 'string' && witness.humanTurn.originatingActorId
        && typeof witness.worker.taskPrincipal?.actorId === 'string' && witness.worker.taskPrincipal.actorId
        && typeof witness.worker.taskPrincipal?.credentialVersion === 'string' && witness.worker.taskPrincipal.credentialVersion
        && witness.worker.taskPrincipal.actorId !== witness.humanTurn.originatingActorId,
        'Task-authenticated context or actor does not match independent original evidence.');
    require(witness.native?.state === execution.state && witness.native.stateRevision === execution.stateRevision
        && witness.native.finalMessageId === execution.finalMessageId
        && isDeepStrictEqual(witness.native.operations, execution.operations)
        && isDeepStrictEqual(witness.native.artifacts, execution.artifacts),
        'The private witness does not match current native completion and effects.');
    require(execution.state === 'completed' && id(execution.finalMessageId)
        && Array.isArray(witness.modelRounds) && witness.modelRounds.length > 0
        && witness.modelRounds.every(round => typeof round.providerRequestId === 'string' && round.providerRequestId
            && digest(round.requestDigest) && digest(round.responseDigest) && digest(round.pricingDigest)
            && typeof round.requestedModelId === 'string' && round.requestedModelId
            && typeof round.returnedModelId === 'string' && round.returnedModelId
            && Number.isSafeInteger(round.usage?.inputTokens) && round.usage.inputTokens > 0
            && Number.isSafeInteger(round.usage?.outputTokens) && round.usage.outputTokens > 0
            && typeof round.costUsd === 'string' && /^(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$/.test(round.costUsd)
            && Number.isFinite(Number(round.costUsd)) && Number(round.costUsd) > 0),
        'Completed native work lacks actual provider response and settled charge evidence.');
    require(witness.proof?.nativeOriginal && witness.proof.workerBindingOriginal && witness.proof.finalMessageOriginal
        && Array.isArray(witness.proof.effectOriginals) && witness.proof.effectOriginals.length > 0,
        'Original worker, effect or final-message proof is missing.');
    return result;
}

export async function projectWitness(binding, capture, responseText) {
    // Recheck source at consumption too; an earlier preflight is not permission
    // to replace the verifier between submitting and reading the original turn.
    require(isDeepStrictEqual(await checkedBinding(), binding), 'The original runtime binding changed during execution.');
    const request = {schemaVersion: 1, evidence: binding.evidence, candidateKey: binding.candidateKey,
        runtimeId: binding.runtimeId, executionId: capture.executionId, workspaceId: capture.workspaceId,
        turnId: capture.turnId, registrationDigest: binding.registrationDigest, humanTurnResponseText: responseText};
    const input = JSON.stringify(request);
    require(Buffer.byteLength(input) <= 2 * 1024 * 1024, 'Original turn exceeds the private projection input bound.');
    const environment = {...sourceEnvironment(), PYTHONNOUSERSITE: '1', PYTHONDONTWRITEBYTECODE: '1'};
    return new Promise((resolveResult, reject) => {
        const child = execFile('python3', [resolve(binding.release.root, 'tools/agent_e2e_execution_witness.py')],
            {cwd: binding.release.root, env: environment, encoding: 'utf8', timeout: 45000, maxBuffer: 1024 * 1024},
            (error, stdout) => {
                if (error) { reject(new Error('Independent private worker evidence is unavailable; the original turn was not replayed.')); return; }
                try { resolveResult(JSON.parse(stdout)); }
                catch { reject(new Error('Private worker evidence was not a valid verified projection.')); }
            });
        child.stdin.on('error', () => {});
        child.stdin.end(input);
    });
}
