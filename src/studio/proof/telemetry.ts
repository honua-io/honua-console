export type StudioProofTelemetryName =
  | "studio.proof.opened"
  | "studio.proof.fixture-blocked"
  | "studio.proof.prompt-submitted"
  | "studio.proof.clarification-needed"
  | "studio.proof.clarification-submitted"
  | "studio.proof.plan-ready"
  | "studio.proof.apply-started"
  | "studio.proof.apply-progress"
  | "studio.proof.preview-ready"
  | "studio.proof.edit-committed"
  | "studio.proof.edit-blocked"
  | "studio.proof.error"
  | "sdk.before"
  | "sdk.after"
  | "sdk.error";

export interface StudioProofTelemetryEvent {
  readonly name: StudioProofTelemetryName;
  readonly at: string;
  readonly sourceKind?: string;
  readonly itemId?: string;
  readonly fixture?: string;
  readonly detail?: Readonly<Record<string, unknown>>;
}

/**
 * Studio uses the legacy `honua:app-builder-proof` window event so the
 * model-free smoke harness ported from Portal can match without translation.
 * The telemetry name namespace is the new Console-side `studio.proof.*` value.
 */
export const STUDIO_PROOF_EVENT = "honua:app-builder-proof";

export function emitStudioProofTelemetry(
  input: Omit<StudioProofTelemetryEvent, "at">,
): StudioProofTelemetryEvent {
  const event: StudioProofTelemetryEvent = {
    ...input,
    at: new Date().toISOString(),
  };
  if (typeof window !== "undefined") {
    window.dispatchEvent(new CustomEvent(STUDIO_PROOF_EVENT, { detail: event }));
  }
  return event;
}
