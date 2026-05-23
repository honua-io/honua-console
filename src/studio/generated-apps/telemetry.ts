export const GENERATED_APP_LIFECYCLE_EVENT = "honua:generated-app-lifecycle" as const;

export interface GeneratedAppLifecycleTelemetryEvent {
  readonly name:
    | "studio.generated-app.preview-opened"
    | "studio.generated-app.rollback-started"
    | "studio.generated-app.rollback-completed"
    | "studio.generated-app.rollback-failed";
  readonly itemId: string;
  readonly revisionId?: string;
  readonly detail?: Readonly<Record<string, unknown>>;
}

export function emitGeneratedAppLifecycleTelemetry(event: GeneratedAppLifecycleTelemetryEvent): void {
  if (typeof window === "undefined") return;
  window.dispatchEvent(new CustomEvent(GENERATED_APP_LIFECYCLE_EVENT, { detail: event }));
}
