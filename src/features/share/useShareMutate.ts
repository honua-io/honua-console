import { useCallback } from "react";

import { HonuaSharingClient } from "../../sdk/sharing";
import type { HonuaControlPlaneClient } from "../../sdk/control-plane";
import type { HonuaShareRequest, HonuaShareResponse } from "../../sdk/sharing";
import { adaptControlPlaneResult, adaptSdkThrown } from "../../surfaces/adapt";
import { type LoadSurface } from "../../surfaces/LoadSurface";
import { emitConsoleSmoke, type SmokeStatus } from "../../telemetry/smoke";

export function useShareMutate(
  controlPlane: HonuaControlPlaneClient | undefined,
): (mapId: string, request: HonuaShareRequest) => Promise<LoadSurface<HonuaShareResponse>> {
  return useCallback(
    async (mapId, request) => {
      if (!controlPlane) {
        return { status: "pending-binding", waitingFor: ["honua-control-plane"] };
      }
      const started = performance.now();
      const client = new HonuaSharingClient(controlPlane);
      let next: LoadSurface<HonuaShareResponse>;
      try {
        const result = await client.updateMapSharing(mapId, request);
        next = result.supported
          ? { status: "ok", value: result.value }
          : (adaptControlPlaneResult(result) as LoadSurface<HonuaShareResponse>);
      } catch (error) {
        next = adaptSdkThrown<HonuaShareResponse>(error);
      }
      emitConsoleSmoke({
        surface: "share.policy.mutate",
        sdkSubpath: "control-plane",
        status: next.status as SmokeStatus,
        durationMs: Math.round(performance.now() - started),
        detail: { mapId },
      });
      return next;
    },
    [controlPlane],
  );
}
