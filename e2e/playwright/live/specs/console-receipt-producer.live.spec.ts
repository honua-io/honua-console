import { createHash } from 'node:crypto';
import { resolve } from 'node:path';
import { readFile } from 'node:fs/promises';
import { test, expect } from '@playwright/test';
import { buildConsoleEvidence, produceConsoleReceiptInBrowser, validateConsoleReceiptInputs } from '../console-receipt-browser.mjs';
import { buildReceiptAliasBytes, clearReceiptOutputs, writeReceiptSetAtomic } from '../console-receipt-output.mjs';
import { exerciseConsoleReadApproveKeyRecipe, readConsoleBoundary } from '../console-receipt-producer.mjs';

test('drives the published Console UI and writes one strict aggregate plus its evidence sidecar', async ({ page }) => {
  const configured = [
    process.env.HONUA_AI_ARC_ENDPOINT,
    process.env.HONUA_AI_ARC_CONSOLE_ORIGIN,
    process.env.HONUA_AI_ARC_CHECKPOINT,
    process.env.HONUA_AI_ARC_CONSOLE_RECEIPT_SCHEMA,
    process.env.HONUA_AI_ARC_REAL_MODEL_HANDOFF,
    process.env.HONUA_AI_ARC_CONSOLE_RECEIPT,
    process.env.HONUA_AI_ARC_SDK_CONSOLE_RECEIPT,
    process.env.HONUA_AI_ARC_CONSOLE_EVIDENCE,
    process.env.HONUA_AI_ARC_CONSOLE_TOKEN,
    process.env.HONUA_AI_ARC_CONSOLE_READ_APPROVE_KEY,
  ].some(Boolean);
  test.skip(!configured, 'Set the HONUA_AI_ARC_* boundary inputs to run the candidate-backed Console producer.');
  test.setTimeout(240_000);

  expect(process.env.HONUA_ADMIN_KEY, 'broad HONUA_ADMIN_KEY must not enter the Console producer').toBeFalsy();
  expect(process.env.HONUA_API_KEY, 'broad HONUA_API_KEY must not enter the Console producer').toBeFalsy();
  const endpoint = required(process.env.HONUA_AI_ARC_ENDPOINT, 'HONUA_AI_ARC_ENDPOINT');
  const consoleOrigin = required(process.env.HONUA_AI_ARC_CONSOLE_ORIGIN, 'HONUA_AI_ARC_CONSOLE_ORIGIN');
  const checkpointPath = resolve(required(process.env.HONUA_AI_ARC_CHECKPOINT, 'HONUA_AI_ARC_CHECKPOINT'));
  const receiptSchemaPath = resolve(required(
    process.env.HONUA_AI_ARC_CONSOLE_RECEIPT_SCHEMA,
    'HONUA_AI_ARC_CONSOLE_RECEIPT_SCHEMA',
  ));
  const handoffPath = resolve(required(process.env.HONUA_AI_ARC_REAL_MODEL_HANDOFF, 'HONUA_AI_ARC_REAL_MODEL_HANDOFF'));
  const outputPath = resolve(required(process.env.HONUA_AI_ARC_CONSOLE_RECEIPT, 'HONUA_AI_ARC_CONSOLE_RECEIPT'));
  const sdkOutputPath = resolve(required(process.env.HONUA_AI_ARC_SDK_CONSOLE_RECEIPT, 'HONUA_AI_ARC_SDK_CONSOLE_RECEIPT'));
  const evidenceOutputPath = resolve(required(process.env.HONUA_AI_ARC_CONSOLE_EVIDENCE, 'HONUA_AI_ARC_CONSOLE_EVIDENCE'));
  const credential = required(process.env.HONUA_AI_ARC_CONSOLE_TOKEN, 'HONUA_AI_ARC_CONSOLE_TOKEN');
  const outputPaths = [outputPath, sdkOutputPath, evidenceOutputPath];
  const inputPaths = [checkpointPath, receiptSchemaPath, handoffPath];
  if (new Set(outputPaths).size !== outputPaths.length || outputPaths.some((path) => inputPaths.includes(path))) {
    throw new Error('aggregate, SDK alias, and Console evidence outputs must be distinct and must not overwrite inputs');
  }
  await clearReceiptOutputs(outputPaths);

  const origin = new URL(consoleOrigin).origin;
  await page.route(`${origin}/**`, async (route) => route.continue({ headers: {
    ...route.request().headers(),
    'x-forwarded-user': 'honua-release-console',
    'x-forwarded-email': 'honua-release-console@honua.invalid',
    'x-forwarded-access-token': credential,
    ...(process.env.HONUA_CONSOLE_EDGE_AUTH
      ? { 'x-honua-edge-auth': process.env.HONUA_CONSOLE_EDGE_AUTH }
      : {}),
  } }));

  const checkpoint = JSON.parse(await readFile(checkpointPath, 'utf8')) as unknown;
  const receiptSchema = JSON.parse(await readFile(receiptSchemaPath, 'utf8')) as unknown;
  const handoff = JSON.parse(await readFile(handoffPath, 'utf8')) as unknown;
  const boundary = readConsoleBoundary(checkpoint, handoff);
  validateConsoleReceiptInputs({ boundary, receiptSchema });
  const mode = process.env.HONUA_CONSOLE_MODE ?? (process.env.HONUA_ZERO_TO_MAP_RECEIPT ? 'witness' : 'full');
  const readApproveKey = process.env.HONUA_AI_ARC_CONSOLE_READ_APPROVE_KEY?.trim();
  const readApproveKeyId = readApproveKey
    ? required(process.env.HONUA_AI_ARC_CONSOLE_READ_APPROVE_KEY_ID, 'HONUA_AI_ARC_CONSOLE_READ_APPROVE_KEY_ID')
    : undefined;
  expect(!readApproveKey || mode === 'witness', 'the focused API-key recipe requires witness mode').toBe(true);
  const keyRecipe = readApproveKey
    ? await exerciseConsoleReadApproveKeyRecipe({ endpoint, apiKey: readApproveKey, apiKeyId: readApproveKeyId, boundary })
    : undefined;
  const produced = await produceConsoleReceiptInBrowser({
    page,
    consoleOrigin,
    serverEndpoint: endpoint,
    mode,
    boundary,
    receiptSchema,
  });
  const { aggregateBytes, sdkBytes } = buildReceiptAliasBytes(produced.aggregate, receiptSchema);
  const evidence = buildConsoleEvidence({
    boundary,
    aggregateSha256: createHash('sha256').update(aggregateBytes).digest('hex'),
    observations: { ...produced.observations, keyRecipe },
  });
  await writeReceiptSetAtomic([
    { path: outputPath, bytes: aggregateBytes },
    { path: sdkOutputPath, bytes: sdkBytes },
    { path: evidenceOutputPath, bytes: `${JSON.stringify(evidence, null, 2)}\n` },
  ]);

  expect(await readFile(outputPath, 'utf8')).toBe(await readFile(sdkOutputPath, 'utf8'));
  expect(produced.aggregate.status).toBe('passed');
  expect(evidence.integrity.algorithm).toBe('sha256');
});

function required(value: string | undefined, name: string): string {
  const trimmed = value?.trim();
  if (!trimmed) throw new Error(`${name} is required for the candidate-backed Console gate`);
  return trimmed;
}
