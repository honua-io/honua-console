import { useEffect, useState } from "react";

import {
  previewGeneratedApp,
  type HonuaGeneratedAppLoadOptions,
  type HonuaGeneratedAppPreviewInput,
  type HonuaGeneratedAppPreviewResult,
} from "../../sdk/generated-app";
import { adaptSdkThrown } from "../../surfaces/adapt";
import { type LoadSurface } from "../../surfaces/LoadSurface";
import { emitConsoleSmoke, type SmokeStatus } from "../../telemetry/smoke";

const PENDING_WAITING: ReadonlyArray<string> = Object.freeze(["generated-app.preview"]);

export function useGeneratedAppPreview(
  input: HonuaGeneratedAppPreviewInput | undefined,
  options: HonuaGeneratedAppLoadOptions | undefined,
): LoadSurface<HonuaGeneratedAppPreviewResult> {
  const [surface, setSurface] = useState<LoadSurface<HonuaGeneratedAppPreviewResult>>({
    status: "pending-binding",
    waitingFor: PENDING_WAITING,
  });

  useEffect(() => {
    if (!input || !options) return;
    let cancelled = false;
    const started = performance.now();
    void (async () => {
      let next: LoadSurface<HonuaGeneratedAppPreviewResult>;
      try {
        const result = await previewGeneratedApp(input, options);
        next = { status: "ok", value: result };
      } catch (error) {
        next = adaptSdkThrown<HonuaGeneratedAppPreviewResult>(error);
      }
      if (cancelled) return;
      setSurface(next);
      emitConsoleSmoke({
        surface: "studio.generated-app.preview",
        sdkSubpath: "generated-app",
        status: next.status as SmokeStatus,
        durationMs: Math.round(performance.now() - started),
      });
    })();
    return () => {
      cancelled = true;
    };
  }, [input, options]);

  return surface;
}
