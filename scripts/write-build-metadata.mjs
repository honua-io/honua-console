#!/usr/bin/env node
// Standalone build-metadata writer. The Blazor Web host exposes the same
// schema at `/version.json`; this script writes a copy next to the published
// artifact so release-promotion tooling can inspect or re-stamp metadata
// without rebuilding the web host.

import { execSync } from "node:child_process";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, "..");
const webProject = readFileSync(resolve(repoRoot, "src/Honua.Console.Web/Honua.Console.Web.csproj"), "utf8");
const version = webProject.match(/<Version>([^<]+)<\/Version>/)?.[1]?.trim() ?? "unknown";
const consoleAreas = JSON.parse(readFileSync(resolve(repoRoot, "config/console-areas.json"), "utf8"));

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
  version,
  commit: sha,
  shortCommit: sha.slice(0, 12),
  ref,
  builtAt: process.env.HONUA_CONSOLE_BUILT_AT ?? new Date().toISOString(),
  legacy: {
    portal: process.env.HONUA_CONSOLE_LEGACY_PORTAL_STATUS ?? "active",
    admin: process.env.HONUA_CONSOLE_LEGACY_ADMIN_STATUS ?? "active",
  },
  areas: consoleAreas,
};

const outDir = process.env.HONUA_CONSOLE_ARTIFACT_DIR
  ? resolve(repoRoot, process.env.HONUA_CONSOLE_ARTIFACT_DIR)
  : resolve(repoRoot, "artifacts/honua-console-web");
mkdirSync(outDir, { recursive: true });
const outPath = resolve(outDir, "version.json");
writeFileSync(outPath, `${JSON.stringify(metadata, null, 2)}\n`);
process.stdout.write(`${outPath}\n`);
