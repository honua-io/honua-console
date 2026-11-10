import assert from "node:assert/strict";
import { mkdtemp, mkdir, writeFile, symlink, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { startStaticBundleServer } from "../../e2e/playwright/static-bundle-server.mjs";

test("static host serves WASM and deep links without hiding API or asset failures", async t => {
  const directory = await mkdtemp(join(tmpdir(), "console-static-host-"));
  t.after(() => rm(directory, { recursive: true, force: true }));
  const root = join(directory, "bundle");
  await mkdir(join(root, "_framework"), { recursive: true });
  await writeFile(join(root, "index.html"), '<base href="/console/">');
  await writeFile(join(root, "_framework", "runtime.wasm"), Buffer.from([0, 97, 115, 109]));
  await writeFile(join(directory, "secret.json"), "private");
  await symlink(join(directory, "secret.json"), join(root, "escape.json"));
  const server = await startStaticBundleServer(root);
  t.after(() => new Promise(resolve => server.close(resolve)));
  const origin = `http://127.0.0.1:${server.address().port}`;
  for (const route of ["/console/", "/console/operate/connections"]) {
    const response = await fetch(origin + route, { headers: { Accept: "text/html" } });
    assert.equal(response.status, 200);
    assert.match(response.headers.get("content-type"), /text\/html/);
    assert.equal(await response.text(), '<base href="/console/">');
  }
  const wasm = await fetch(`${origin}/console/_framework/runtime.wasm`);
  assert.equal(wasm.headers.get("content-type"), "application/wasm");
  assert.deepEqual(new Uint8Array(await wasm.arrayBuffer()), new Uint8Array([0, 97, 115, 109]));
  const head = await fetch(`${origin}/console/index.html`, { method: "HEAD" });
  assert.equal(head.status, 200);
  assert.equal(await head.text(), "");
  for (const route of ["/api/v1/admin/auth/session", "/console/missing.js", "/console/escape.json",
    "/console/%2e%2e%2fsecret.json", "/console/%zz"]) {
    assert.equal((await fetch(origin + route, { headers: { Accept: "text/html" } })).status, 404, route);
  }
  assert.equal((await fetch(`${origin}/console/unknown`)).status, 404);
  assert.equal((await fetch(`${origin}/console/`, { method: "POST" })).status, 405);
});
