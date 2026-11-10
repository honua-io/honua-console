import assert from "node:assert/strict";
import { createServer } from "node:http";
import test from "node:test";
import { createContractApi, fixtureBearer, fixtureCookie } from "../../e2e/playwright/static-contract-server.mjs";

async function start(t, options) {
  const server = createServer(createContractApi(options));
  await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
  t.after(() => new Promise(resolve => server.close(resolve)));
  return `http://127.0.0.1:${server.address().port}`;
}

test("contract fixture requires the same-origin session cookie and keeps unknown APIs missing", async t => {
  const origin = await start(t, { allowedOrigin: "http://console.test" });
  const path = "/api/v1/admin/connections";
  assert.equal((await fetch(origin + path)).status, 401);
  assert.equal((await fetch(origin + path, { headers: { Authorization: `Bearer ${fixtureBearer}` } })).status, 401);
  const headers = { Cookie: fixtureCookie };
  const session = await fetch(`${origin}/api/v1/admin/auth/session`, { headers });
  assert.equal(session.status, 200);
  assert.equal((await session.json()).accessToken, null);
  const response = await fetch(origin + path, { headers });
  assert.equal(response.status, 200);
  assert.equal((await response.json()).data[0].name, "Browser transport fixture");
  assert.equal((await fetch(`${origin}/api/unimplemented`, { headers })).status, 404);
  assert.equal((await fetch(origin + path, { method: "POST", headers })).status, 405);
});

test("cross-origin fixture allows one origin and bearer header, rejecting cookie fallback", async t => {
  const allowedOrigin = "http://console.test";
  const origin = await start(t, { allowedOrigin, crossOrigin: true });
  const path = "/api/v1/admin/connections";
  const headers = { Origin: allowedOrigin, Authorization: `Bearer ${fixtureBearer}` };
  const response = await fetch(origin + path, { headers });
  assert.equal(response.status, 200);
  assert.equal(response.headers.get("access-control-allow-origin"), allowedOrigin);
  assert.equal(response.headers.get("vary"), "Origin");
  assert.equal((await fetch(origin + path, { headers: { ...headers, Origin: "http://other.test" } })).status, 403);
  assert.equal((await fetch(origin + path, { headers: { Origin: allowedOrigin, Cookie: fixtureCookie } })).status, 401);
  assert.equal((await fetch(origin + path, { headers: { ...headers, Cookie: fixtureCookie } })).status, 401);
  const preflight = {
    Origin: allowedOrigin, "Access-Control-Request-Method": "GET", "Access-Control-Request-Headers": "authorization",
  };
  assert.equal((await fetch(origin + path, { method: "OPTIONS", headers: preflight })).status, 204);
  assert.equal((await fetch(origin + path, {
    method: "OPTIONS", headers: { ...preflight, "Access-Control-Request-Headers": "x-api-key" },
  })).status, 403);
});
