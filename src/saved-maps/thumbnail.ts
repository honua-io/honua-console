/**
 * Saved-map thumbnail capture.
 *
 * Best-effort canvas-based capture from the active map instance. The save
 * flow MUST tolerate failure: a thumbnail is preview affordance, not a
 * correctness requirement. Failure isolates to a logged warning; the saved
 * item ships with `preview.thumbnail: null` and the catalog renders a
 * placeholder card image.
 */

const DEFAULT_MAX_WIDTH = 512;
const DEFAULT_MAX_HEIGHT = 320;
/** Saved-map design contract: thumbnails must be <= 200 KB after encode. */
const DEFAULT_MAX_BYTES = 200 * 1024;

export interface MapHandle {
  /**
   * Returns the active map canvas. Compatible with MapLibre's
   * `Map.getCanvas()` and any other library exposing the same shape.
   */
  getCanvas(): HTMLCanvasElement;
}

export interface CaptureThumbnailOptions {
  maxWidth?: number;
  maxHeight?: number;
  /** Encoded blob byte cap. Defaults to 200 KB per the saved-map contract. */
  maxBytes?: number;
  /** Override the resize/encode pipeline; primarily for tests. */
  encoder?: (canvas: HTMLCanvasElement, width: number, height: number) => Promise<Blob>;
  logger?: { warn: (msg: string, error?: unknown) => void };
}

export interface CaptureThumbnailSuccess {
  ok: true;
  blob: Blob;
  width: number;
  height: number;
}

export interface CaptureThumbnailFailure {
  ok: false;
  reason: string;
}

export type CaptureThumbnailResult = CaptureThumbnailSuccess | CaptureThumbnailFailure;

/**
 * Capture a thumbnail from the active map instance. Always resolves; never
 * throws. Returns a discriminated result so callers can decide whether to
 * proceed with `preview.thumbnail: null` or retry.
 */
export async function captureThumbnail(
  map: MapHandle,
  options: CaptureThumbnailOptions = {},
): Promise<CaptureThumbnailResult> {
  const maxWidth = options.maxWidth ?? DEFAULT_MAX_WIDTH;
  const maxHeight = options.maxHeight ?? DEFAULT_MAX_HEIGHT;
  const maxBytes = options.maxBytes ?? DEFAULT_MAX_BYTES;
  const logger = options.logger ?? console;
  try {
    const source = map.getCanvas();
    if (!source) {
      return { ok: false, reason: "map-has-no-canvas" };
    }
    const { width, height } = fitWithin(source.width, source.height, maxWidth, maxHeight);
    const encoder = options.encoder ?? defaultEncoder;
    const blob = await encoder(source, width, height);
    if (blob.size > maxBytes) {
      const reason = `thumbnail-too-large: ${blob.size}B exceeds ${maxBytes}B`;
      logger.warn?.(reason);
      return { ok: false, reason };
    }
    return { ok: true, blob, width, height };
  } catch (error) {
    logger.warn?.("captureThumbnail failed", error);
    return { ok: false, reason: errorReason(error) };
  }
}

export function fitWithin(srcW: number, srcH: number, maxW: number, maxH: number): { width: number; height: number } {
  if (srcW <= 0 || srcH <= 0) return { width: maxW, height: maxH };
  const scale = Math.min(maxW / srcW, maxH / srcH, 1);
  return {
    width: Math.max(1, Math.round(srcW * scale)),
    height: Math.max(1, Math.round(srcH * scale)),
  };
}

async function defaultEncoder(source: HTMLCanvasElement, width: number, height: number): Promise<Blob> {
  if (typeof OffscreenCanvas === "undefined") {
    throw new Error("OffscreenCanvas is not available in this environment");
  }
  const offscreen = new OffscreenCanvas(width, height);
  const ctx = offscreen.getContext("2d");
  if (!ctx) throw new Error("2d-context-unavailable");
  // biome-ignore lint/suspicious/noExplicitAny: drawImage accepts canvas-like sources
  ctx.drawImage(source as any, 0, 0, width, height);
  return await offscreen.convertToBlob({ type: "image/png" });
}

function errorReason(error: unknown): string {
  if (error instanceof Error) return error.message || "thumbnail-capture-failed";
  return "thumbnail-capture-failed";
}
