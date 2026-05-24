import { useEffect, useState } from "react";

import {
  HonuaSavedMapCollaborationClient,
  type HonuaSavedMapCollaborationSession,
  type HonuaSavedMapCollaborationClientOptions,
  type SavedMapCollaborationJoinRequest,
} from "../../sdk/collaboration";
import { adaptSdkThrown } from "../../surfaces/adapt";
import { pendingBinding, type LoadSurface } from "../../surfaces/LoadSurface";
import { emitConsoleSmoke, type SmokeStatus } from "../../telemetry/smoke";

const PENDING_WAITING: ReadonlyArray<string> = Object.freeze(["collaboration-transport"]);

export function useCollaborationSession<TPayload = unknown>(
  options: HonuaSavedMapCollaborationClientOptions<TPayload> | undefined,
  join: SavedMapCollaborationJoinRequest | undefined,
): LoadSurface<HonuaSavedMapCollaborationSession<TPayload>> {
  const [surface, setSurface] = useState<LoadSurface<HonuaSavedMapCollaborationSession<TPayload>>>({
    status: "pending-binding",
    waitingFor: PENDING_WAITING,
  });

  useEffect(() => {
    if (!options || !join) {
      setSurface(pendingBinding(PENDING_WAITING));
      return;
    }
    let cancelled = false;
    let live: HonuaSavedMapCollaborationSession<TPayload> | undefined;
    const started = performance.now();
    const client = new HonuaSavedMapCollaborationClient<TPayload>(options);
    void (async () => {
      let next: LoadSurface<HonuaSavedMapCollaborationSession<TPayload>>;
      try {
        const session = await client.joinSavedMap(join);
        live = session;
        next = { status: "ok", value: session };
      } catch (error) {
        next = adaptSdkThrown<HonuaSavedMapCollaborationSession<TPayload>>(error);
      }
      if (cancelled) {
        live?.disconnect();
        return;
      }
      setSurface(next);
      emitConsoleSmoke({
        surface: "collaboration.session.join",
        sdkSubpath: "collaboration",
        status: next.status as SmokeStatus,
        durationMs: Math.round(performance.now() - started),
        detail: { mapId: join.mapId },
      });
    })();
    return () => {
      cancelled = true;
      live?.disconnect();
    };
  }, [options, join]);

  return surface;
}
