import { randomUUID } from "node:crypto";
import { createServer } from "node:http";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { startStaticBundleServer } from "./static-bundle-server.mjs";

// Network fixture for the published browser sign-in contract. Real server authentication
// and OIDC remain separate integration gates; these credentials have no external authority.
export const fixtureCookie = "console-fixture-session=issued";
export const fixtureBearer = "console-fixture-bearer";

function acceptOrigin({ request, response, allowedOrigin }) {
  response.setHeader("Cache-Control", "no-store");
  const origin = request.headers.origin;
  if (origin && origin !== allowedOrigin) {
    response.writeHead(403).end();
    return false;
  }
  if (origin) {
    response.setHeader("Access-Control-Allow-Origin", allowedOrigin);
    response.setHeader("Vary", "Origin");
  }
  if (request.method === "OPTIONS") {
    const headers = (request.headers["access-control-request-headers"] ?? "")
      .toLowerCase().split(",").map(value => value.trim()).filter(Boolean);
    if (!origin || !["GET", "POST"].includes(request.headers["access-control-request-method"])
      || headers.some(header => !["authorization", "content-type"].includes(header))) {
      response.writeHead(403).end();
      return false;
    }
    response.setHeader("Access-Control-Allow-Methods", "GET, POST");
    response.setHeader("Access-Control-Allow-Headers", "Authorization, Content-Type");
    response.writeHead(204).end();
    return false;
  }
  return true;
}

async function authorizeOidc({ request, response, allowedOrigin, pending, json }) {
  if (request.headers["x-honua-csrf"] !== "anonymous-csrf") {
    response.writeHead(403).end();
    return;
  }
  let body = "";
  for await (const chunk of request) body += chunk;
  let returnTo;
  try { returnTo = JSON.parse(body).returnTo ?? "/console/"; } catch { /* Deny invalid input. */ }
  if (!["/console/", "/console/operate/connections"].includes(returnTo)) {
    response.writeHead(400).end();
    return;
  }
  const state = randomUUID();
  pending.set(state, returnTo);
  response.setHeader("Set-Cookie", `fixture-pending=${state}; Path=/; HttpOnly; SameSite=Lax`);
  json({ authorizeUrl: `${allowedOrigin}/api/fixture-provider?state=${state}` });
}

function completeOidc({ request, response, allowedOrigin, pending }) {
  const query = new URL(request.url, allowedOrigin).searchParams;
  const state = query.get("state");
  const returnTo = pending.get(state);
  pending.delete(state);
  response.setHeader("Referrer-Policy", "no-referrer");
  if (!returnTo || query.get("code") !== "fixture-code"
    || !request.headers.cookie?.split("; ").includes(`fixture-pending=${state}`)) {
    response.writeHead(403).end();
    return;
  }
  response.setHeader("Set-Cookie", [`${fixtureCookie}; Path=/; HttpOnly; SameSite=Strict`, "fixture-pending=; Path=/; Max-Age=0; HttpOnly; SameSite=Lax"]);
  response.writeHead(302, { Location: returnTo }).end();
}

async function handleOidc(context) {
  const { request, response, allowedOrigin, oidc, pathname, json } = context;
  if (oidc && pathname === "/api/v1/admin/auth/config") {
    json({ oidcEnabled: true, providers: [{ key: "entra", displayName: "Organization" }] });
    return true;
  }
  if (oidc && pathname === "/api/fixture-provider") {
    const state = new URL(request.url, allowedOrigin).searchParams.get("state");
    response.writeHead(302, { Location: `${allowedOrigin}/admin/auth/callback?code=fixture-code&state=${encodeURIComponent(state)}` }).end();
    return true;
  }
  if (!oidc) return false;
  if (request.method === "POST" && pathname === "/api/v1/admin/auth/providers/entra/authorize-url") {
    await authorizeOidc(context);
    return true;
  }
  if (request.method === "GET" && pathname === "/admin/auth/callback") {
    completeOidc(context);
    return true;
  }
  return false;
}

async function signInWithPassword({ request, response, crossOrigin, pathname, origin, allowedOrigin, json }) {
  const validTransport = crossOrigin
    ? pathname.endsWith("/password-token") && origin === allowedOrigin && !request.headers.cookie && !request.headers.authorization
    : pathname.endsWith("/session") && request.headers["x-honua-csrf"] === "anonymous-csrf";
  if (!validTransport) {
    response.writeHead(403).end();
    return;
  }
  let body = "";
  for await (const chunk of request) body += chunk;
  let password;
  try { password = JSON.parse(body).password; } catch { /* Invalid fixture input is denied. */ }
  if (password !== "fixture-password") {
    response.writeHead(401).end();
    return;
  }
  if (crossOrigin) {
    json({ accessToken: fixtureBearer, tokenType: "Bearer", expiresAt: new Date(Date.now() + 300_000).toISOString(), expiresIn: 300 });
  } else {
    response.setHeader("Set-Cookie", `${fixtureCookie}; Path=/; HttpOnly; SameSite=Strict`);
    response.writeHead(204).end();
  }
}

