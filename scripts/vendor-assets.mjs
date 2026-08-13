#!/usr/bin/env node
// Vendors the Console's third-party browser assets from npm into wwwroot, reproducibly.
//
// Why a script and not an npm dependency: the Console has no bundler. package.json declares zero
// runtime dependencies and Node exists here only as the smoke/parity harness, so there is no build
// step that could turn `node_modules/maplibre-gl` into a served asset. The assets are therefore
// committed static files — and a committed binary blob with no recorded provenance is exactly the
// supply-chain problem honua-console#333 is about. This script is the provenance.
//
//   node scripts/vendor-assets.mjs            # verify committed assets against the lock (offline)
//   node scripts/vendor-assets.mjs --update   # re-fetch from npm, rewrite assets + lock
//
// Update procedure (bumping maplibre-gl, or adding another package):
//   1. edit scripts/vendored-assets.json (version, or a new package entry)
//   2. node scripts/vendor-assets.mjs --update
//   3. commit the rewritten wwwroot assets together with scripts/vendored-assets.lock.json
//
// The verify path is what `npm test` runs (scripts/__tests__/vendor-assets.test.mjs): it needs no
// network and fails if a committed byte ever drifts from the digest recorded at vendoring time.
//
// Provenance chain, end to end:
//   manifest version -> registry metadata -> tarball sha512 (npm's own `dist.integrity`)
//   -> per-file sha384 of the extracted bytes -> the committed file on disk.
// Every link is recorded in scripts/vendored-assets.lock.json, so anyone can re-derive the
// committed bytes from a public registry without trusting this repo's history.

import { createHash } from "node:crypto";
import { mkdir, readFile, readdir, rm, writeFile } from "node:fs/promises";
import { basename, dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { gunzipSync } from "node:zlib";

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const manifestPath = resolve(repoRoot, "scripts/vendored-assets.json");
const lockPath = resolve(repoRoot, "scripts/vendored-assets.lock.json");
const registryBase = process.env.HONUA_NPM_REGISTRY ?? "https://registry.npmjs.org";

/** sha384-<base64>, the same shape Subresource Integrity uses, so a digest is quotable as-is. */
export function sriDigest(bytes) {
  return `sha384-${createHash("sha384").update(bytes).digest("base64")}`;
}

function sha512Integrity(bytes) {
  return `sha512-${createHash("sha512").update(bytes).digest("base64")}`;
}

/**
 * Minimal gzip+tar reader. Node ships zlib but no tar, and this repo has zero dependencies by
 * design — a ~40-line ustar walker is a better trade than adding one for three files a year.
 * Returns a Map of entry path -> Buffer for regular files only.
 */
export function readTarGz(gzipped) {
  const tar = gunzipSync(gzipped);
  const entries = new Map();
  let offset = 0;
  let pendingLongName = null;

  while (offset + 512 <= tar.length) {
    const header = tar.subarray(offset, offset + 512);
    // Two consecutive zero blocks terminate the archive; one is enough to stop reading.
    if (header.every((byte) => byte === 0)) break;

    const name = header.subarray(0, 100).toString("utf8").replace(/\0.*$/, "");
    const prefix = header.subarray(345, 500).toString("utf8").replace(/\0.*$/, "");
    const sizeField = header.subarray(124, 136).toString("utf8").replace(/\0.*$/, "").trim();
    const size = Number.parseInt(sizeField, 8) || 0;
    const typeFlag = String.fromCharCode(header[156]);
    const body = tar.subarray(offset + 512, offset + 512 + size);
    offset += 512 + Math.ceil(size / 512) * 512;

    if (typeFlag === "L") {
      // GNU long-name extension: the next header's name lives in this entry's body.
      pendingLongName = body.toString("utf8").replace(/\0.*$/, "");
      continue;
    }
    const fullName = pendingLongName ?? (prefix ? `${prefix}/${name}` : name);
    pendingLongName = null;
    if (typeFlag === "0" || typeFlag === "\0") {
      entries.set(fullName, Buffer.from(body));
    }
  }
  return entries;
}

async function fetchBuffer(url) {
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`GET ${url} failed: ${response.status} ${response.statusText}`);
  }
  return Buffer.from(await response.arrayBuffer());
}

async function readManifest() {
  return JSON.parse(await readFile(manifestPath, "utf8"));
}

async function readLock() {
  return JSON.parse(await readFile(lockPath, "utf8"));
}

