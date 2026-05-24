import { afterEach, describe, expect, it } from "vitest";

import { addConsoleSmokeListener, emitConsoleSmoke } from "./smoke";

const cleanups: Array<() => void> = [];

afterEach(() => {
  while (cleanups.length) cleanups.pop()?.();
});

describe("emitConsoleSmoke", () => {
  it("delivers event to local listeners with timestamp and shape", () => {
    const received: unknown[] = [];
    cleanups.push(addConsoleSmokeListener((event) => received.push(event)));

    emitConsoleSmoke({
      surface: "viewer.map-package.detail",
      sdkSubpath: "control-plane",
      status: "ok",
      durationMs: 12,
      detail: { packageId: "p1" },
    });

    expect(received).toHaveLength(1);
    const event = received[0] as Record<string, unknown>;
    expect(event.surface).toBe("viewer.map-package.detail");
    expect(event.sdkSubpath).toBe("control-plane");
    expect(event.status).toBe("ok");
    expect(typeof event.at).toBe("string");
  });

  it("dispatches the window custom event for portal-style smoke bus", () => {
    const received: unknown[] = [];
    const handler = (event: Event): void => {
      received.push((event as CustomEvent).detail);
    };
    window.addEventListener("honua:console-smoke", handler);
    cleanups.push(() => window.removeEventListener("honua:console-smoke", handler));

    emitConsoleSmoke({
      surface: "share.policy.mutate",
      sdkSubpath: "control-plane",
      status: "ok",
      durationMs: 5,
    });

    expect(received).toHaveLength(1);
  });
});
