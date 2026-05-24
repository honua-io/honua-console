import { describe, expect, it } from "vitest";
import { WEBMAP_DOC_VERSION } from "../saved-maps/types.js";
import {
  buildAnnotationGeoJsonExport,
  buildAnnotationJsonExport,
  serializeAnnotationExport,
} from "./annotation-export.js";
import {
  addFeatureCommentThread,
  addMapCommentThread,
  appendAnnotationReply,
  createEmptyAnnotationWorkspace,
  createShapeAnnotation,
  setAnnotationThreadStatus,
} from "./annotation-state.js";

const actor = { id: "u-member", name: "Mira Chen" };
const exportContext = {
  mapId: "map-style-demo",
  mapTitle: "Honolulu style demo",
  webMapVersion: WEBMAP_DOC_VERSION,
  exportedAt: "2026-05-10T15:30:00.000Z",
};

describe("annotation export", () => {
  it("builds a lossless JSON export for saved-map annotations", () => {
    const workspace = buildWorkspace();
    const exported = buildAnnotationJsonExport(workspace, exportContext);

    expect(exported).toMatchObject({
      version: "honua-annotation-export/v1",
      exportedAt: "2026-05-10T15:30:00.000Z",
      map: {
        id: "map-style-demo",
        title: "Honolulu style demo",
        webMapVersion: "honua-webmap/v1",
      },
      counts: {
        annotations: 4,
        threads: 3,
        openThreads: 2,
        resolvedThreads: 1,
        pendingThreads: 0,
        hiddenThreads: 0,
      },
    });
    expect(exported.workspace).toEqual(workspace);
    expect(exported.workspace.visibility).toEqual({ defaultAudience: "map", publicComments: false });
    expect(exported.pointAnnotations).toEqual([
      expect.objectContaining({
        id: "pin-001",
        threadId: "thread-001",
        status: "resolved",
        anchor: { kind: "map", lngLat: [-157.81235, 21.31235] },
      }),
    ]);
    expect(exported.shapeAnnotations).toEqual([
      expect.objectContaining({
        id: "shape-001",
        shape: "rectangle",
        title: "Driveway review area",
        geometry: {
          type: "rectangle",
          coordinates: [
            [-157.814, 21.314],
            [-157.81, 21.314],
            [-157.81, 21.31],
            [-157.814, 21.31],
            [-157.814, 21.314],
          ],
        },
      }),
      expect.objectContaining({
        id: "shape-002",
        shape: "polygon",
        title: "Vegetation boundary",
        status: "resolved",
      }),
      expect.objectContaining({
        id: "shape-003",
        shape: "freehand",
        title: "Field sketch",
        status: "open",
      }),
    ]);
    expect(exported.commentThreads[0]?.comments.map((comment) => comment.body)).toEqual([
      "Review driveway label before publishing.",
      "Confirmed with the field lead.",
    ]);
    expect(exported.commentThreads[1]).toMatchObject({
      id: "thread-002",
      anchor: {
        kind: "feature",
        layerId: "field-stations",
        featureId: "station-makiki",
        label: "Makiki Watershed Station",
        lngLat: [-157.825, 21.32],
      },
    });
  });

  it("builds a GeoJSON projection without dropping non-spatial feature threads", () => {
    const exported = buildAnnotationGeoJsonExport(buildWorkspace(), exportContext);
    const byId = new Map(exported.features.map((feature) => [feature.id, feature]));

    expect(exported.metadata).toEqual({
      version: "honua-annotation-geojson/v1",
      exportedAt: "2026-05-10T15:30:00.000Z",
      map: {
        id: "map-style-demo",
        title: "Honolulu style demo",
        webMapVersion: "honua-webmap/v1",
      },
    });
    expect(exported.features).toHaveLength(6);
    expect(byId.get("thread-001")?.geometry).toEqual({
      type: "Point",
      coordinates: [-157.81235, 21.31235],
    });
    expect(byId.get("thread-001")?.properties).toMatchObject({
      kind: "comment-thread",
      threadId: "thread-001",
      annotationId: "pin-001",
      title: "Review driveway label before publishing.",
      status: "resolved",
      moderationState: "approved",
      submittedBy: "member",
      anchorKind: "map",
      commentCount: 2,
      createdById: "u-member",
      createdByName: "Mira Chen",
      firstComment: "Review driveway label before publishing.",
      latestCommentAt: "2026-05-10T13:00:00.000Z",
    });
    expect(byId.get("thread-002")?.geometry).toEqual({
      type: "Point",
      coordinates: [-157.825, 21.32],
    });
    expect(byId.get("thread-002")?.properties).toMatchObject({
      anchorKind: "feature",
      layerId: "field-stations",
      featureId: "station-makiki",
      label: "Makiki Watershed Station",
    });
    expect(byId.get("thread-003")?.geometry).toBeNull();
    expect(byId.get("thread-003")?.properties).toMatchObject({
      anchorKind: "feature",
      layerId: "districts",
      featureId: "district-kakaako",
      label: "Kakaako district",
    });
    expect(byId.get("shape-001")?.geometry).toEqual({
      type: "Polygon",
      coordinates: [
        [
          [-157.814, 21.314],
          [-157.81, 21.314],
          [-157.81, 21.31],
          [-157.814, 21.31],
          [-157.814, 21.314],
        ],
      ],
    });
    expect(byId.get("shape-001")?.properties).toMatchObject({
      kind: "shape",
      annotationId: "shape-001",
      shape: "rectangle",
      title: "Driveway review area",
      status: "open",
      createdById: "u-member",
      createdByName: "Mira Chen",
      style: {
        strokeColor: "#2563eb",
        fillColor: "#93c5fd",
        strokeWidth: 2,
        fillOpacity: 0.35,
      },
    });
    expect(byId.get("shape-002")?.properties).toMatchObject({
      kind: "shape",
      annotationId: "shape-002",
      shape: "polygon",
      status: "resolved",
      createdById: "u-owner",
    });
    expect(byId.get("shape-003")?.geometry).toEqual({
      type: "LineString",
      coordinates: [
        [-157.824, 21.32],
        [-157.823, 21.321],
        [-157.821, 21.3215],
      ],
    });
    expect(byId.get("shape-003")?.properties).toMatchObject({
      kind: "shape",
      annotationId: "shape-003",
      shape: "freehand",
      title: "Field sketch",
      status: "open",
      createdById: "u-member",
    });
  });

  it("serializes stable filenames and media types", () => {
    const json = serializeAnnotationExport(buildWorkspace(), exportContext, "json");
    const geojson = serializeAnnotationExport(buildWorkspace(), exportContext, "geojson");

    expect(json.filename).toBe("map-style-demo-annotations.json");
    expect(json.mediaType).toBe("application/json");
    expect(JSON.parse(json.text)).toMatchObject({ version: "honua-annotation-export/v1" });
    expect(json.text.endsWith("\n")).toBe(true);

    expect(geojson.filename).toBe("map-style-demo-annotations.geojson");
    expect(geojson.mediaType).toBe("application/geo+json");
    expect(JSON.parse(geojson.text)).toMatchObject({ type: "FeatureCollection" });
    expect(geojson.text.endsWith("\n")).toBe(true);
  });
});

