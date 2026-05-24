import { describe, expect, it, vi } from "vitest";
import { type MapHandle, captureThumbnail, fitWithin } from "../thumbnail.js";

function makeMockCanvas(width: number, height: number): HTMLCanvasElement {
  return { width, height } as unknown as HTMLCanvasElement;
}

function makeMockMap(canvas: HTMLCanvasElement | null): MapHandle {
  return {
    getCanvas() {
      if (!canvas) throw new Error("simulated: canvas missing");
      return canvas;
    },
  };
}

describe("fitWithin", () => {
  it("never upscales", () => {
    expect(fitWithin(100, 100, 512, 320)).toEqual({ width: 100, height: 100 });
  });

  it("scales down preserving aspect", () => {
    expect(fitWithin(2048, 1024, 512, 320)).toEqual({ width: 512, height: 256 });
  });

  it("handles degenerate inputs", () => {
    expect(fitWithin(0, 0, 200, 100)).toEqual({ width: 200, height: 100 });
  });
});

describe("captureThumbnail", () => {
  it("returns ok=true when the encoder succeeds", async () => {
    const map = makeMockMap(makeMockCanvas(1024, 768));
    const blob = new Blob([new Uint8Array([1])], { type: "image/png" });
    const result = await captureThumbnail(map, {
      encoder: async () => blob,
    });
    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.blob).toBe(blob);
      expect(result.width).toBeLessThanOrEqual(512);
      expect(result.height).toBeLessThanOrEqual(320);
    }
  });

  it("never throws — returns ok=false with a reason on encoder failure", async () => {
    const warn = vi.fn();
    const map = makeMockMap(makeMockCanvas(800, 600));
    const result = await captureThumbnail(map, {
      logger: { warn },
      encoder: async () => {
        throw new Error("CORS blocked");
      },
    });
    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.reason).toBe("CORS blocked");
    }
    expect(warn).toHaveBeenCalledOnce();
  });

  it("returns ok=false with reason 'map-has-no-canvas' when getCanvas returns null", async () => {
    const map: MapHandle = { getCanvas: () => null as unknown as HTMLCanvasElement };
    const result = await captureThumbnail(map, { logger: { warn: () => {} } });
    expect(result).toEqual({ ok: false, reason: "map-has-no-canvas" });
  });

  it("isolates getCanvas() throwing — does NOT propagate", async () => {
    const map = makeMockMap(null);
    const result = await captureThumbnail(map, { logger: { warn: () => {} } });
    expect(result.ok).toBe(false);
  });

  it("returns ok=false when the encoded blob exceeds the 200 KB contract", async () => {
    const warn = vi.fn();
    const map = makeMockMap(makeMockCanvas(2048, 1024));
    const oversized = new Blob([new Uint8Array(300 * 1024)], { type: "image/png" });
    const result = await captureThumbnail(map, {
      logger: { warn },
      encoder: async () => oversized,
    });
    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.reason).toMatch(/thumbnail-too-large/);
      expect(result.reason).toMatch(/307200B/);
      expect(result.reason).toMatch(/204800B/);
    }
    expect(warn).toHaveBeenCalledOnce();
  });

  it("respects an explicit maxBytes override", async () => {
    const map = makeMockMap(makeMockCanvas(800, 600));
    const fivekb = new Blob([new Uint8Array(5 * 1024)], { type: "image/png" });
    const tooBigForCustom = await captureThumbnail(map, {
      logger: { warn: () => {} },
      maxBytes: 1024,
      encoder: async () => fivekb,
    });
    expect(tooBigForCustom.ok).toBe(false);

    const fitsCustom = await captureThumbnail(map, {
      logger: { warn: () => {} },
      maxBytes: 10 * 1024,
      encoder: async () => fivekb,
    });
    expect(fitsCustom.ok).toBe(true);
  });
});
