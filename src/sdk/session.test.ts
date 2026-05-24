import { describe, expect, it, vi } from "vitest";

import { SessionClient } from "./session";

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { "content-type": "application/json" },
  });
}

describe("SessionClient", () => {
  it("uses the configured base URL and user-id claim for the permissions fan-out", async () => {
    const requests: string[] = [];
    const fetchImpl = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      requests.push(url);

      if (url === "https://honua.example/api/v1/admin/auth/session") {
        return jsonResponse({
          success: true,
          data: {
            isAuthenticated: true,
            providerKey: "oidc",
            claims: [
              { type: "sub", value: "user-1" },
              { type: "name", value: "Admin User" },
            ],
          },
        });
      }

      if (url === "https://honua.example/api/v1/admin/license/entitlements") {
        return jsonResponse({
          success: true,
          data: [
            { key: "sharing", name: "Sharing", isActive: true },
            { key: "disabled.feature", name: "Disabled", isActive: false },
          ],
        });
      }

      if (url === "https://honua.example/api/v1/admin/users/user-1/effective-permissions") {
        return jsonResponse({
          success: true,
          data: {
            userId: "user-1",
            roles: ["admin"],
            permissions: [{ service: "*", layer: "*", operation: "*" }],
          },
        });
      }

      return new Response("not found", { status: 404 });
    });

    const result = await new SessionClient({
      baseUrl: "https://honua.example",
      fetchImpl: fetchImpl as typeof fetch,
    }).bootstrap();

    expect(requests).toContain("https://honua.example/api/v1/admin/auth/session");
    expect(requests).toContain("https://honua.example/api/v1/admin/license/entitlements");
    expect(requests).toContain("https://honua.example/api/v1/admin/users/user-1/effective-permissions");
    expect(requests).not.toContain("https://honua.example/api/v1/admin/users/oidc/effective-permissions");
    expect(result.status.kind).toBe("authenticated");
    if (result.status.kind !== "authenticated") return;
    expect(result.status.identity.providerKey).toBe("oidc");
    expect(result.status.identity.userId).toBe("user-1");
    expect(result.status.bundle.capabilities.has("permission:*:*:*")).toBe(true);
    expect(result.status.bundle.capabilities.has("catalog:read")).toBe(true);
    expect(result.status.bundle.capabilities.has("map-packages:read")).toBe(true);
    expect(result.status.bundle.capabilities.has("studio:preview")).toBe(true);
    expect(result.status.bundle.capabilities.has("operate:provenance:read")).toBe(true);
    expect(result.status.bundle.entitlements.has("sharing")).toBe(true);
    expect(result.status.bundle.entitlements.has("disabled.feature")).toBe(false);
  });

  it("maps 401/403 secondary endpoints to fallback metadata while keeping authenticated state", async () => {
    const fetchImpl = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith("/auth/session")) {
        return jsonResponse({
          isAuthenticated: true,
          providerKey: "oidc",
          claims: [{ type: "sub", value: "user-1" }],
        });
      }
      if (url.endsWith("/effective-permissions")) return new Response("", { status: 403 });
      return new Response("", { status: 401 });
    });

    const result = await new SessionClient({
      fetchImpl: fetchImpl as typeof fetch,
    }).bootstrap();

    expect(result.status.kind).toBe("authenticated");
    expect(result.fellBackEndpoints).toEqual([
      "/api/v1/admin/users/user-1/effective-permissions",
      "/api/v1/admin/license/entitlements",
    ]);
  });
});
