import { renderHook, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { addConsoleSmokeListener } from "../../telemetry/smoke";
import { useProvenance, type ProvenanceLoader } from "./useProvenance";

interface HookProps {
  readonly loader: ProvenanceLoader | undefined;
}

const cleanups: Array<() => void> = [];

afterEach(() => {
  while (cleanups.length) cleanups.pop()?.();
});

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

  it("emits pending-binding smoke when the provenance loader is absent", async () => {
    const events: unknown[] = [];
    cleanups.push(addConsoleSmokeListener((event) => events.push(event)));

    renderHook(() => useProvenance(undefined));

    await waitFor(() => {
      expect(events).toEqual([
        expect.objectContaining({
          surface: "operate.provenance.load",
          sdkSubpath: "operator/workspace",
          status: "pending-binding",
          detail: expect.objectContaining({ waitingFor: ["operator-workspace"] }),
        }),
      ]);
    });
  });
});
