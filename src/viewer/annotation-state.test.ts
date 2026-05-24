import { describe, expect, it } from "vitest";
import {
  addFeatureCommentThread,
  addMapCommentThread,
  appendAnnotationReply,
  countOpenThreads,
  createEmptyAnnotationWorkspace,
  createShapeAnnotation,
  getAnnotationPins,
  getAnnotationThreads,
  getPointAnnotations,
  getShapeAnnotations,
  parseAnnotationWorkspace,
  setAnnotationPublicComments,
  setAnnotationThreadModeration,
  setAnnotationThreadStatus,
} from "./annotation-state.js";

const actor = { id: "u-member", name: "Mira Chen" };
const now = () => new Date("2026-05-10T12:00:00.000Z");
const generateId = (prefix: string) => `${prefix}-fixed`;

describe("annotation workspace state", () => {
  it("creates an empty workspace for saved maps without annotations", () => {
    const workspace = createEmptyAnnotationWorkspace();
    expect(workspace).toEqual({
      version: "honua-annotations/v1",
      visibility: { defaultAudience: "map", publicComments: false },
      annotationSets: [],
      commentThreads: [],
    });
    expect(parseAnnotationWorkspace(undefined)).toEqual({ status: "ok", workspace });
  });

  it("adds a map pin comment with stable ids, anchor, author, and timestamp", () => {
    const workspace = addMapCommentThread(createEmptyAnnotationWorkspace(), {
      body: "Check shoreline access before publishing.",
      lngLat: [-157.81234567, 21.31234567],
      actor,
      now,
      generateId,
    });

    expect(getPointAnnotations(workspace)).toEqual([
      expect.objectContaining({
        id: "pin-fixed",
        threadId: "thread-fixed",
        anchor: { kind: "map", lngLat: [-157.81235, 21.31235] },
        createdBy: actor,
      }),
    ]);
    expect(getAnnotationThreads(workspace)).toEqual([
      expect.objectContaining({
        id: "thread-fixed",
        status: "open",
        anchor: { kind: "map", lngLat: [-157.81235, 21.31235] },
        createdAt: "2026-05-10T12:00:00.000Z",
        comments: [
          expect.objectContaining({
            body: "Check shoreline access before publishing.",
            author: actor,
          }),
        ],
      }),
    ]);
    expect(getAnnotationPins(workspace)).toEqual([
      expect.objectContaining({
        id: "pin-fixed",
        threadId: "thread-fixed",
        anchorKind: "map",
        lngLat: [-157.81235, 21.31235],
      }),
    ]);
  });

  it("adds a feature-linked comment thread without requiring a map pin annotation", () => {
    const workspace = addFeatureCommentThread(createEmptyAnnotationWorkspace(), {
      body: "Confirm the sensor count with field ops.",
      selected: { layerId: "field-stations", featureId: "station-makiki" },
      label: "Makiki Watershed Station",
      lngLat: [-157.825, 21.32],
      actor,
      now,
      generateId,
    });

    expect(getPointAnnotations(workspace)).toEqual([]);
    expect(getAnnotationThreads(workspace)[0]).toMatchObject({
      id: "thread-fixed",
      title: "Confirm the sensor count with field ops.",
      anchor: {
        kind: "feature",
        layerId: "field-stations",
        featureId: "station-makiki",
        label: "Makiki Watershed Station",
        lngLat: [-157.825, 21.32],
      },
    });
    expect(getAnnotationPins(workspace)).toEqual([
      expect.objectContaining({ threadId: "thread-fixed", anchorKind: "feature", lngLat: [-157.825, 21.32] }),
    ]);
  });

  it("creates and parses shape annotations with stable ids, geometry, author, timestamp, and style", () => {
    const workspace = createShapeAnnotation(createEmptyAnnotationWorkspace(), {
      title: "  Shoreline review area  ",
      shape: "rectangle",
      coordinates: [
        [-157.81234567, 21.31234567],
        [-157.8, 21.31234567],
        [-157.8, 21.3],
        [-157.81234567, 21.3],
      ],
      actor,
      now,
      generateId,
      style: {
        strokeColor: "#2563eb",
        fillColor: "#93c5fd",
        strokeWidth: 2,
        fillOpacity: 1.25,
      },
    });

    expect(workspace.commentThreads).toEqual([]);
    expect(getShapeAnnotations(workspace)).toEqual([
      {
        id: "shape-fixed",
        kind: "shape",
        shape: "rectangle",
        title: "Shoreline review area",
        status: "open",
        geometry: {
          type: "rectangle",
          coordinates: [
            [-157.81235, 21.31235],
            [-157.8, 21.31235],
            [-157.8, 21.3],
            [-157.81235, 21.3],
            [-157.81235, 21.31235],
          ],
        },
        createdAt: "2026-05-10T12:00:00.000Z",
        createdBy: actor,
        style: {
          strokeColor: "#2563eb",
          fillColor: "#93c5fd",
          strokeWidth: 2,
          fillOpacity: 1,
        },
      },
    ]);
  });

  it("parses existing polygon shape records from annotationSets", () => {
    const workspace = {
      ...createEmptyAnnotationWorkspace(),
      annotationSets: [
        {
          id: "shape-existing",
          kind: "shape",
          shape: "polygon",
          title: "Vegetation boundary",
          status: "resolved",
          geometry: {
            type: "polygon",
            coordinates: [
              [-157.81, 21.31],
              [-157.8, 21.31],
              [-157.805, 21.3],
            ],
          },
          createdAt: "2026-05-10T11:00:00.000Z",
          createdBy: { id: "u-owner" },
          style: { strokeColor: "#16a34a" },
        },
      ],
    };

    expect(getShapeAnnotations(workspace)).toEqual([
      expect.objectContaining({
        id: "shape-existing",
        shape: "polygon",
        status: "resolved",
        geometry: {
          type: "polygon",
          coordinates: [
            [-157.81, 21.31],
            [-157.8, 21.31],
            [-157.805, 21.3],
            [-157.81, 21.31],
          ],
        },
        style: { strokeColor: "#16a34a" },
      }),
    ]);
  });

  it("creates and parses freehand shape annotations as open line strings", () => {
    const workspace = createShapeAnnotation(createEmptyAnnotationWorkspace(), {
      title: "  Field sketch  ",
      shape: "freehand",
      coordinates: [
        [-157.81234567, 21.31234567],
        [-157.81234567, 21.31234567],
        [-157.811, 21.313],
        [-157.81, 21.314],
      ],
      actor,
      now,
      generateId,
      style: {
        strokeColor: "#9333ea",
        strokeWidth: 3,
      },
    });

    expect(getShapeAnnotations(workspace)).toEqual([
      expect.objectContaining({
        id: "shape-fixed",
        shape: "freehand",
        title: "Field sketch",
        geometry: {
          type: "freehand",
          coordinates: [
            [-157.81235, 21.31235],
            [-157.811, 21.313],
            [-157.81, 21.314],
          ],
        },
        style: {
          strokeColor: "#9333ea",
          strokeWidth: 3,
        },
      }),
    ]);
  });

  it("appends replies and resolves or reopens a thread without deleting comments", () => {
    const initial = addMapCommentThread(createEmptyAnnotationWorkspace(), {
      body: "Initial review note.",
      lngLat: [-157.8, 21.3],
      actor,
      now,
      generateId,
    });
    const replied = appendAnnotationReply(initial, {
      threadId: "thread-fixed",
      body: "Second reviewer agrees.",
      actor: { id: "u-owner", name: "Nanea Lee" },
      now: () => new Date("2026-05-10T13:00:00.000Z"),
      generateId,
    });
    const resolved = setAnnotationThreadStatus(replied, {
      threadId: "thread-fixed",
      status: "resolved",
      actor,
      now,
      generateId,
    });
    const reopened = setAnnotationThreadStatus(resolved, {
      threadId: "thread-fixed",
      status: "open",
      actor,
      now,
      generateId,
    });

    expect(getAnnotationThreads(replied)[0]?.comments.map((comment) => comment.body)).toEqual([
      "Initial review note.",
      "Second reviewer agrees.",
    ]);
    expect(countOpenThreads(resolved)).toBe(0);
    expect(getPointAnnotations(resolved)[0]?.status).toBe("resolved");
    expect(countOpenThreads(reopened)).toBe(1);
    expect(getAnnotationThreads(reopened)[0]?.comments).toHaveLength(2);
  });

  it("accepts publicComments=true and preserves the owner opt-in policy", () => {
    const workspace = {
      version: "honua-annotations/v1" as const,
      visibility: { defaultAudience: "map" as const, publicComments: true },
      annotationSets: [],
      commentThreads: [],
    };

    const parsed = parseAnnotationWorkspace(workspace);
    expect(parsed).toEqual({ status: "ok", workspace });
    expect(setAnnotationPublicComments(createEmptyAnnotationWorkspace(), { enabled: true, actor })).toMatchObject({
      visibility: { defaultAudience: "map", publicComments: true },
    });
  });

  it("hides pending and hidden guest comments from public reads until approved", () => {
    let workspace = setAnnotationPublicComments(createEmptyAnnotationWorkspace(), { enabled: true, actor });
    workspace = addMapCommentThread(workspace, {
      body: "Guest asks for a crosswalk note.",
      lngLat: [-157.8, 21.3],
      actor: { id: "guest", name: "Guest visitor" },
      moderationState: "pending",
      submittedBy: "guest",
      now,
      generateId,
    });

    expect(getAnnotationThreads(workspace)).toEqual([
      expect.objectContaining({
        moderation: { state: "pending", submittedBy: "guest" },
      }),
    ]);
    expect(getAnnotationThreads(workspace, { audience: "public" })).toEqual([]);
    expect(getAnnotationPins(workspace, { audience: "public" })).toEqual([]);

    const approved = setAnnotationThreadModeration(workspace, {
      threadId: "thread-fixed",
      state: "approved",
      actor,
      now,
      generateId,
    });
    expect(getAnnotationThreads(approved, { audience: "public" })).toHaveLength(1);
    expect(getAnnotationPins(approved, { audience: "public" })).toHaveLength(1);

    const hidden = setAnnotationThreadModeration(approved, {
      threadId: "thread-fixed",
      state: "hidden",
      actor,
      now,
      generateId,
    });
    expect(getAnnotationThreads(hidden, { audience: "public" })).toEqual([]);
    expect(getAnnotationThreads(hidden)[0]?.moderation).toMatchObject({
      state: "hidden",
      submittedBy: "guest",
      moderatedBy: actor,
      moderatedAt: "2026-05-10T12:00:00.000Z",
    });
  });
});
