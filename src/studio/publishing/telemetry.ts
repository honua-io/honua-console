import type { PublishProblemKind, StudioPublishTarget } from "./types.js";

export type StudioPublishTelemetryName =
  | "publish.review.opened"
  | "publish.submitted"
  | "publish.succeeded"
  | "publish.failed"
  | "publish.reopen.completed";

export interface StudioPublishTelemetryEvent {
  readonly name: StudioPublishTelemetryName;
  readonly draftId?: string;
  readonly itemId?: string;
  readonly target?: StudioPublishTarget;
  readonly problemKind?: PublishProblemKind;
  readonly at: string;
}

export const STUDIO_PUBLISH_TELEMETRY_EVENT = "honua:studio-publish";

export function emitStudioPublishTelemetry(
  input: Omit<StudioPublishTelemetryEvent, "at">
): StudioPublishTelemetryEvent {
  const event: StudioPublishTelemetryEvent = {
    ...input,
    at: new Date().toISOString()
  };

  if (typeof window !== "undefined") {
    window.dispatchEvent(new CustomEvent(STUDIO_PUBLISH_TELEMETRY_EVENT, { detail: event }));
  }

  return event;
}
