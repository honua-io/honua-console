import { useEffect, useState } from "react";

import {
  CONTENT_ITEM_PENDING,
  type MetadataV2Pending,
} from "../../sdk/content";
import { type LoadSurface } from "../../surfaces/LoadSurface";
import { emitConsoleSmoke } from "../../telemetry/smoke";

/**
 * Catalog content-item list loader. Until `honua-sdk-js#225` publishes the
 * metadata v2 projection, this hook always resolves to `pending-binding`. The
 * gap is recorded in `CONTENT_ITEM_PENDING` for telemetry and for the docs gap
 * log. No Console-local `ContentItem` shape is defined — see the rule in
 * `eslint.config.js` (`no-restricted-syntax`).
 */
export function useContentItemList(): LoadSurface<MetadataV2Pending> {
  const [surface] = useState<LoadSurface<MetadataV2Pending>>({
    status: "pending-binding",
    waitingFor: CONTENT_ITEM_PENDING.waitingFor,
  });

  useEffect(() => {
    emitConsoleSmoke({
      surface: "catalog.content-item.list",
      sdkSubpath: "content",
      status: "pending-binding",
      durationMs: 0,
      detail: { waitingFor: CONTENT_ITEM_PENDING.waitingFor },
    });
  }, []);

  return surface;
}
