// Legacy-admin adapter: produces the publish-handoff event that the
// transitional `honua-server-admin` operator surface emits when it
// publishes a service for Console catalog consumption. Until the
// "Operate" surface in Console fully replaces the legacy admin publish
// path (tracked by honua-console#6 and the larger honua-portal#11
// publish-handoff slice), the legacy admin is the producer of these
// events. Tagging this step with `legacy-admin` ensures a failure
// here points the smoke triage at the right transition surface.

import { readFile } from "node:fs/promises";
import { resolve } from "node:path";

const REQUIRED_EVENT_FIELDS = [
  "sourceServiceId",
  "eventKind",
  "serviceUrl",
  "serviceType",
  "status",
  "owner",
  "metadata",
  "actor",
];

export class PublishEventError extends Error {
  constructor(message, { reason }) {
    super(message);
    this.name = "PublishEventError";
    this.reason = reason;
  }
}

export async function loadPublishEvent({ repoRoot, fixturePath } = {}) {
  if (!repoRoot) throw new Error("loadPublishEvent requires repoRoot");
  const path = fixturePath ?? resolve(repoRoot, "smoke/parity/fixtures/publish-event.json");
  const raw = await readFile(path, "utf8");
  const event = JSON.parse(raw);
  validatePublishEvent(event, path);
  return event;
}

export function validatePublishEvent(event, source) {
  const missing = REQUIRED_EVENT_FIELDS.filter((f) => !(f in event));
  if (missing.length > 0) {
    throw new PublishEventError(`publish-handoff event from ${source} missing fields: ${missing.join(", ")}`, {
      reason: "missing-fields",
    });
  }
  if (!["publish", "republish", "metadataUpdate", "statusChange"].includes(event.eventKind)) {
    throw new PublishEventError(`publish-handoff event from ${source} has invalid eventKind "${event.eventKind}"`, {
      reason: "invalid-event-kind",
    });
  }
  if (!event.metadata || typeof event.metadata.title !== "string" || event.metadata.title.length === 0) {
    throw new PublishEventError(`publish-handoff event from ${source} requires non-empty metadata.title`, {
      reason: "invalid-metadata-title",
    });
  }
}
