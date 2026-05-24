import { render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import {
  SessionClient,
  type SessionBootstrapResult,
} from "../sdk/session";
import { RequireCapability } from "./RequireCapability";
import { SessionProvider, useCapability, useEntitlement } from "./SessionProvider";

class FakeClient extends SessionClient {
  constructor(private readonly result: SessionBootstrapResult) {
    super({ fetchImpl: (() => Promise.reject(new Error("not used"))) as typeof fetch });
  }

  override async bootstrap(): Promise<SessionBootstrapResult> {
    return this.result;
  }
}

function ProbeCapability({ name }: { readonly name: string }): JSX.Element {
  const has = useCapability(name);
  return <span data-testid="cap">{has ? "yes" : "no"}</span>;
}

function ProbeEntitlement({ name }: { readonly name: string }): JSX.Element {
  const has = useEntitlement(name);
  return <span data-testid="ent">{has ? "yes" : "no"}</span>;
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe("SessionProvider", () => {
  it("renders authenticated user's capability bundle", async () => {
    const client = new FakeClient({
      status: {
        kind: "authenticated",
        identity: { providerKey: "u1", displayName: "Mike" },
        bundle: {
          capabilities: new Set(["catalog:read"]),
          entitlements: new Set(["sharing"]),
        },
      },
      fellBackEndpoints: [],
    });

    render(
      <SessionProvider client={client}>
        <ProbeCapability name="catalog:read" />
        <ProbeEntitlement name="sharing" />
      </SessionProvider>,
    );

    await waitFor(() => {
      expect(screen.getByTestId("cap").textContent).toBe("yes");
      expect(screen.getByTestId("ent").textContent).toBe("yes");
    });
  });

  it("gates content with RequireCapability when capability missing", async () => {
    const client = new FakeClient({
      status: {
        kind: "authenticated",
        identity: { providerKey: "u1", displayName: "Mike" },
        bundle: { capabilities: new Set(), entitlements: new Set() },
      },
      fellBackEndpoints: [],
    });

    render(
      <SessionProvider client={client}>
        <RequireCapability of="catalog:write">
          <span data-testid="ok">protected</span>
        </RequireCapability>
      </SessionProvider>,
    );

    await waitFor(() => {
      expect(screen.queryByTestId("ok")).toBeNull();
      expect(document.querySelector('[data-kind="unauthorized"]')).not.toBeNull();
    });
  });

  it("anonymous session renders the unauthorized resource state", async () => {
    const client = new FakeClient({
      status: { kind: "anonymous" },
      fellBackEndpoints: ["/api/v1/admin/auth/session"],
    });

    render(
      <SessionProvider client={client}>
        <RequireCapability of="catalog:read">
          <span data-testid="ok">protected</span>
        </RequireCapability>
      </SessionProvider>,
    );

    await waitFor(() => {
      expect(document.querySelector('[data-kind="unauthorized"]')).not.toBeNull();
    });
  });
});
