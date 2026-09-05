import { randomUUID } from 'node:crypto';
import { mkdir, rename, unlink, writeFile } from 'node:fs/promises';
import { dirname } from 'node:path';
import { validatePinnedJsonSchema } from './json-schema-validator.mjs';

/**
 * The release and SDK paths are aliases for one consumer-owned aggregate.
 * Validate both write intents against that pinned schema, then deliberately
 * return the same immutable byte string for the two distinct files.
 */
export function buildReceiptAliasBytes(aggregate, receiptSchema) {
  validatePinnedJsonSchema(aggregate, receiptSchema);
  validatePinnedJsonSchema(aggregate, receiptSchema);
  const bytes = `${JSON.stringify(aggregate, null, 2)}\n`;
  return Object.freeze({ aggregateBytes: bytes, sdkBytes: bytes });
}

export async function clearReceiptOutputs(paths) {
  for (const path of paths) {
    await unlink(path).catch((error) => {
      if (error?.code !== 'ENOENT') throw error;
    });
  }
}

export async function writeReceiptSetAtomic(outputs) {
  const paths = outputs.map(({ path }) => path);
  if (new Set(paths).size !== paths.length) throw new Error('receipt output paths must be distinct');
  const nonce = `${process.pid}.${randomUUID()}`;
  const temporary = outputs.map(({ path }) => `${path}.${nonce}.tmp`);
  const backups = outputs.map(({ path }) => `${path}.${nonce}.bak`);
  const committed = [];
  const backedUp = [];
  try {
    for (let index = 0; index < outputs.length; index += 1) {
      const { path, bytes } = outputs[index];
      await mkdir(dirname(path), { recursive: true });
      await writeFile(temporary[index], bytes, { encoding: 'utf8', mode: 0o600 });
    }
    for (let index = 0; index < outputs.length; index += 1) {
      await rename(paths[index], backups[index]).then(() => backedUp.push(index)).catch((error) => {
        if (error?.code !== 'ENOENT') throw error;
      });
    }
    for (let index = 0; index < outputs.length; index += 1) {
      await rename(temporary[index], outputs[index].path);
      committed.push(index);
    }
    await Promise.all(backedUp.map((index) => unlink(backups[index])));
  } finally {
    if (committed.length !== outputs.length) {
      await Promise.all(committed.map((index) => unlink(paths[index]).catch((error) => {
        if (error?.code !== 'ENOENT') throw error;
      })));
      await Promise.all(backedUp.map((index) => rename(backups[index], paths[index]).catch((error) => {
        if (error?.code !== 'ENOENT') throw error;
      })));
    }
    await Promise.all(temporary.map((path) => unlink(path).catch((error) => {
      if (error?.code !== 'ENOENT') throw error;
    })));
    await Promise.all(backups.map((path) => unlink(path).catch((error) => {
      if (error?.code !== 'ENOENT') throw error;
    })));
  }
}
