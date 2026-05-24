import type { Extent } from "../../contracts/content-item.js";

const VIEW_BOX_WIDTH = 360;
const VIEW_BOX_HEIGHT = 180;

export interface ExtentPreviewProps {
  readonly extent: Extent;
  readonly title?: string;
}

/**
 * SVG-only preview of an item's WGS84 bounding box on a flat lon/lat grid.
 * Avoids pulling MapLibre into the detail page bundle. Defers a full basemap
 * to honua-portal#13.
 */
export function ExtentPreview({ extent, title = "Item extent" }: ExtentPreviewProps) {
  const [west, south, east, north] = extent.bbox;
  const y = latToY(north);
  const h = Math.max(2, latToY(south) - y);
  const boxes = extentBoxes(west, east, y, h);
  const crossesAntimeridian = west > east;

  return (
    <figure className="extent-preview" aria-label={title}>
      <svg
        viewBox={`0 0 ${VIEW_BOX_WIDTH} ${VIEW_BOX_HEIGHT}`}
        role="img"
        aria-label={`Bounding box ${formatNumber(west)}, ${formatNumber(south)} to ${formatNumber(east)}, ${formatNumber(north)}`}
        className="extent-preview__svg"
        data-testid="extent-preview-svg"
        data-antimeridian={crossesAntimeridian ? "true" : "false"}
      >
        <rect width={VIEW_BOX_WIDTH} height={VIEW_BOX_HEIGHT} className="extent-preview__bg" />
        <line
          x1={VIEW_BOX_WIDTH / 2}
          y1={0}
          x2={VIEW_BOX_WIDTH / 2}
          y2={VIEW_BOX_HEIGHT}
          className="extent-preview__grid"
        />
        <line
          x1={0}
          y1={VIEW_BOX_HEIGHT / 2}
          x2={VIEW_BOX_WIDTH}
          y2={VIEW_BOX_HEIGHT / 2}
          className="extent-preview__grid"
        />
        {boxes.map((box) => (
          <rect
            key={`${box.x}-${box.width}`}
            x={box.x}
            y={box.y}
            width={box.width}
            height={box.height}
            className="extent-preview__bbox"
            data-testid="extent-preview-bbox"
          />
        ))}
      </svg>
      <figcaption className="extent-preview__caption">
        <span>W {formatNumber(west)}</span>
        <span>S {formatNumber(south)}</span>
        <span>E {formatNumber(east)}</span>
        <span>N {formatNumber(north)}</span>
      </figcaption>
    </figure>
  );
}

function extentBoxes(
  west: number,
  east: number,
  y: number,
  height: number,
): Array<{ x: number; y: number; width: number; height: number }> {
  if (west <= east) {
    const x = lonToX(west);
    return [{ x, y, width: Math.max(2, lonToX(east) - x), height }];
  }
  const westX = lonToX(west);
  const eastX = lonToX(east);
  return [
    { x: westX, y, width: Math.max(2, VIEW_BOX_WIDTH - westX), height },
    { x: 0, y, width: Math.max(2, eastX), height },
  ];
}

function lonToX(lon: number): number {
  const clamped = Math.max(-180, Math.min(180, lon));
  return ((clamped + 180) / 360) * VIEW_BOX_WIDTH;
}

function latToY(lat: number): number {
  const clamped = Math.max(-90, Math.min(90, lat));
  return ((90 - clamped) / 180) * VIEW_BOX_HEIGHT;
}

function formatNumber(value: number): string {
  return value.toFixed(2);
}
