import { dirname, resolve } from 'node:path';
import { mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import { test, expect } from '@playwright/test';
import {
  produceConsoleReceipts,
  readConsoleBoundary,
  playwrightRequestTransport,
} from '../console-receipt-producer.mjs';

test('produces candidate-bound release and SDK Console receipts at the pause boundary', async ({ request }) => {
  const configured = [
    process.env.HONUA_AI_ARC_ENDPOINT,
    process.env.HONUA_AI_ARC_CHECKPOINT,
    process.env.HONUA_AI_ARC_REAL_MODEL_EVIDENCE,
    process.env.HONUA_AI_ARC_PRE_CONSOLE_RECEIPT,
    process.env.HONUA_AI_ARC_CONSOLE_RECEIPT,
    process.env.HONUA_AI_ARC_CONSOLE_TOKEN,
  ].some(Boolean);
  test.skip(!configured, 'Set the HONUA_AI_ARC_* boundary inputs to run the live Console producer.');
  test.setTimeout(180_000);

  expect(process.env.HONUA_ADMIN_KEY, 'broad HONUA_ADMIN_KEY must not enter the Console producer').toBeFalsy();
  expect(process.env.HONUA_API_KEY, 'broad HONUA_API_KEY must not enter the Console producer').toBeFalsy();
  const endpoint = required(process.env.HONUA_AI_ARC_ENDPOINT, 'HONUA_AI_ARC_ENDPOINT');
  const checkpointPath = resolve(required(process.env.HONUA_AI_ARC_CHECKPOINT, 'HONUA_AI_ARC_CHECKPOINT'));
  const preConsoleEvidencePath = resolve(required(
    process.env.HONUA_AI_ARC_REAL_MODEL_EVIDENCE ?? process.env.HONUA_AI_ARC_PRE_CONSOLE_RECEIPT,
    'HONUA_AI_ARC_REAL_MODEL_EVIDENCE',
  ));
  const outputPath = resolve(required(process.env.HONUA_AI_ARC_CONSOLE_RECEIPT, 'HONUA_AI_ARC_CONSOLE_RECEIPT'));
  const sdkOutputPath = resolve(process.env.HONUA_AI_ARC_SDK_CONSOLE_RECEIPT ?? `${outputPath}.sdk.json`);
  const credential = required(process.env.HONUA_AI_ARC_CONSOLE_TOKEN, 'HONUA_AI_ARC_CONSOLE_TOKEN');

  const checkpoint = JSON.parse(await readFile(checkpointPath, 'utf8')) as unknown;
  const preConsoleEvidence = JSON.parse(await readFile(preConsoleEvidencePath, 'utf8')) as unknown;
  const boundary = readConsoleBoundary(checkpoint, preConsoleEvidence);
  const receipts = await produceConsoleReceipts({
    endpoint,
    credential,
    mode: process.env.HONUA_CONSOLE_MODE ?? 'full',
    boundary,
    transport: playwrightRequestTransport(request),
  });

  await writeJsonAtomic(outputPath, receipts.releaseReceipt);
  await writeJsonAtomic(sdkOutputPath, receipts.sdkReceipt);
  expect(receipts.releaseReceipt.status).toBe('passed');
  expect(receipts.sdkReceipt.status).toBe('passed');
});

async function writeJsonAtomic(path: string, value: unknown): Promise<void> {
  await mkdir(dirname(path), { recursive: true });
  const temporary = `${path}.${process.pid}.tmp`;
  await writeFile(temporary, `${JSON.stringify(value, null, 2)}\n`, { encoding: 'utf8', mode: 0o600 });
  await rename(temporary, path);
}

function required(value: string | undefined, name: string): string {
  const trimmed = value?.trim();
  if (!trimmed) throw new Error(`${name} is required for the candidate-backed Console gate`);
  return trimmed;
}