function readResource({ request, response, pathname, crossOrigin, authorized, json }) {
  if (request.method !== "GET") {
    response.writeHead(405).end();
    return;
  }
  if (pathname === "/api/v1/admin/auth/csrf" && !crossOrigin) {
    json({ requestToken: authorized ? "identified-csrf" : "anonymous-csrf" });
    return;
  }
  if (pathname === "/api/v1/admin/auth/session") {
    if (!authorized) {
      response.writeHead(401).end();
      return;
    }
    json({
      isAuthenticated: true,
      claims: [
        { type: "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier", value: "fixture-operator" },
        { type: "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name", value: "Fixture Operator" },
        { type: "http://schemas.microsoft.com/ws/2008/06/identity/claims/role", value: "admin" },
      ],
      expiresAt: new Date(Date.now() + 300_000).toISOString(),
    });
    return;
  }
  if (!authorized) {
    response.writeHead(401).end();
    return;
  }
  readAuthorizedResource({ response, pathname, json });
}

function readAuthorizedResource({ response, pathname, json }) {
  switch (pathname) {
    case "/api/v1/studio/session":
      json({ success: true, data: {
        authenticated: true, principalId: "fixture-operator",
        environmentId: "fixture-environment", sourcePathBase: "",
      } });
      return;
    case "/api/v1/studio/content-items":
      json({ success: true, data: { items: [], nextCursor: null } });
      return;
    case "/api/v1/admin/connections":
      json({ success: true, data: [{
        connectionId: "11111111-1111-1111-1111-111111111111", name: "Browser transport fixture",
        provider: "PostgreSQL/PostGIS", host: "fixture.invalid", databaseName: "transport",
        username: "fixture-operator", isActive: true, healthStatus: "Healthy",
      }] });
      return;
    case "/api/v1/admin/services":
      json({ success: true, data: { items: [], page: 1, pageSize: 100, total: 0, layerTotal: 0 } });
      return;
    case "/api/v1/admin/folders":
      json({ success: true, data: [] });
      return;
    case "/api/v1/admin/proposals":
      json({ proposals: [] });
      return;
    case "/api/v1/admin/license":
      json({ success: true, data: { edition: "Community", isValid: true } });
      return;
    case "/api/v1/capabilities/manifest":
      json({ schemaVersion: "honua.capability_manifest.v1", capabilities: [], packages: { families: [] } });
      return;
    default:
      response.writeHead(404).end();
  }
}

export function createContractApi({ allowedOrigin, crossOrigin = false, oidc = false }) {
  const pending = new Map();
  return async (request, response) => {
    const context = { request, response, allowedOrigin, crossOrigin, oidc, pending };
    if (!acceptOrigin(context)) return;
    context.origin = request.headers.origin;
    context.pathname = new URL(request.url, "http://localhost").pathname.replace(/\/$/, "");
    context.json = body => {
      response.writeHead(200, { "Content-Type": "application/json" });
      response.end(JSON.stringify(body));
    };
    const cookiePresent = request.headers.cookie?.split("; ").includes(fixtureCookie);
    context.authorized = crossOrigin
      ? request.headers.authorization === `Bearer ${fixtureBearer}` && !request.headers.cookie
      : cookiePresent && !request.headers.authorization;
    if (await handleOidc(context)) return;
    if (request.method === "POST" && ["/api/v1/admin/auth/session", "/api/v1/admin/auth/password-token"].includes(context.pathname)) {
      await signInWithPassword(context);
      return;
    }
    readResource(context);
  };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const directory = process.env.HONUA_CONSOLE_STATIC_ROOT ?? "artifacts/static-publish/wwwroot/console";
  const sameOrigin = "http://127.0.0.1:5185";
  const browserOrigin = "http://127.0.0.1:5186";
  const apiOrigin = "http://127.0.0.1:5187";
  await startStaticBundleServer(directory, 5185, {
    configuration: { apiBaseUrl: "/", auth: { mode: "builtin" }, v4Enabled: true },
    handleApi: createContractApi({ allowedOrigin: sameOrigin }),
  });
  await startStaticBundleServer(directory, 5186, {
    configuration: { apiBaseUrl: apiOrigin, auth: { mode: "builtin" }, v4Enabled: true },
  });
  const oidcOrigin = "http://127.0.0.1:5188";
  await startStaticBundleServer(directory, 5188, {
    configuration: { apiBaseUrl: "/", auth: { mode: "oidc", authority: "https://identity.example/", clientId: "public-fixture", scopes: ["openid"] }, v4Enabled: true },
    handleApi: createContractApi({ allowedOrigin: oidcOrigin, oidc: true }),
  });
  await new Promise((resolve, reject) => {
    const api = createServer(createContractApi({ allowedOrigin: browserOrigin, crossOrigin: true }));
    api.once("error", reject);
    api.listen(5187, "127.0.0.1", resolve);
  });
}
