import { render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";

import { SessionClient, type SessionBootstrapResult } from "../sdk/session";
import { SessionProvider } from "../session/SessionProvider";
import { addConsoleSmokeListener } from "../telemetry/smoke";
import { SharePage } from "./SharePage";

class FakeClient extends SessionClient {
  constructor(private readonly result: SessionBootstrapResult) {
    super({ fetchImpl: (() => Promise.reject(new Error("not used"))) as typeof fetch });
  }

  override async bootstrap(): Promise<SessionBootstrapResult> {
    return this.result;
  }
}

const cleanups: Array<() => void> = [];

afterEach(() => {
  while (cleanups.length) cleanups.pop()?.();
});

describe("SharePage", () => {
  it("allows sharing:read capability without a separate sharing entitlement", async () => {
    const events: unknown[] = [];
    cleanups.push(addConsoleSmokeListener((event) => events.push(event)));
    const client = new FakeClient({
      status: {
        kind: "authenticated",
        identity: { providerKey: "u1", displayName: "Share User" },
        bundle: {
          capabilities: new Set(["sharing:read"]),
          entitlements: new Set(),
        },
      },
      fellBackEndpoints: [],
    });

    render(
      <SessionProvider client={client}>
        <SharePage />
      </SessionProvider>,
    );

    await waitFor(() => {
      expect(screen.getByRole("heading", { name: "Sharing policies" })).toBeInTheDocument();
      expect(document.querySelector('[data-kind="pending-binding"]')).not.toBeNull();
      expect(document.querySelector('[data-kind="unauthorized"]')).toBeNull();
    });
    await waitFor(() => {
      expect(events).toEqual(
        expect.arrayContaining([
          expect.objectContaining({
            surface: "share.policy.load",
            sdkSubpath: "control-plane",
            status: "pending-binding",
            detail: expect.objectContaining({ waitingFor: ["honua-sdk-js#225"] }),
          }),
        ]),
      );
    });
  });
});
