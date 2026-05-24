import { renderHook, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { HonuaControlPlaneClient } from "../../sdk/control-plane";
import { addConsoleSmokeListener, type ConsoleSmokeEvent } from "../../telemetry/smoke";
import { usePackageDetail } from "./usePackageDetail";

interface HookProps {
  readonly controlPlane: HonuaControlPlaneClient | undefined;
  readonly packageId: string | undefined;
}

const cleanups: Array<() => void> = [];

afterEach(() => {
  while (cleanups.length) cleanups.pop()?.();
});

describe("usePackageDetail", () => {
  it("resets to pending-binding and emits smoke when the package id disappears", async () => {
    const controlPlane = {
      requestJson: vi.fn(async () => ({
        supported: true,
        value: { id: "pkg-1", title: "Package 1" },
      })),
    } as unknown as HonuaControlPlaneClient;

    const events: ConsoleSmokeEvent[] = [];
    cleanups.push(addConsoleSmokeListener((event) => events.push(event)));

    const initialProps: HookProps = { controlPlane, packageId: "pkg-1" };
    const { result, rerender } = renderHook(
      ({ controlPlane: nextControlPlane, packageId }: HookProps) =>
        usePackageDetail(nextControlPlane, packageId),
      { initialProps },
    );

    await waitFor(() => {
      expect(result.current.status).toBe("ok");
    });

    rerender({ controlPlane, packageId: undefined });

    await waitFor(() => {
      expect(result.current.status).toBe("pending-binding");
    });
    expect(events).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          surface: "viewer.map-package.detail",
          sdkSubpath: "control-plane",
          status: "pending-binding",
          detail: expect.objectContaining({ waitingFor: ["honua-control-plane"] }),
        }),
      ]),
    );
  });
});
