import { describe, expect, it, vi } from "vitest";

import type { Session } from "../auth/types.js";
import { createPortalViewerSdkClient } from "./sdk-client.js";

describe("createPortalViewerSdkClient", () => {
  it("passes authenticated session tokens through the SDK auth provider", async () => {
    const fetchFn = vi.fn((_input: Parameters<typeof fetch>[0], _init?: Parameters<typeof fetch>[1]) => {
      return Promise.resolve(jsonResponse({ features: [], exceededTransferLimit: false }));
    });
    const session: Session = {
      status: "authenticated",
      user: { id: "user_1", displayName: "Alex", email: "alex@example.com" },
      workspace: { id: "workspace_1", name: "Honua" },
      scopes: ["member"],
      accessToken: "portal-token",
    };
    const client = createPortalViewerSdkClient({
      baseUrl: "https://api.honua.example/arcgis",
      session,
      fetchFn,
    });

    await client.queryFeatures({ serviceId: "city/parcels", layerId: 0 });

    const init = fetchFn.mock.calls[0]?.[1];
    expect(new Headers(init?.headers).get("Authorization")).toBe("Bearer portal-token");
  });

  it("omits Authorization when the portal session has no bearer token", async () => {
    const fetchFn = vi.fn((_input: Parameters<typeof fetch>[0], _init?: Parameters<typeof fetch>[1]) => {
      return Promise.resolve(jsonResponse({ features: [], exceededTransferLimit: false }));
    });
    const client = createPortalViewerSdkClient({
      baseUrl: "https://api.honua.example/arcgis",
      session: { status: "unauthenticated" },
      fetchFn,
    });

    await client.queryFeatures({ serviceId: "city/parcels", layerId: 0 });

    const init = fetchFn.mock.calls[0]?.[1];
    expect(new Headers(init?.headers).get("Authorization")).toBeNull();
  });
});

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    headers: { "Content-Type": "application/json" },
    status: 200,
  });
}
