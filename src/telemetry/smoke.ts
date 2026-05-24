/**
 * Console smoke telemetry emitter.
 *
 * Each SDK-backed loader emits one event on first non-cache resolution:
 *
 *   { surface, sdkSubpath, status, durationMs, detail? }
 *
 * Events ride a `CustomEvent` on `window` (`honua:console-smoke`) so existing
 * Portal-style smoke listeners can reuse the same shape until a shared smoke
 * bus ships. The detail object is intentionally narrow so dashboard parity
 * with Portal stays mechanical.
 *
 * Tracked surfaces (smoke coverage required by the project constraint):
 *   - session.bootstrap
 *   - catalog.content-item.{list, detail}
 *   - viewer.map-package.{list, detail}
 *   - studio.generated-app.preview
 *   - share.policy.{load, mutate}
 *   - share.embed.load
 *   - operate.provenance.load
 *   - collaboration.session.join
 *   - publish.map-package
 */

export type SmokeStatus =
  | "ok"
  | "missing"
  | "unauthorized"
  | "unsupported"
  | "pending-binding";

export interface ConsoleSmokeEvent {
  readonly surface: string;
  readonly sdkSubpath: string;
  readonly status: SmokeStatus;
  readonly durationMs: number;
  readonly at: string;
  readonly detail?: Readonly<Record<string, unknown>>;
}

export const CONSOLE_SMOKE_EVENT_NAME = "honua:console-smoke";

export type ConsoleSmokeListener = (event: ConsoleSmokeEvent) => void;

const localListeners = new Set<ConsoleSmokeListener>();

export function emitConsoleSmoke(input: Omit<ConsoleSmokeEvent, "at">): ConsoleSmokeEvent {
  const event: ConsoleSmokeEvent = { ...input, at: new Date().toISOString() };
  for (const listener of localListeners) listener(event);
  if (typeof window !== "undefined" && typeof window.dispatchEvent === "function") {
    window.dispatchEvent(new CustomEvent(CONSOLE_SMOKE_EVENT_NAME, { detail: event }));
  }
  return event;
}

export function addConsoleSmokeListener(listener: ConsoleSmokeListener): () => void {
  localListeners.add(listener);
  return () => localListeners.delete(listener);
}
