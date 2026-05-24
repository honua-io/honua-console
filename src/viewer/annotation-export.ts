import type { AnnotationWorkspaceState, WebMapDocVersion } from "../saved-maps/types.js";
import {
  type AnnotationAnchor,
  type AnnotationModerationState,
  type AnnotationThreadStatus,
  getAnnotationThreads,
  getPointAnnotations,
  getShapeAnnotations,
} from "./annotation-state.js";

export type AnnotationExportFormat = "json" | "geojson";

export interface AnnotationExportContext {
  mapId: string;
  mapTitle: string;
  webMapVersion: WebMapDocVersion;
  exportedAt?: string;
}

export interface AnnotationJsonExport {
  version: "honua-annotation-export/v1";
  exportedAt: string;
  map: {
    id: string;
    title: string;
    webMapVersion: WebMapDocVersion;
  };
  counts: {
    annotations: number;
    threads: number;
    openThreads: number;
    resolvedThreads: number;
    pendingThreads: number;
    hiddenThreads: number;
  };
  workspace: AnnotationWorkspaceState;
  pointAnnotations: ReturnType<typeof getPointAnnotations>;
  shapeAnnotations: ReturnType<typeof getShapeAnnotations>;
  commentThreads: ReturnType<typeof getAnnotationThreads>;
}

export interface AnnotationGeoJsonFeatureCollection {
  type: "FeatureCollection";
  metadata: {
    version: "honua-annotation-geojson/v1";
    exportedAt: string;
    map: {
      id: string;
      title: string;
      webMapVersion: WebMapDocVersion;
    };
  };
  features: AnnotationGeoJsonFeature[];
}

export interface AnnotationGeoJsonFeature {
  type: "Feature";
  id: string;
  properties: AnnotationCommentThreadGeoJsonProperties | AnnotationShapeGeoJsonProperties;
  geometry:
    | { type: "Point"; coordinates: [number, number] }
    | { type: "Polygon"; coordinates: [number, number][][] }
    | { type: "LineString"; coordinates: [number, number][] }
    | null;
}

export interface AnnotationCommentThreadGeoJsonProperties {
  kind: "comment-thread";
  threadId: string;
  annotationId: string | null;
  title: string;
  status: AnnotationThreadStatus;
  moderationState: AnnotationModerationState;
  submittedBy: "member" | "guest";
  anchorKind: AnnotationAnchor["kind"];
  layerId?: string;
  featureId?: string;
  label?: string;
  commentCount: number;
  createdById: string;
  createdByName?: string;
  firstComment?: string;
  latestCommentAt?: string;
  createdAt: string;
  updatedAt: string;
}

export interface AnnotationShapeGeoJsonProperties {
  kind: "shape";
  annotationId: string;
  shape: ReturnType<typeof getShapeAnnotations>[number]["shape"];
  title: string;
  status: AnnotationThreadStatus;
  createdById: string;
  createdByName?: string;
  createdAt: string;
  style?: ReturnType<typeof getShapeAnnotations>[number]["style"];
}

export interface SerializedAnnotationExport {
  filename: string;
  mediaType: "application/json" | "application/geo+json";
  text: string;
}

export function buildAnnotationJsonExport(
  workspace: AnnotationWorkspaceState,
  context: AnnotationExportContext,
): AnnotationJsonExport {
  const pointAnnotations = getPointAnnotations(workspace);
  const shapeAnnotations = getShapeAnnotations(workspace);
  const commentThreads = getAnnotationThreads(workspace);
  return {
    version: "honua-annotation-export/v1",
    exportedAt: context.exportedAt ?? new Date().toISOString(),
    map: {
      id: context.mapId,
      title: context.mapTitle,
      webMapVersion: context.webMapVersion,
    },
    counts: {
      annotations: pointAnnotations.length + shapeAnnotations.length,
      threads: commentThreads.length,
      openThreads: commentThreads.filter((thread) => thread.status === "open").length,
      resolvedThreads: commentThreads.filter((thread) => thread.status === "resolved").length,
      pendingThreads: commentThreads.filter((thread) => thread.moderation.state === "pending").length,
      hiddenThreads: commentThreads.filter((thread) => thread.moderation.state === "hidden").length,
    },
    workspace,
    pointAnnotations,
    shapeAnnotations,
    commentThreads,
  };
}

