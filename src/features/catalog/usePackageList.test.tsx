import { renderHook, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { HonuaControlPlaneClient } from "../../sdk/control-plane";
import { addConsoleSmokeListener, type ConsoleSmokeEvent } from "../../telemetry/smoke";
import { usePackageList } from "./usePackageList";

interface HookProps {
  readonly controlPlane: HonuaControlPlaneClient | undefined;
}

const cleanups: Array<() => void> = [];

afterEach(() => {
  while (cleanups.length) cleanups.pop()?.();
});

describe("usePackageList", () => {
  it("resets to pending-binding and emits smoke when the control-plane client disappears", async () => {
    const controlPlane = {
      requestPage: vi.fn(async () => ({
        supported: true,
        value: {
          items: [{ id: "pkg-1", title: "Package 1" }],
          pagination: {},
        },
      })),
    } as unknown as HonuaControlPlaneClient;

    const events: ConsoleSmokeEvent[] = [];
    cleanups.push(addConsoleSmokeListener((event) => events.push(event)));

    const initialProps: HookProps = { controlPlane };
    const { result, rerender } = renderHook(
      ({ controlPlane: nextControlPlane }: HookProps) => usePackageList(nextControlPlane),
      { initialProps },
    );

    await waitFor(() => {
      expect(result.current.status).toBe("ok");
    });

    rerender({ controlPlane: undefined });

    await waitFor(() => {
      expect(result.current.status).toBe("pending-binding");
    });
    expect(events).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          surface: "viewer.map-package.list",
          sdkSubpath: "control-plane",
          status: "pending-binding",
          detail: expect.objectContaining({ waitingFor: ["honua-control-plane"] }),
        }),
      ]),
    );
  });
});
