import { createServer } from "node:http";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { startStaticBundleServer } from "./static-bundle-server.mjs";

// Network fixture for the published browser transport. This deliberately starts with an
// issued session; server sign-in, token issuance and OIDC remain separate integration gates.
export const fixtureCookie = "console-fixture-session=issued";
export const fixtureBearer = "console-fixture-bearer";

export function createContractApi({ allowedOrigin, crossOrigin = false }) {
  return (request, response) => {
    response.setHeader("Cache-Control", "no-store");
    const origin = request.headers.origin;
    if (origin && origin !== allowedOrigin) {
      response.writeHead(403).end();
      return;
    }
    if (origin) {
      response.setHeader("Access-Control-Allow-Origin", allowedOrigin);
      response.setHeader("Vary", "Origin");
    }
    if (request.method === "OPTIONS") {
      const headers = (request.headers["access-control-request-headers"] ?? "")
        .toLowerCase().split(",").map(value => value.trim()).filter(Boolean);
      if (!origin || request.headers["access-control-request-method"] !== "GET"
        || headers.some(header => header !== "authorization")) {
        response.writeHead(403).end();
        return;
      }
      response.setHeader("Access-Control-Allow-Methods", "GET");
      response.setHeader("Access-Control-Allow-Headers", "Authorization");
      response.writeHead(204).end();
      return;
    }
    if (request.method !== "GET") {
      response.writeHead(405).end();
      return;
    }
    const pathname = new URL(request.url, "http://localhost").pathname.replace(/\/$/, "");
    const json = body => {
      response.writeHead(200, { "Content-Type": "application/json" });
      response.end(JSON.stringify(body));
    };
    const cookiePresent = request.headers.cookie?.split("; ").includes(fixtureCookie);
    if (pathname === "/api/v1/admin/auth/session") {
      if (!crossOrigin && !cookiePresent) {
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
        csrfToken: "console-fixture-csrf",
        accessToken: crossOrigin ? fixtureBearer : null,
        expiresAt: new Date(Date.now() + 300_000).toISOString(),
      });
      return;
    }
    const authorized = crossOrigin
      ? request.headers.authorization === `Bearer ${fixtureBearer}` && !request.headers.cookie
      : cookiePresent && !request.headers.authorization;
    if (!authorized) {
      response.writeHead(401).end();
      return;
    }
    switch (pathname) {
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
      case "/api/v1/admin/license":
        json({ success: true, data: { edition: "Community", isValid: true } });
        return;
      case "/api/v1/capabilities/manifest":
        json({ schemaVersion: "honua.capability_manifest.v1", capabilities: [], packages: { families: [] } });
        return;
      default:
        response.writeHead(404).end();
    }
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
  await new Promise((resolve, reject) => {
    const api = createServer(createContractApi({ allowedOrigin: browserOrigin, crossOrigin: true }));
    api.once("error", reject);
    api.listen(5187, "127.0.0.1", resolve);
  });
}
