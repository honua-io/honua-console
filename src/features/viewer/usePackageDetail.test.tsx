import { renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import type { HonuaControlPlaneClient } from "../../sdk/control-plane";
import { usePackageDetail } from "./usePackageDetail";

interface HookProps {
  readonly controlPlane: HonuaControlPlaneClient | undefined;
  readonly packageId: string | undefined;
}

describe("usePackageDetail", () => {
  it("resets to pending-binding when the package id disappears", async () => {
    const controlPlane = {
      requestJson: vi.fn(async () => ({
        supported: true,
        value: { id: "pkg-1", title: "Package 1" },
      })),
    } as unknown as HonuaControlPlaneClient;

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
  });
});
