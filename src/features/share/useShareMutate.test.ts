import { renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";

import { addConsoleSmokeListener, type ConsoleSmokeEvent } from "../../telemetry/smoke";
import { useShareMutate } from "./useShareMutate";

const cleanups: Array<() => void> = [];

afterEach(() => {
  while (cleanups.length) cleanups.pop()?.();
});

describe("useShareMutate", () => {
  it("emits a pending-binding smoke event when invoked without a control-plane client", async () => {
    const events: ConsoleSmokeEvent[] = [];
    cleanups.push(addConsoleSmokeListener((event) => events.push(event)));

    const { result } = renderHook(() => useShareMutate(undefined));

    const outcome = await result.current("map-7", { visibility: "public" } as never);

    expect(outcome.status).toBe("pending-binding");
    expect(events).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          surface: "share.policy.mutate",
          sdkSubpath: "control-plane",
          status: "pending-binding",
          detail: expect.objectContaining({
            waitingFor: ["honua-control-plane"],
            mapId: "map-7",
          }),
        }),
      ]),
    );
  });
});