async function update() {
  const manifest = await readManifest();
  const lock = { $comment: LOCK_COMMENT, packages: {} };

  for (const pkg of manifest.packages) {
    const metadataUrl = `${registryBase}/${pkg.name}/${pkg.version}`;
    const metadata = JSON.parse((await fetchBuffer(metadataUrl)).toString("utf8"));
    const tarballUrl = metadata?.dist?.tarball;
    const declaredIntegrity = metadata?.dist?.integrity;
    if (!tarballUrl) {
      throw new Error(`${pkg.name}@${pkg.version}: registry metadata has no dist.tarball`);
    }

    const tarball = await fetchBuffer(tarballUrl);
    // Recompute rather than trust: the registry states an integrity, and the bytes that arrived
    // must actually hash to it. A CDN/mirror substituting a tarball fails here, not at page load.
    const actualIntegrity = sha512Integrity(tarball);
    if (declaredIntegrity && declaredIntegrity !== actualIntegrity) {
      throw new Error(
        `${pkg.name}@${pkg.version}: tarball integrity mismatch\n` +
          `  registry: ${declaredIntegrity}\n  actual:   ${actualIntegrity}`,
      );
    }

    const entries = readTarGz(tarball);
    const destination = resolve(repoRoot, pkg.destination);
    // Rewrite the destination from scratch so a file dropped from the manifest cannot linger.
    await rm(destination, { recursive: true, force: true });
    await mkdir(destination, { recursive: true });

    const files = {};
    for (const relative of pkg.files) {
      // npm tarballs root every entry under `package/`.
      const bytes = entries.get(`package/${relative}`);
      if (!bytes) {
        throw new Error(`${pkg.name}@${pkg.version}: ${relative} is not in the published tarball`);
      }
      const name = basename(relative);
      await writeFile(join(destination, name), bytes);
      files[name] = sriDigest(bytes);
      console.log(`vendored ${pkg.name}@${pkg.version} ${relative} -> ${pkg.destination}/${name}`);
    }
    await writeFile(join(destination, "README.md"), vendorReadme(pkg, tarballUrl, actualIntegrity));

    lock.packages[pkg.name] = {
      version: pkg.version,
      license: pkg.license,
      resolved: tarballUrl,
      integrity: actualIntegrity,
      destination: pkg.destination,
      files,
    };
  }

  await writeFile(lockPath, `${JSON.stringify(lock, null, 2)}\n`);
  console.log(`wrote ${lockPath}`);
}

const LOCK_COMMENT =
  "Generated by scripts/vendor-assets.mjs --update. Do not hand-edit: change " +
  "scripts/vendored-assets.json and re-run the script. `integrity` is the npm tarball digest; " +
  "`files` are sha384 digests of the exact bytes committed under wwwroot.";

function vendorReadme(pkg, tarballUrl, integrity) {
  return `# Vendored: ${pkg.name}@${pkg.version}

Committed third-party browser assets, served from the Console's own origin. The Console must not
fetch executable code from a CDN at page load (honua-console#333): a customer without egress gets a
broken surface, and the CSP would have to admit a script origin nothing else needs.

| | |
| --- | --- |
| Package | \`${pkg.name}@${pkg.version}\` |
| License | ${pkg.license} (see \`LICENSE.txt\`) |
| Source | ${tarballUrl} |
| Tarball integrity | \`${integrity}\` |

**Do not edit these files by hand.** They are byte-for-byte copies of the published npm tarball
contents, and \`npm test\` verifies their digests against \`scripts/vendored-assets.lock.json\`.

To update:

\`\`\`sh
# 1. bump "version" in scripts/vendored-assets.json
node scripts/vendor-assets.mjs --update
# 2. commit the rewritten assets together with scripts/vendored-assets.lock.json
\`\`\`
`;
}

/**
 * Offline verification: every locked file exists with the locked digest, the lock agrees with the
 * manifest, and no unlocked strays sit in a vendor directory.
 * Returns an array of human-readable problems (empty when clean).
 */
export async function verify() {
  const manifest = await readManifest();
  const lock = await readLock();
  const problems = [];

  for (const pkg of manifest.packages) {
    const locked = lock.packages?.[pkg.name];
    if (!locked) {
      problems.push(`${pkg.name}: missing from the lock — run \`node scripts/vendor-assets.mjs --update\``);
      continue;
    }
    if (locked.version !== pkg.version) {
      problems.push(
        `${pkg.name}: manifest pins ${pkg.version} but the lock holds ${locked.version} — re-run --update`,
      );
    }
    if (locked.destination !== pkg.destination) {
      problems.push(`${pkg.name}: manifest destination ${pkg.destination} != locked ${locked.destination}`);
    }

    const expectedNames = pkg.files.map((file) => basename(file));
    const lockedNames = Object.keys(locked.files ?? {});
    for (const name of expectedNames) {
      if (!lockedNames.includes(name)) problems.push(`${pkg.name}: ${name} is in the manifest but not the lock`);
    }

    const destination = resolve(repoRoot, locked.destination);
    for (const [name, digest] of Object.entries(locked.files ?? {})) {
      const file = join(destination, name);
      let bytes;
      try {
        bytes = await readFile(file);
      } catch {
        problems.push(`${locked.destination}/${name}: missing`);
        continue;
      }
      const actual = sriDigest(bytes);
      if (actual !== digest) {
        problems.push(`${locked.destination}/${name}: digest mismatch\n    locked: ${digest}\n    actual: ${actual}`);
      }
    }

    // A file nobody locked is a file nobody reviewed. README.md is written by --update itself.
    let present;
    try {
      present = await readdir(destination);
    } catch {
      problems.push(`${locked.destination}: directory missing`);
      continue;
    }
    for (const name of present) {
      if (name === "README.md") continue;
      if (!lockedNames.includes(name)) problems.push(`${locked.destination}/${name}: present but not locked`);
    }
  }

  return problems;
}

async function main() {
  const wantsUpdate = process.argv.includes("--update");
  if (wantsUpdate) {
    await update();
    return;
  }
  const problems = await verify();
  if (problems.length > 0) {
    console.error("Vendored assets are out of sync with scripts/vendored-assets.lock.json:");
    for (const problem of problems) console.error(`  - ${problem}`);
    process.exitCode = 1;
    return;
  }
  console.log("Vendored assets match the lock.");
}

// Only run as a CLI; the test suite imports `verify` directly.
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  await main();
}
