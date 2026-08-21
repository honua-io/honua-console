#!/usr/bin/env node
import { mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { readConsoleBoundary, produceConsoleReceipts } from './console-receipt-producer.mjs';

const options = parse(process.argv.slice(2));

try {
  const endpoint = required(options.endpoint ?? process.env.HONUA_AI_ARC_ENDPOINT, 'endpoint');
  const checkpointPath = resolve(required(options.checkpoint ?? process.env.HONUA_AI_ARC_CHECKPOINT, 'checkpoint'));
  const preConsoleEvidencePath = resolve(required(
    options.preConsoleEvidence ?? process.env.HONUA_AI_ARC_REAL_MODEL_EVIDENCE ?? process.env.HONUA_AI_ARC_PRE_CONSOLE_RECEIPT,
    'pre-Console Studio handoff or SDK receipt',
  ));
  const outputPath = resolve(required(options.output ?? process.env.HONUA_AI_ARC_CONSOLE_RECEIPT, 'release receipt output'));
  const sdkOutputPath = resolve(options.sdkOutput ?? process.env.HONUA_AI_ARC_SDK_CONSOLE_RECEIPT ?? `${outputPath}.sdk.json`);
  const mode = options.mode ?? process.env.HONUA_CONSOLE_MODE ?? 'full';
  const credentialEnv = options.credentialEnv ?? 'HONUA_AI_ARC_CONSOLE_TOKEN';
  if (process.env.HONUA_ADMIN_KEY || process.env.HONUA_API_KEY) {
    throw new Error('broad admin-key variables are not accepted; supply a scoped read + admin:approve bearer');
  }
  const credential = required(process.env[credentialEnv], `credential environment ${credentialEnv}`);
  const checkpoint = JSON.parse(await readFile(checkpointPath, 'utf8'));
  const preConsoleEvidence = JSON.parse(await readFile(preConsoleEvidencePath, 'utf8'));
  const boundary = readConsoleBoundary(checkpoint, preConsoleEvidence);
  const receipts = await produceConsoleReceipts({ endpoint, credential, mode, boundary });
  await writeJsonAtomic(outputPath, receipts.releaseReceipt);
  await writeJsonAtomic(sdkOutputPath, receipts.sdkReceipt);
  process.stdout.write(`Console receipt passed (${mode}); release=${outputPath}; sdk=${sdkOutputPath}\n`);
} catch (error) {
  process.stderr.write(`Console receipt refused: ${error instanceof Error ? error.message : 'unknown failure'}\n`);
  process.exitCode = 1;
}

function parse(argv) {
  const result = {};
  const names = new Map([
    ['--endpoint', 'endpoint'], ['--checkpoint', 'checkpoint'],
    ['--pre-console-evidence', 'preConsoleEvidence'], ['--pre-console-receipt', 'preConsoleEvidence'],
    ['--output', 'output'], ['--sdk-output', 'sdkOutput'], ['--mode', 'mode'], ['--credential-env', 'credentialEnv'],
  ]);
  for (let index = 0; index < argv.length; index += 1) {
    const name = names.get(argv[index]);
    if (!name) throw new Error(`unsupported argument ${argv[index]}`);
    const value = argv[++index];
    if (!value) throw new Error(`${argv[index - 1]} requires a value`);
    result[name] = value;
  }
  return result;
}

async function writeJsonAtomic(path, value) {
  await mkdir(dirname(path), { recursive: true });
  const temporary = `${path}.${process.pid}.tmp`;
  await writeFile(temporary, `${JSON.stringify(value, null, 2)}\n`, { encoding: 'utf8', mode: 0o600 });
  await rename(temporary, path);
}

function required(value, label) {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`${label} is required`);
  return value.trim();
}
