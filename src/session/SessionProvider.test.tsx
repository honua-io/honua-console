import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import {
  SessionClient,
  type SessionBootstrapResult,
} from "../sdk/session";
import { RequireCapability } from "./RequireCapability";
import { SessionProvider, useCapability, useEntitlement, useSession } from "./SessionProvider";

const env = import.meta.env as Record<string, string | undefined>;
const originalHonuaBaseUrl = env.VITE_HONUA_BASE_URL;

class FakeClient extends SessionClient {
  constructor(private readonly result: SessionBootstrapResult) {
    super({ fetchImpl: (() => Promise.reject(new Error("not used"))) as typeof fetch });
  }

  override async bootstrap(): Promise<SessionBootstrapResult> {
    return this.result;
  }
}

class DeferredClient extends SessionClient {
  private readonly queue: Array<Promise<SessionBootstrapResult>>;
  callCount = 0;

  constructor(queue: Array<Promise<SessionBootstrapResult>>) {
    super({ fetchImpl: (() => Promise.reject(new Error("not used"))) as typeof fetch });
    this.queue = queue;
  }

  override async bootstrap(): Promise<SessionBootstrapResult> {
    const next = this.queue[this.callCount];
    this.callCount += 1;
    if (!next) throw new Error("unexpected bootstrap");
    return next;
  }
}

function deferred<T>(): { readonly promise: Promise<T>; readonly resolve: (value: T) => void } {
  let resolve: (value: T) => void = () => undefined;
  const promise = new Promise<T>((innerResolve) => {
    resolve = innerResolve;
  });
  return { promise, resolve };
}

function ProbeCapability({ name }: { readonly name: string }): JSX.Element {
  const has = useCapability(name);
  return <span data-testid="cap">{has ? "yes" : "no"}</span>;
}

function ProbeEntitlement({ name }: { readonly name: string }): JSX.Element {
  const has = useEntitlement(name);
  return <span data-testid="ent">{has ? "yes" : "no"}</span>;
}

function ProbeStatusWithRefresh(): JSX.Element {
  const { refresh, status } = useSession();
  const label = status.kind === "authenticated" ? status.identity.providerKey : status.kind;
  return (
    <>
      <button type="button" onClick={() => void refresh()}>
        refresh
      </button>
      <span data-testid="session-status">{label}</span>
    </>
  );
}

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  env.VITE_HONUA_BASE_URL = originalHonuaBaseUrl;
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

  it("uses the configured Honua server origin when it owns the client", async () => {
    env.VITE_HONUA_BASE_URL = "https://api.honua.example";
    const fetchImpl = vi.fn(async () => new Response("", { status: 401 }));
    vi.stubGlobal("fetch", fetchImpl);

    render(
      <SessionProvider>
        <RequireCapability of="catalog:read">
          <span data-testid="ok">protected</span>
        </RequireCapability>
      </SessionProvider>,
    );

    await waitFor(() => {
      expect(fetchImpl).toHaveBeenCalledWith(
        "https://api.honua.example/api/v1/admin/auth/session",
        expect.objectContaining({ credentials: "include" }),
      );
    });
    expect(fetchImpl).toHaveBeenCalledWith(
      "https://api.honua.example/api/v1/admin/license/entitlements",
      expect.objectContaining({ credentials: "include" }),
    );
  });

  it("does not let an older bootstrap overwrite a newer refresh result", async () => {
    const first = deferred<SessionBootstrapResult>();
    const second = deferred<SessionBootstrapResult>();
    const client = new DeferredClient([first.promise, second.promise]);

    render(
      <SessionProvider client={client}>
        <ProbeStatusWithRefresh />
      </SessionProvider>,
    );

    await waitFor(() => {
      expect(client.callCount).toBe(1);
    });
    fireEvent.click(screen.getByRole("button", { name: "refresh" }));
    await waitFor(() => {
      expect(client.callCount).toBe(2);
    });

    second.resolve({
      status: {
        kind: "authenticated",
        identity: { providerKey: "new-session", displayName: "New Session" },
        bundle: { capabilities: new Set(), entitlements: new Set() },
      },
      fellBackEndpoints: [],
    });
    await waitFor(() => {
      expect(screen.getByTestId("session-status").textContent).toBe("new-session");
    });

    first.resolve({ status: { kind: "anonymous" }, fellBackEndpoints: [] });
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(screen.getByTestId("session-status").textContent).toBe("new-session");
  });
});
