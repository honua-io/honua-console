import { execSync } from "node:child_process";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";
import react from "@vitejs/plugin-react";
import { defineConfig, type Plugin } from "vite";

const here = dirname(fileURLToPath(import.meta.url));
const pkg = JSON.parse(readFileSync(resolve(here, "package.json"), "utf8")) as { version: string };
const consoleAreas = JSON.parse(readFileSync(resolve(here, "src/areas.json"), "utf8")) as readonly string[];

function describeCommit(): { sha: string; shortSha: string; ref: string } {
  const sha = process.env.HONUA_CONSOLE_COMMIT_SHA ?? readGit(["rev-parse", "HEAD"]);
  const shortSha = sha.slice(0, 12);
  const ref = process.env.HONUA_CONSOLE_REF ?? readGit(["rev-parse", "--abbrev-ref", "HEAD"]);
  return { sha, shortSha, ref };
}

function readGit(args: string[]): string {
  try {
    return execSync(`git ${args.join(" ")}`, { cwd: here, stdio: ["ignore", "pipe", "ignore"] })
      .toString()
      .trim();
  } catch {
    return "unknown";
  }
}

function buildMetadataPlugin(): Plugin {
  // Devops promotion tooling reads /version.json to identify the deployed
  // artifact. Keep the schema stable: { name, version, commit, ref, builtAt,
  // legacy: { portal, admin } }.
  return {
    name: "honua-console:build-metadata",
    apply: "build",
    closeBundle() {
      const { sha, shortSha, ref } = describeCommit();
      const builtAt = new Date().toISOString();
      const metadata = {
        name: "honua-console",
        version: pkg.version,
        commit: sha,
        shortCommit: shortSha,
        ref,
        builtAt,
        legacy: {
          // Devops release notes use this block to mark when each legacy
          // surface stops being deployed. Default is "active" until the
          // freeze tickets (honua-console#10) flip these to "retired".
          portal: process.env.HONUA_CONSOLE_LEGACY_PORTAL_STATUS ?? "active",
          admin: process.env.HONUA_CONSOLE_LEGACY_ADMIN_STATUS ?? "active",
        },
        areas: consoleAreas,
      };
      const outDir = resolve(here, "dist");
      mkdirSync(outDir, { recursive: true });
      writeFileSync(resolve(outDir, "version.json"), `${JSON.stringify(metadata, null, 2)}\n`);
    },
  };
}

export default defineConfig(({ mode }) => {
  const { sha, shortSha, ref } = describeCommit();
  const builtAt = new Date().toISOString();
  // Base path is configurable so the devops bundle can mount Console under a
  // subpath if a deployment topology ever requires it. Default "/" keeps the
  // single-origin path map (/studio, /catalog, /share, /operate) unchanged.
  const base = process.env.HONUA_CONSOLE_BASE_PATH ?? "/";
  return {
    base,
    plugins: [react(), buildMetadataPlugin()],
    define: {
      __HONUA_CONSOLE_VERSION__: JSON.stringify(pkg.version),
      __HONUA_CONSOLE_COMMIT__: JSON.stringify(sha),
      __HONUA_CONSOLE_SHORT_COMMIT__: JSON.stringify(shortSha),
      __HONUA_CONSOLE_REF__: JSON.stringify(ref),
      __HONUA_CONSOLE_BUILT_AT__: JSON.stringify(builtAt),
      __HONUA_CONSOLE_MODE__: JSON.stringify(mode),
    },
    server: {
      host: "127.0.0.1",
      port: 5174,
    },
    preview: {
      host: "127.0.0.1",
      port: 4174,
    },
    build: {
      target: "es2022",
      sourcemap: true,
      rollupOptions: {
        output: {
          manualChunks: {
            react: ["react", "react-dom", "react-router-dom"],
          },
        },
      },
    },
  };
});
