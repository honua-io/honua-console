import { renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { useProvenance, type ProvenanceLoader } from "./useProvenance";

interface HookProps {
  readonly loader: ProvenanceLoader | undefined;
}

describe("useProvenance", () => {
  it("resets to pending-binding when the provenance loader disappears", async () => {
    const loader = vi.fn(async () => [{ step: "publish", startedAt: 1 }]);

    const initialProps: HookProps = { loader };
    const { result, rerender } = renderHook(
      ({ loader: nextLoader }: HookProps) => useProvenance(nextLoader),
      { initialProps },
    );

    await waitFor(() => {
      expect(result.current.status).toBe("ok");
    });

    rerender({ loader: undefined });

    await waitFor(() => {
      expect(result.current.status).toBe("pending-binding");
    });
  });
});