export function buildAnnotationGeoJsonExport(
  workspace: AnnotationWorkspaceState,
  context: AnnotationExportContext,
): AnnotationGeoJsonFeatureCollection {
  const annotationsByThread = new Map(
    getPointAnnotations(workspace).map((annotation) => [annotation.threadId, annotation]),
  );
  const features: AnnotationGeoJsonFeature[] = [];

  for (const thread of getAnnotationThreads(workspace)) {
    const coordinates = thread.anchor.lngLat;
    const annotation = annotationsByThread.get(thread.id);
    features.push({
      type: "Feature",
      id: thread.id,
      properties: {
        kind: "comment-thread",
        threadId: thread.id,
        annotationId: annotation?.id ?? null,
        title: thread.title,
        status: thread.status,
        moderationState: thread.moderation.state,
        submittedBy: thread.moderation.submittedBy,
        anchorKind: thread.anchor.kind,
        ...(thread.anchor.kind === "feature"
          ? {
              layerId: thread.anchor.layerId,
              featureId: thread.anchor.featureId,
              ...(thread.anchor.label ? { label: thread.anchor.label } : {}),
            }
          : {}),
        commentCount: thread.comments.length,
        createdById: thread.createdBy.id,
        ...(thread.createdBy.name ? { createdByName: thread.createdBy.name } : {}),
        ...(thread.comments[0]?.body ? { firstComment: thread.comments[0].body } : {}),
        ...(thread.comments.at(-1)?.createdAt ? { latestCommentAt: thread.comments.at(-1)?.createdAt } : {}),
        createdAt: thread.createdAt,
        updatedAt: thread.updatedAt,
      },
      geometry: coordinates ? { type: "Point", coordinates } : null,
    });
  }

  for (const annotation of getShapeAnnotations(workspace)) {
    features.push({
      type: "Feature",
      id: annotation.id,
      properties: {
        kind: "shape",
        annotationId: annotation.id,
        shape: annotation.shape,
        title: annotation.title,
        status: annotation.status,
        createdById: annotation.createdBy.id,
        ...(annotation.createdBy.name ? { createdByName: annotation.createdBy.name } : {}),
        createdAt: annotation.createdAt,
        ...(annotation.style ? { style: annotation.style } : {}),
      },
      geometry:
        annotation.shape === "freehand"
          ? { type: "LineString", coordinates: annotation.geometry.coordinates }
          : {
              type: "Polygon",
              coordinates: [annotation.geometry.coordinates],
            },
    });
  }

  return {
    type: "FeatureCollection",
    metadata: {
      version: "honua-annotation-geojson/v1",
      exportedAt: context.exportedAt ?? new Date().toISOString(),
      map: {
        id: context.mapId,
        title: context.mapTitle,
        webMapVersion: context.webMapVersion,
      },
    },
    features,
  };
}

export function serializeAnnotationExport(
  workspace: AnnotationWorkspaceState,
  context: AnnotationExportContext,
  format: AnnotationExportFormat,
): SerializedAnnotationExport {
  const body =
    format === "geojson"
      ? buildAnnotationGeoJsonExport(workspace, context)
      : buildAnnotationJsonExport(workspace, context);
  return {
    filename: annotationExportFilename(context.mapId, format),
    mediaType: format === "geojson" ? "application/geo+json" : "application/json",
    text: `${JSON.stringify(body, null, 2)}\n`,
  };
}

export function annotationExportFilename(mapId: string, format: AnnotationExportFormat): string {
  const slug = slugFilenamePart(mapId) || "saved-map";
  return `${slug}-annotations.${format === "geojson" ? "geojson" : "json"}`;
}

function slugFilenamePart(value: string): string {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9_-]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 80);
}
