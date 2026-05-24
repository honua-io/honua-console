import { useEffect, useState } from "react";

import { HonuaMapPackagesClient, type HonuaControlPlaneClient } from "../../sdk/control-plane";
import type { HonuaMapPackageSummary } from "../../sdk/packages";
import { adaptControlPlaneResult, adaptSdkThrown } from "../../surfaces/adapt";
import { pendingBinding, type LoadSurface } from "../../surfaces/LoadSurface";
import { emitConsoleSmoke, type SmokeStatus } from "../../telemetry/smoke";

const PENDING_WAITING: ReadonlyArray<string> = Object.freeze(["honua-control-plane"]);

export function usePackageList(
  controlPlane: HonuaControlPlaneClient | undefined,
): LoadSurface<ReadonlyArray<HonuaMapPackageSummary>> {
  const [surface, setSurface] = useState<LoadSurface<ReadonlyArray<HonuaMapPackageSummary>>>({
    status: "pending-binding",
    waitingFor: PENDING_WAITING,
  });

  useEffect(() => {
    if (!controlPlane) {
      setSurface(pendingBinding(PENDING_WAITING));
      return;
    }
    let cancelled = false;
    const started = performance.now();
    const client = new HonuaMapPackagesClient(controlPlane);
    void (async () => {
      let next: LoadSurface<ReadonlyArray<HonuaMapPackageSummary>>;
      try {
        const result = await client.list();
        next = result.supported
          ? { status: "ok", value: result.value.items as ReadonlyArray<HonuaMapPackageSummary> }
          : (adaptControlPlaneResult(result) as LoadSurface<ReadonlyArray<HonuaMapPackageSummary>>);
      } catch (error) {
        next = adaptSdkThrown<ReadonlyArray<HonuaMapPackageSummary>>(error);
      }
      if (cancelled) return;
      setSurface(next);
      emitConsoleSmoke({
        surface: "viewer.map-package.list",
        sdkSubpath: "control-plane",
        status: next.status as SmokeStatus,
        durationMs: Math.round(performance.now() - started),
      });
    })();
    return () => {
      cancelled = true;
    };
  }, [controlPlane]);

  return surface;
}
