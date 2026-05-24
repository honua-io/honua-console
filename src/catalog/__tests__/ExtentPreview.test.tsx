import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";

import type { Extent } from "../../contracts/content-item.js";
import { ExtentPreview } from "../components/ExtentPreview.js";

afterEach(() => {
  cleanup();
});

describe("ExtentPreview — antimeridian rendering", () => {
  it("renders a single rectangle for a regular extent", () => {
    const extent: Extent = { bbox: [-158.3, 21.2, -157.6, 21.8], crs: "EPSG:4326" };
    render(<ExtentPreview extent={extent} />);
    const svg = screen.getByTestId("extent-preview-svg");
    expect(svg.getAttribute("data-antimeridian")).toBe("false");
    expect(screen.getAllByTestId("extent-preview-bbox")).toHaveLength(1);
  });

  it("renders two rectangles for an antimeridian-crossing extent (west > east)", () => {
    const extent: Extent = { bbox: [170, -10, -170, 10], crs: "EPSG:4326" };
    render(<ExtentPreview extent={extent} />);
    const svg = screen.getByTestId("extent-preview-svg");
    expect(svg.getAttribute("data-antimeridian")).toBe("true");
    const rects = screen.getAllByTestId("extent-preview-bbox");
    expect(rects).toHaveLength(2);
    const widths = rects.map((r) => Number(r.getAttribute("width")));
    for (const w of widths) {
      expect(w).toBeGreaterThan(2);
    }
    const xs = rects.map((r) => Number(r.getAttribute("x")));
    expect(Math.min(...xs)).toBe(0);
  });
});
