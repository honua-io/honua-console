#!/usr/bin/env node
// Standalone build-metadata writer. Vite emits the same file via the
// build-metadata plugin during `npm run build`; this script exists so
// release-promotion tooling (honua-devops) can re-stamp the artifact with
// promotion-time metadata (e.g., new ref, new legacy status) without
// re-running the full bundler.

import { execSync } from "node:child_process";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, "..");
const pkg = JSON.parse(readFileSync(resolve(repoRoot, "package.json"), "utf8"));

function readGit(args) {
  try {
    return execSync(`git ${args.join(" ")}`, { cwd: repoRoot, stdio: ["ignore", "pipe", "ignore"] })
      .toString()
      .trim();
  } catch {
    return "unknown";
  }
}

const sha = process.env.HONUA_CONSOLE_COMMIT_SHA ?? readGit(["rev-parse", "HEAD"]);
const ref = process.env.HONUA_CONSOLE_REF ?? readGit(["rev-parse", "--abbrev-ref", "HEAD"]);

const metadata = {
  name: "honua-console",
  version: pkg.version,
  commit: sha,
  shortCommit: sha.slice(0, 12),
  ref,
  builtAt: new Date().toISOString(),
  legacy: {
    portal: process.env.HONUA_CONSOLE_LEGACY_PORTAL_STATUS ?? "active",
    admin: process.env.HONUA_CONSOLE_LEGACY_ADMIN_STATUS ?? "active",
  },
  areas: ["studio", "catalog", "share", "operate"],
};

const outDir = process.env.HONUA_CONSOLE_DIST_DIR
  ? resolve(repoRoot, process.env.HONUA_CONSOLE_DIST_DIR)
  : resolve(repoRoot, "dist");
mkdirSync(outDir, { recursive: true });
const outPath = resolve(outDir, "version.json");
writeFileSync(outPath, `${JSON.stringify(metadata, null, 2)}\n`);
process.stdout.write(`${outPath}\n`);
