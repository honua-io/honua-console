import assert from "node:assert/strict";
import { existsSync, mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { spawnSync } from "node:child_process";
import { test } from "node:test";

const script = resolve("scripts/remote-console-dotnet.sh");

test("remote Console builds transfer the prepared cache and preserve dotnet arguments", () => {
  const scratch = mkdtempSync(join(tmpdir(), "console-remote-"));
  try {
    const bin = join(scratch, "bin");
    mkdirSync(bin);
    for (const command of ["node", "remote-run", "dotnet"]) {
      writeFileSync(join(bin, command), `#!/usr/bin/env bash
printf '%s\\0' '${command}' "\${HONUA_STUDIO_RELEASE_CACHE:-}" "$@" >> "$COMMAND_LOG"
if [[ '${command}' == node ]]; then printf archive > "$HONUA_STUDIO_RELEASE_CACHE/archive"; fi
if [[ '${command}' == dotnet ]]; then test "$(cat "$HONUA_STUDIO_RELEASE_CACHE/archive")" = archive || exit 18; fi
if [[ '${command}' == remote-run ]]; then exec "$@"; fi
`, { mode: 0o755 });
    }
    const cache = join(scratch, "cache with spaces");
    const log = join(scratch, "commands");
    const result = spawnSync("bash", [script, "test", "project with spaces.csproj", "--filter", "Name=V4"], {
      env: { ...process.env, PATH: `${bin}:${process.env.PATH}`, COMMAND_LOG: log, HONUA_STUDIO_RELEASE_CACHE: cache },
      encoding: "utf8",
    });
    assert.equal(result.status, 0, result.stderr);
    const commands = readFileSync(log, "utf8").split("\0");
    const transferredCache = commands[9];
    assert.match(transferredCache, /^\/tmp\/honua-console-studio\.[A-Za-z0-9]+$/);
    assert.deepEqual(commands, [
      "node", cache, "scripts/fetch-studio-release.mjs", "--force",
      "remote-run", cache, "bash", "scripts/remote-console-dotnet.sh", "--prepared-cache", transferredCache,
      "test", "project with spaces.csproj", "--filter", "Name=V4",
      "dotnet", transferredCache, "test", "project with spaces.csproj", "--filter", "Name=V4", "",
    ]);
    assert.equal(existsSync(transferredCache), false);
  } finally {
    rmSync(scratch, { recursive: true, force: true });
  }
});

test("remote Console builds require dotnet arguments", () => {
  const result = spawnSync("bash", [script], { encoding: "utf8" });
  assert.equal(result.status, 2);
  assert.match(result.stderr, /usage:/);
});

for (const exitCode of [0, 19]) {
  test(`prepared remote caches are removed when dotnet exits ${exitCode}`, () => {
    const scratch = mkdtempSync(join(tmpdir(), "console-prepared-"));
    const cache = mkdtempSync("/tmp/honua-console-studio.");
    try {
      writeFileSync(join(scratch, "dotnet"), `#!/usr/bin/env bash\nexit ${exitCode}\n`, { mode: 0o755 });
      const result = spawnSync("bash", [script, "--prepared-cache", cache, "build"], {
        env: { ...process.env, PATH: `${scratch}:${process.env.PATH}` },
        encoding: "utf8",
      });
      assert.equal(result.status, exitCode, result.stderr);
      assert.equal(existsSync(cache), false);
    } finally {
      rmSync(cache, { recursive: true, force: true });
      rmSync(scratch, { recursive: true, force: true });
    }
  });
}

test("prepared cache cleanup refuses an unrelated directory", () => {
  const cache = mkdtempSync(join(tmpdir(), "console-unrelated-"));
  try {
    const result = spawnSync("bash", [script, "--prepared-cache", cache, "build"], { encoding: "utf8" });
    assert.equal(result.status, 2);
    assert.equal(existsSync(cache), true);
  } finally {
    rmSync(cache, { recursive: true, force: true });
  }
});

test("remote Console builds stop when Studio asset preparation fails", () => {
  const scratch = mkdtempSync(join(tmpdir(), "console-remote-failure-"));
  try {
    const bin = join(scratch, "bin");
    mkdirSync(bin);
    writeFileSync(join(bin, "node"), "#!/usr/bin/env bash\nexit 17\n", { mode: 0o755 });
    const dispatched = join(scratch, "dispatched");
    writeFileSync(join(bin, "remote-run"), `#!/usr/bin/env bash\ntouch '${dispatched}'\n`, { mode: 0o755 });
    const result = spawnSync("bash", [script, "build"], {
      env: { ...process.env, PATH: `${bin}:${process.env.PATH}`, HONUA_STUDIO_RELEASE_CACHE: join(scratch, "cache") },
      encoding: "utf8",
    });
    assert.equal(result.status, 17);
    assert.throws(() => readFileSync(dispatched), { code: "ENOENT" });
  } finally {
    rmSync(scratch, { recursive: true, force: true });
  }
});
