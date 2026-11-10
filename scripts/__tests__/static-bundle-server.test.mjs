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
  const callback = await fetch(`${origin}/admin/auth/callback?code=fixture-code&state=fixture-state`, { redirect: "manual" });
  assert.equal(callback.status, 302);
  assert.equal(callback.headers.get("location"), "/console/admin/auth/callback?code=fixture-code&state=fixture-state");
  assert.equal(callback.headers.get("referrer-policy"), "no-referrer");
  assert.equal(callback.headers.get("cache-control"), "no-store");
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

test("deployment fixture overrides only public runtime configuration", async t => {
  const root = await mkdtemp(join(tmpdir(), "console-static-config-"));
  t.after(() => rm(root, { recursive: true, force: true }));
  const configuration = { apiBaseUrl: "https://api.example.test/", auth: { mode: "builtin" } };
  const server = await startStaticBundleServer(root, 0, { configuration });
  t.after(() => new Promise(resolve => server.close(resolve)));
  const url = `http://127.0.0.1:${server.address().port}/console/config.json`;
  assert.deepEqual(await (await fetch(url)).json(), configuration);
  assert.equal((await fetch(url)).headers.get("cache-control"), "no-store");
  assert.equal(await (await fetch(url, { method: "HEAD" })).text(), "");
  assert.equal((await fetch(url, { method: "POST" })).status, 405);
});
