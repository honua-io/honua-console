import { render, waitFor } from "@testing-library/react";
import { useEffect } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { ControlPlaneProvider, useControlPlane } from "./ControlPlaneProvider";

function RawProbe(): null {
  const controlPlane = useControlPlane();
  useEffect(() => {
    void controlPlane.raw({ path: "/packages", method: "GET" });
  }, [controlPlane]);
  return null;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("ControlPlaneProvider", () => {
  it("includes browser credentials for configured Honua origins", async () => {
    const fetchImpl = vi.fn(async () => new Response(JSON.stringify({ ok: true })));
    vi.stubGlobal("fetch", fetchImpl);

    render(
      <ControlPlaneProvider baseUrl="https://api.honua.example">
        <RawProbe />
      </ControlPlaneProvider>,
    );

    await waitFor(() => {
      expect(fetchImpl).toHaveBeenCalledWith(
        "https://api.honua.example/api/v1/admin/packages",
        expect.objectContaining({ credentials: "include" }),
      );
    });
  });
});
