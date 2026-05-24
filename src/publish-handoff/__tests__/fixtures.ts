import { createDeterministicContentItemIdGenerator } from "../../contracts/ids.js";
import type { AdminServiceStatus, PublishHandoffEvent, ServiceType } from "../types.js";

export interface PublishEventOverrides {
  sourceServiceId?: string;
  eventKind?: PublishHandoffEvent["eventKind"];
  serviceUrl?: string;
  serviceType?: ServiceType;
  importJobId?: string | null;
  status?: AdminServiceStatus;
  statusReason?: string | null;
  adminDiagnosticsRef?: string | null;
  lastCheckedAt?: string | null;
  ownerId?: string;
  ownerKind?: PublishHandoffEvent["owner"]["kind"];
  ownerDisplayName?: string;
  title?: string;
  summary?: string | null;
  tags?: string[];
  extent?: PublishHandoffEvent["metadata"]["extent"];
  actor?: string;
}

export function makePublishEvent(overrides: PublishEventOverrides = {}): PublishHandoffEvent {
  return {
    sourceServiceId: overrides.sourceServiceId ?? "svc-source-1",
    eventKind: overrides.eventKind ?? "publish",
    serviceUrl: overrides.serviceUrl ?? "https://maps.honua.io/services/census/v1",
    serviceType: overrides.serviceType ?? "feature",
    importJobId: overrides.importJobId === undefined ? null : overrides.importJobId,
    status: overrides.status ?? "ok",
    statusReason: overrides.statusReason === undefined ? null : overrides.statusReason,
    adminDiagnosticsRef:
      overrides.adminDiagnosticsRef === undefined ? "diag-svc-source-1" : overrides.adminDiagnosticsRef,
    ...(overrides.lastCheckedAt !== undefined ? { lastCheckedAt: overrides.lastCheckedAt } : {}),
    owner: {
      id: overrides.ownerId ?? "user-operator-1",
      kind: overrides.ownerKind ?? "user",
      ...(overrides.ownerDisplayName !== undefined
        ? { displayName: overrides.ownerDisplayName }
        : { displayName: "Operator One" }),
    },
    metadata: {
      title: overrides.title ?? "Census Tracts 2026",
      summary: overrides.summary === undefined ? "U.S. Census Bureau tract polygons." : overrides.summary,
      tags: overrides.tags ?? ["census", "polygons"],
      extent: overrides.extent === undefined ? { bbox: [-180, -90, 180, 90], crs: "EPSG:4326" } : overrides.extent,
    },
    actor: overrides.actor ?? "user-operator-1",
  };
}

export function deterministicNow(start = "2026-05-06T12:00:00.000Z"): () => Date {
  let ms = new Date(start).getTime();
  return () => {
    const date = new Date(ms);
    ms += 1; // monotonic
    return date;
  };
}

export function deterministicIdGenerator(prefix = "svc"): () => string {
  return createDeterministicContentItemIdGenerator(prefix);
}
