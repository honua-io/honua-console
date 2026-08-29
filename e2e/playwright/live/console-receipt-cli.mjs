#!/usr/bin/env node
import { createHash } from 'node:crypto';
import { mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { chromium } from '@playwright/test';
import { buildConsoleEvidence, produceConsoleReceiptInBrowser } from './console-receipt-browser.mjs';
import { buildReceiptAliasBytes } from './console-receipt-output.mjs';
import { exerciseConsoleReadApproveKeyRecipe, readConsoleBoundary } from './console-receipt-producer.mjs';

const options = parse(process.argv.slice(2));

try {
  if (process.env.HONUA_ADMIN_KEY || process.env.HONUA_API_KEY) {
    throw new Error('broad admin-key variables are not accepted; supply a scoped read + admin:approve bearer');
  }
  const endpoint = required(options.endpoint ?? process.env.HONUA_AI_ARC_ENDPOINT, 'endpoint');
  const consoleOrigin = required(options.consoleOrigin ?? process.env.HONUA_AI_ARC_CONSOLE_ORIGIN, 'Console origin');
  const checkpointPath = resolve(required(options.checkpoint ?? process.env.HONUA_AI_ARC_CHECKPOINT, 'checkpoint'));
  const receiptSchemaPath = resolve(required(
    options.receiptSchema ?? process.env.HONUA_AI_ARC_CONSOLE_RECEIPT_SCHEMA,
    'pinned SDK Console receipt schema',
  ));
  const realModelHandoffPath = resolve(required(
    options.realModelHandoff ?? process.env.HONUA_AI_ARC_REAL_MODEL_HANDOFF,
    'immutable paused Studio handoff',
  ));
  const outputPath = resolve(required(options.output ?? process.env.HONUA_AI_ARC_CONSOLE_RECEIPT, 'release receipt output'));
  const sdkOutputPath = resolve(options.sdkOutput ?? process.env.HONUA_AI_ARC_SDK_CONSOLE_RECEIPT ?? `${outputPath}.sdk.json`);
  const evidenceOutputPath = resolve(required(
    options.evidenceOutput ?? process.env.HONUA_AI_ARC_CONSOLE_EVIDENCE,
    'Console evidence output',
  ));
  if (outputPath === sdkOutputPath || outputPath === evidenceOutputPath || sdkOutputPath === evidenceOutputPath) {
    throw new Error('aggregate, SDK alias, and Console evidence outputs must be distinct files');
  }
  const mode = options.mode ?? process.env.HONUA_CONSOLE_MODE ?? 'full';
  const credential = required(process.env.HONUA_AI_ARC_CONSOLE_TOKEN, 'HONUA_AI_ARC_CONSOLE_TOKEN');
  const checkpoint = JSON.parse(await readFile(checkpointPath, 'utf8'));
  const realModelHandoff = JSON.parse(await readFile(realModelHandoffPath, 'utf8'));
  const receiptSchema = JSON.parse(await readFile(receiptSchemaPath, 'utf8'));
  const boundary = readConsoleBoundary(checkpoint, realModelHandoff);
  const readApproveKey = process.env.HONUA_AI_ARC_CONSOLE_READ_APPROVE_KEY?.trim();
  if (readApproveKey && mode !== 'witness') {
    throw new Error('the admin:read + admin:approve key recipe resolves proposals before the browser; use witness mode');
  }
  const keyRecipe = readApproveKey
    ? await exerciseConsoleReadApproveKeyRecipe({ endpoint, apiKey: readApproveKey, boundary })
    : undefined;
  const browser = await chromium.launch({ headless: true });
  try {
    const context = await browser.newContext();
    const origin = new URL(consoleOrigin).origin;
    await context.route(`${origin}/**`, async (route) => {
      await route.continue({
        headers: {
          ...route.request().headers(),
          'x-forwarded-user': 'honua-release-console',
          'x-forwarded-email': 'honua-release-console@honua.invalid',
          'x-forwarded-access-token': credential,
        },
      });
    });
    const page = await context.newPage();
    const produced = await produceConsoleReceiptInBrowser({
      page,
      consoleOrigin,
      serverEndpoint: endpoint,
      mode,
      boundary,
      receiptSchema,
    });
    const { aggregateBytes, sdkBytes } = buildReceiptAliasBytes(produced.aggregate, receiptSchema);
    const aggregateSha256 = createHash('sha256').update(aggregateBytes).digest('hex');
    const evidence = buildConsoleEvidence({
      boundary,
      aggregateSha256,
      observations: { ...produced.observations, keyRecipe },
    });
    const evidenceBytes = `${JSON.stringify(evidence, null, 2)}\n`;
    await writeBytesAtomic(outputPath, aggregateBytes);
    await writeBytesAtomic(sdkOutputPath, sdkBytes);
    await writeBytesAtomic(evidenceOutputPath, evidenceBytes);
    await context.close();
  } finally {
    await browser.close();
  }
  process.stdout.write(
    `Console receipt passed (${mode}); aggregate=${outputPath}; sdk-alias=${sdkOutputPath}; evidence=${evidenceOutputPath}\n`,
  );
} catch (error) {
  process.stderr.write(`Console receipt refused: ${error instanceof Error ? error.message : 'unknown failure'}\n`);
  process.exitCode = 1;
}

function parse(argv) {
  const result = {};
  const names = new Map([
    ['--endpoint', 'endpoint'], ['--console-origin', 'consoleOrigin'], ['--checkpoint', 'checkpoint'],
    ['--receipt-schema', 'receiptSchema'],
    ['--real-model-handoff', 'realModelHandoff'],
    ['--output', 'output'], ['--sdk-output', 'sdkOutput'], ['--evidence-output', 'evidenceOutput'],
    ['--mode', 'mode'],
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

async function writeBytesAtomic(path, bytes) {
  await mkdir(dirname(path), { recursive: true });
  const temporary = `${path}.${process.pid}.tmp`;
  await writeFile(temporary, bytes, { encoding: 'utf8', mode: 0o600 });
  await rename(temporary, path);
}

function required(value, label) {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`${label} is required`);
  return value.trim();
}
