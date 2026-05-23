import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { afterAll, beforeAll, describe, expect, it } from "vitest";

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, "..");
const script = resolve(repoRoot, "scripts/write-build-metadata.mjs");

describe("write-build-metadata", () => {
  let outDir: string;
  let metadata: Record<string, unknown>;

  beforeAll(() => {
    outDir = mkdtempSync(resolve(tmpdir(), "honua-console-build-"));
    execFileSync(process.execPath, [script], {
      cwd: repoRoot,
      env: {
        ...process.env,
        HONUA_CONSOLE_DIST_DIR: outDir,
        HONUA_CONSOLE_COMMIT_SHA: "0123456789abcdef0123456789abcdef01234567",
        HONUA_CONSOLE_REF: "release/2026.06",
        HONUA_CONSOLE_LEGACY_PORTAL_STATUS: "retiring",
        HONUA_CONSOLE_LEGACY_ADMIN_STATUS: "active",
      },
      stdio: ["ignore", "pipe", "pipe"],
    });
    metadata = JSON.parse(readFileSync(resolve(outDir, "version.json"), "utf8"));
  });

  afterAll(() => {
    rmSync(outDir, { recursive: true, force: true });
  });

  it("stamps name, commit, ref, and legacy status", () => {
    expect(metadata.name).toBe("honua-console");
    expect(metadata.commit).toBe("0123456789abcdef0123456789abcdef01234567");
    expect(metadata.shortCommit).toBe("0123456789ab");
    expect(metadata.ref).toBe("release/2026.06");
    expect(metadata.legacy).toEqual({ portal: "retiring", admin: "active" });
  });

  it("declares the supported areas", () => {
    expect(metadata.areas).toEqual(["studio", "catalog", "share", "operate"]);
  });

  it("includes a parseable builtAt timestamp", () => {
    expect(typeof metadata.builtAt).toBe("string");
    expect(Number.isNaN(Date.parse(metadata.builtAt as string))).toBe(false);
  });
});
