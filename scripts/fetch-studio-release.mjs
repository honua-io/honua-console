#!/usr/bin/env node
import { existsSync } from "node:fs";
import { resolve } from "node:path";

import {
  STUDIO_RELEASE_LOCK_PATH,
  VENDORED_STUDIO_ROOT,
  fetchStudioRelease,
  readStudioReleaseLock,
  releaseAssetUrl,
  verifyStudioAssets,
} from "./lib/studio-release.mjs";

const USAGE = `usage:
  node scripts/fetch-studio-release.mjs                 verify the vendored Studio release; fetch it when missing or stale
  node scripts/fetch-studio-release.mjs --force         fetch the locked Studio release, replacing the vendored tree
  node scripts/fetch-studio-release.mjs --verify [DIR]  verify DIR (default: the vendored tree) without fetching
HONUA_STUDIO_RELEASE_CACHE=DIR keeps the verified release archive in DIR for reuse.`;

const [mode, target, ...extra] = process.argv.slice(2);
try {
  const lock = await readStudioReleaseLock();
  if (extra.length > 0 || (target && mode !== "--verify")) throw new Error(USAGE);
  if (mode === "--verify") await verify(lock, resolve(target ?? VENDORED_STUDIO_ROOT));
  else if (mode === "--force") await vendorRelease(lock);
  else if (mode === undefined) await ensureVendoredRelease(lock);
  else throw new Error(USAGE);
} catch (error) {
  console.error(`honua-studio release (${STUDIO_RELEASE_LOCK_PATH}): ${error.message}`);
  process.exit(1);
}

async function verify(lock, root) {
  await verifyStudioAssets(root, lock);
  console.log(`honua-studio ${lock.tag}: ${root} matches the release lock`);
}

async function ensureVendoredRelease(lock) {
  if (await vendoredTreeMatches(lock)) {
    console.log(`honua-studio ${lock.tag}: the vendored tree already matches the release lock`);
    return;
  }
  await vendorRelease(lock);
}

async function vendorRelease(lock) {
  console.log(`honua-studio ${lock.tag}: vendoring ${releaseAssetUrl(lock)}`);
  await fetchStudioRelease(lock, VENDORED_STUDIO_ROOT, { cacheDirectory: process.env.HONUA_STUDIO_RELEASE_CACHE });
  console.log(`honua-studio ${lock.tag}: vendored sha256 ${lock.sha256} into ${VENDORED_STUDIO_ROOT}`);
}

async function vendoredTreeMatches(lock) {
  if (!existsSync(VENDORED_STUDIO_ROOT)) return false;
  try {
    await verifyStudioAssets(VENDORED_STUDIO_ROOT, lock);
    return true;
  } catch (error) {
    console.log(`honua-studio ${lock.tag}: replacing the vendored tree (${error.message})`);
    return false;
  }
}
