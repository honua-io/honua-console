import { useEffect, useState } from "react";

import type { ProvenanceRecord } from "../../sdk/operator";
import { adaptSdkThrown } from "../../surfaces/adapt";
import { pendingBinding, type LoadSurface } from "../../surfaces/LoadSurface";
import { emitConsoleSmoke, type SmokeStatus } from "../../telemetry/smoke";

export type ProvenanceLoader = (signal: AbortSignal) => Promise<ReadonlyArray<ProvenanceRecord>>;

const PENDING_WAITING: ReadonlyArray<string> = Object.freeze(["operator-workspace"]);

/**
 * Read-only provenance list for the Operate area. Edit/approval flows belong
 * to other tickets; this only surfaces `ProvenanceRecord` (the SDK-owned type)
 * through a loader the caller controls. The Operate page wires its loader to
 * the server's provenance API or to an `OperatorWorkspace` snapshot it owns,
 * so Console never re-declares the DTO.
 */
export function useProvenance(
  loader: ProvenanceLoader | undefined,
): LoadSurface<ReadonlyArray<ProvenanceRecord>> {
  const [surface, setSurface] = useState<LoadSurface<ReadonlyArray<ProvenanceRecord>>>({
    status: "pending-binding",
    waitingFor: PENDING_WAITING,
  });

  useEffect(() => {
    if (!loader) {
      setSurface(pendingBinding(PENDING_WAITING));
      return;
    }
    const controller = new AbortController();
    const started = performance.now();
    void (async () => {
      let next: LoadSurface<ReadonlyArray<ProvenanceRecord>>;
      try {
        const records = await loader(controller.signal);
        next = { status: "ok", value: records };
      } catch (error) {
        if (controller.signal.aborted) return;
        next = adaptSdkThrown<ReadonlyArray<ProvenanceRecord>>(error);
      }
      if (controller.signal.aborted) return;
      setSurface(next);
      emitConsoleSmoke({
        surface: "operate.provenance.load",
        sdkSubpath: "operator/workspace",
        status: next.status as SmokeStatus,
        durationMs: Math.round(performance.now() - started),
      });
    })();
    return () => controller.abort();
  }, [loader]);

  return surface;
}
