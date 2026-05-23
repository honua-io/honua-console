// Legacy-admin adapter: produces the publish-handoff payload that the
// transitional `honua-server-admin` operator surface emits when it
// publishes a service for Console catalog consumption. Until the
// "Operate" surface in Console fully replaces the legacy admin publish
// path (tracked by honua-console#6 and the larger honua-portal#11
// publish-handoff slice), the legacy admin is the producer of these
// handoffs. Tagging this step with `legacy-admin` ensures a failure
// here points the smoke triage at the right transition surface.
//
// The handoff fixture mirrors the canonical publish-handoff/v1.1.0
// schema at /home/makani/honua-portal/schemas/publish-handoff-v1.json so
// the smoke exercises the same wire shape honua-server actually accepts.

import { readFile } from "node:fs/promises";
import { resolve } from "node:path";

// Mirrors the top-level required[] in publish-handoff-v1.json. Kept in
// sync with the canonical schema; a contract test asserts this list and
// the fixture stay aligned.
export const PUBLISH_HANDOFF_REQUIRED_FIELDS = Object.freeze([
  "type",
  "title",
  "summary",
  "owner",
  "extent",
  "nativeCrs",
  "license",
  "attribution",
  "source",
  "target",
  "endpoints",
  "preview",
  "capabilities",
  "dependencies",
  "access",
]);

const SOURCE_REQUIRED = Object.freeze(["kind", "sourceId", "jobId", "publishedBy"]);
const ENDPOINT_SLOTS = Object.freeze(["geoservices", "ogcFeatures", "stac", "tiles"]);
const VALID_SOURCE_KINDS = Object.freeze(["import", "publish", "admin-job", "external"]);
const VALID_ITEM_TYPES = Object.freeze(["service", "layer", "map", "scene", "app", "document", "external-url"]);

export class PublishHandoffError extends Error {
  constructor(message, { reason }) {
    super(message);
    this.name = "PublishHandoffError";
    this.reason = reason;
  }
}

export async function loadPublishHandoff({ repoRoot, fixturePath } = {}) {
  if (!repoRoot) throw new Error("loadPublishHandoff requires repoRoot");
  const path = fixturePath ?? resolve(repoRoot, "smoke/parity/fixtures/publish-handoff.json");
  const raw = await readFile(path, "utf8");
  const handoff = JSON.parse(raw);
  validatePublishHandoff(handoff, path);
  return handoff;
}

export function validatePublishHandoff(handoff, source) {
  const missing = PUBLISH_HANDOFF_REQUIRED_FIELDS.filter((f) => !(f in handoff));
  if (missing.length > 0) {
    throw new PublishHandoffError(
      `publish-handoff from ${source} missing required fields: ${missing.join(", ")}`,
      { reason: "missing-fields" },
    );
  }
  if (!VALID_ITEM_TYPES.includes(handoff.type)) {
    throw new PublishHandoffError(
      `publish-handoff from ${source} has invalid type "${handoff.type}"; allowed: ${VALID_ITEM_TYPES.join(", ")}`,
      { reason: "invalid-type" },
    );
  }
  if (typeof handoff.title !== "string" || handoff.title.length === 0) {
    throw new PublishHandoffError(`publish-handoff from ${source} requires non-empty title`, {
      reason: "invalid-title",
    });
  }
  if (typeof handoff.summary !== "string" || handoff.summary.length === 0) {
    throw new PublishHandoffError(`publish-handoff from ${source} requires non-empty summary`, {
      reason: "invalid-summary",
    });
  }
  if (!handoff.source || typeof handoff.source !== "object") {
    throw new PublishHandoffError(`publish-handoff from ${source} requires source object`, {
      reason: "missing-source",
    });
  }
  const sourceMissing = SOURCE_REQUIRED.filter((f) => !(f in handoff.source));
  if (sourceMissing.length > 0) {
    throw new PublishHandoffError(
      `publish-handoff from ${source} source missing fields: ${sourceMissing.join(", ")}`,
      { reason: "missing-source-fields" },
    );
  }
  if (!VALID_SOURCE_KINDS.includes(handoff.source.kind)) {
    throw new PublishHandoffError(
      `publish-handoff from ${source} has invalid source.kind "${handoff.source.kind}"`,
      { reason: "invalid-source-kind" },
    );
  }
  if (!handoff.target || handoff.target.type !== handoff.type) {
    throw new PublishHandoffError(
      `publish-handoff from ${source} target.type "${handoff.target?.type}" must equal item type "${handoff.type}"`,
      { reason: "target-type-mismatch" },
    );
  }
  if (!handoff.endpoints || typeof handoff.endpoints !== "object") {
    throw new PublishHandoffError(`publish-handoff from ${source} requires endpoints object`, {
      reason: "missing-endpoints",
    });
  }
  const slotsMissing = ENDPOINT_SLOTS.filter((slot) => !(slot in handoff.endpoints));
  if (slotsMissing.length > 0) {
    throw new PublishHandoffError(
      `publish-handoff from ${source} endpoints missing slots: ${slotsMissing.join(", ")}`,
      { reason: "missing-endpoint-slots" },
    );
  }
  if (!handoff.preview || !("thumbnail" in handoff.preview) || !("image" in handoff.preview)) {
    throw new PublishHandoffError(`publish-handoff from ${source} requires preview { thumbnail, image }`, {
      reason: "missing-preview",
    });
  }
  if (!handoff.access || typeof handoff.access.sharing !== "string" || typeof handoff.access.embeddable !== "boolean") {
    throw new PublishHandoffError(`publish-handoff from ${source} requires access { sharing, embeddable, openData }`, {
      reason: "invalid-access",
    });
  }
}
