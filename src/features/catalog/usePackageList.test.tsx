import { renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import type { HonuaControlPlaneClient } from "../../sdk/control-plane";
import { usePackageList } from "./usePackageList";

interface HookProps {
  readonly controlPlane: HonuaControlPlaneClient | undefined;
}

describe("usePackageList", () => {
  it("resets to pending-binding when the control-plane client disappears", async () => {
    const controlPlane = {
      requestPage: vi.fn(async () => ({
        supported: true,
        value: {
          items: [{ id: "pkg-1", title: "Package 1" }],
          pagination: {},
        },
      })),
    } as unknown as HonuaControlPlaneClient;

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
  });
});