function buildWorkspace() {
  const generateId = makeIdFactory();
  let workspace = createEmptyAnnotationWorkspace();
  workspace = addMapCommentThread(workspace, {
    body: "Review driveway label before publishing.",
    lngLat: [-157.81234567, 21.31234567],
    actor,
    now: () => new Date("2026-05-10T12:00:00.000Z"),
    generateId,
  });
  workspace = appendAnnotationReply(workspace, {
    threadId: "thread-001",
    body: "Confirmed with the field lead.",
    actor: { id: "u-owner", name: "Nanea Lee" },
    now: () => new Date("2026-05-10T13:00:00.000Z"),
    generateId,
  });
  workspace = setAnnotationThreadStatus(workspace, {
    threadId: "thread-001",
    status: "resolved",
    actor,
    now: () => new Date("2026-05-10T14:00:00.000Z"),
    generateId,
  });
  workspace = addFeatureCommentThread(workspace, {
    body: "Confirm sensor count with field ops.",
    selected: { layerId: "field-stations", featureId: "station-makiki" },
    label: "Makiki Watershed Station",
    lngLat: [-157.825, 21.32],
    actor,
    now: () => new Date("2026-05-10T14:15:00.000Z"),
    generateId,
  });
  workspace = addFeatureCommentThread(workspace, {
    body: "Check the district attribute before release.",
    selected: { layerId: "districts", featureId: "district-kakaako" },
    label: "Kakaako district",
    actor,
    now: () => new Date("2026-05-10T14:30:00.000Z"),
    generateId,
  });
  workspace = createShapeAnnotation(workspace, {
    title: "Driveway review area",
    shape: "rectangle",
    coordinates: [
      [-157.814, 21.314],
      [-157.81, 21.314],
      [-157.81, 21.31],
      [-157.814, 21.31],
    ],
    actor,
    now: () => new Date("2026-05-10T14:45:00.000Z"),
    generateId,
    style: {
      strokeColor: "#2563eb",
      fillColor: "#93c5fd",
      strokeWidth: 2,
      fillOpacity: 0.35,
    },
  });
  workspace = createShapeAnnotation(workspace, {
    title: "Vegetation boundary",
    shape: "polygon",
    coordinates: [
      [-157.826, 21.322],
      [-157.82, 21.321],
      [-157.822, 21.318],
    ],
    status: "resolved",
    actor: { id: "u-owner", name: "Nanea Lee" },
    now: () => new Date("2026-05-10T15:00:00.000Z"),
    generateId,
  });
  workspace = createShapeAnnotation(workspace, {
    title: "Field sketch",
    shape: "freehand",
    coordinates: [
      [-157.824, 21.32],
      [-157.823, 21.321],
      [-157.821, 21.3215],
    ],
    actor,
    now: () => new Date("2026-05-10T15:10:00.000Z"),
    generateId,
  });
  return workspace;
}

function makeIdFactory(): (prefix: string) => string {
  const counters = new Map<string, number>();
  return (prefix: string) => {
    const next = (counters.get(prefix) ?? 0) + 1;
    counters.set(prefix, next);
    return `${prefix}-${next.toString().padStart(3, "0")}`;
  };
}
