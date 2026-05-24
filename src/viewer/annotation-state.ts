import {
  ANNOTATION_WORKSPACE_VERSION,
  type AnnotationWorkspaceState,
  type AnnotationWorkspaceVersion,
} from "../saved-maps/types.js";
import type { SelectedFeature } from "./types.js";

export type AnnotationThreadStatus = "open" | "resolved";
export type AnnotationModerationState = "approved" | "pending" | "hidden";
export type AnnotationReadAudience = "workspace" | "public";

export type AnnotationAnchor =
  | { kind: "map"; lngLat: [number, number] }
  | { kind: "feature"; layerId: string; featureId: string; label?: string; lngLat?: [number, number] };

export interface AnnotationActor {
  id: string;
  name?: string;
}

export interface AnnotationComment {
  id: string;
  body: string;
  author: AnnotationActor;
  createdAt: string;
}

export interface AnnotationModeration {
  state: AnnotationModerationState;
  submittedBy: "member" | "guest";
  moderatedAt?: string;
  moderatedBy?: AnnotationActor;
  reason?: string;
}

export interface PortalAnnotationThread extends Record<string, unknown> {
  id: string;
  kind: "comment-thread";
  title: string;
  status: AnnotationThreadStatus;
  moderation: AnnotationModeration;
  anchor: AnnotationAnchor;
  createdAt: string;
  updatedAt: string;
  createdBy: AnnotationActor;
  comments: AnnotationComment[];
}

export interface PortalPointAnnotation extends Record<string, unknown> {
  id: string;
  kind: "point";
  title: string;
  status: AnnotationThreadStatus;
  threadId: string;
  anchor: { kind: "map"; lngLat: [number, number] };
  createdAt: string;
  createdBy: AnnotationActor;
}

export type ShapeAnnotationType = "rectangle" | "polygon" | "freehand";

export interface ShapeAnnotationStyle {
  strokeColor?: string;
  fillColor?: string;
  strokeWidth?: number;
  fillOpacity?: number;
}

export interface ShapeAnnotationGeometry {
  type: ShapeAnnotationType;
  coordinates: [number, number][];
}

export interface PortalShapeAnnotation extends Record<string, unknown> {
  id: string;
  kind: "shape";
  shape: ShapeAnnotationType;
  title: string;
  status: AnnotationThreadStatus;
  geometry: ShapeAnnotationGeometry;
  createdAt: string;
  createdBy: AnnotationActor;
  style?: ShapeAnnotationStyle;
}

export interface AnnotationPin {
  id: string;
  threadId: string;
  title: string;
  status: AnnotationThreadStatus;
  anchorKind: AnnotationAnchor["kind"];
  lngLat: [number, number];
}

export type AnnotationWorkspaceParseResult =
  | { status: "ok"; workspace: AnnotationWorkspaceState }
  | { status: "unsupported"; message: string };

export interface AnnotationEditContext {
  actor: AnnotationActor;
  now?: () => Date;
  generateId?: (prefix: string) => string;
}

export interface AnnotationReadOptions {
  audience?: AnnotationReadAudience;
}

export interface AddMapThreadInput extends AnnotationEditContext {
  body: string;
  lngLat: [number, number];
  moderationState?: AnnotationModerationState;
  submittedBy?: AnnotationModeration["submittedBy"];
}

export interface AddFeatureThreadInput extends AnnotationEditContext {
  body: string;
  selected: SelectedFeature;
  label?: string;
  lngLat?: [number, number];
  moderationState?: AnnotationModerationState;
  submittedBy?: AnnotationModeration["submittedBy"];
}

export interface AppendReplyInput extends AnnotationEditContext {
  threadId: string;
  body: string;
}

export interface SetThreadStatusInput extends AnnotationEditContext {
  threadId: string;
  status: AnnotationThreadStatus;
}

export interface SetThreadModerationInput extends AnnotationEditContext {
  threadId: string;
  state: AnnotationModerationState;
  reason?: string;
}

export interface SetPublicCommentsInput extends AnnotationEditContext {
  enabled: boolean;
}

export interface CreateShapeAnnotationInput extends AnnotationEditContext {
  title: string;
  shape: ShapeAnnotationType;
  coordinates: [number, number][];
  status?: AnnotationThreadStatus;
  style?: ShapeAnnotationStyle;
}

export function createEmptyAnnotationWorkspace(): AnnotationWorkspaceState {
  return {
    version: ANNOTATION_WORKSPACE_VERSION,
    visibility: { defaultAudience: "map", publicComments: false },
    annotationSets: [],
    commentThreads: [],
  };
}

export function parseAnnotationWorkspace(value: AnnotationWorkspaceState | undefined): AnnotationWorkspaceParseResult {
  if (!value) return { status: "ok", workspace: createEmptyAnnotationWorkspace() };
  if (value.version !== ANNOTATION_WORKSPACE_VERSION) {
    return { status: "unsupported", message: `Unsupported annotation workspace version: ${value.version}` };
  }
  if (value.visibility?.defaultAudience !== "map" || typeof value.visibility.publicComments !== "boolean") {
    return {
      status: "unsupported",
      message: "This annotation workspace uses unsupported visibility settings.",
    };
  }
  return { status: "ok", workspace: cloneWorkspace(value) };
}

export function setAnnotationPublicComments(
  workspace: AnnotationWorkspaceState,
  input: SetPublicCommentsInput,
): AnnotationWorkspaceState {
  return {
    ...cloneWorkspace(workspace),
    visibility: {
      defaultAudience: "map",
      publicComments: input.enabled,
    },
  };
}

export function createShapeAnnotation(
  workspace: AnnotationWorkspaceState,
  input: CreateShapeAnnotationInput,
): AnnotationWorkspaceState {
  const createdAt = nowIso(input);
  const annotation: PortalShapeAnnotation = {
    id: nextId(workspace, input, "shape"),
    kind: "shape",
    shape: input.shape,
    title: normalizeTitle(input.title),
    status: input.status ?? "open",
    geometry: normalizeShapeGeometry(input.shape, input.coordinates),
    createdAt,
    createdBy: cloneActor(input.actor),
    ...(input.style ? { style: normalizeShapeStyle(input.style) } : {}),
  };

  return {
    ...cloneWorkspace(workspace),
    annotationSets: [...workspace.annotationSets.map(cloneRecord), annotation],
  };
}

export function addMapCommentThread(
  workspace: AnnotationWorkspaceState,
  input: AddMapThreadInput,
): AnnotationWorkspaceState {
  const body = normalizeBody(input.body);
  const createdAt = nowIso(input);
  const threadId = nextId(workspace, input, "thread");
  const annotationId = nextId(workspace, input, "pin");
  const title = summarizeBody(body, "Map comment");
  const actor = cloneActor(input.actor);
  const thread: PortalAnnotationThread = {
    id: threadId,
    kind: "comment-thread",
    title,
    status: "open",
    moderation: createModeration(input),
    anchor: { kind: "map", lngLat: normalizeLngLat(input.lngLat) },
    createdAt,
    updatedAt: createdAt,
    createdBy: actor,
    comments: [createComment(nextCommentId(workspace, input, threadId), body, actor, createdAt)],
  };
  const annotation: PortalPointAnnotation = {
    id: annotationId,
    kind: "point",
    title,
    status: "open",
    threadId,
    anchor: { kind: "map", lngLat: normalizeLngLat(input.lngLat) },
    createdAt,
    createdBy: actor,
  };

  return {
    ...cloneWorkspace(workspace),
    annotationSets: [...workspace.annotationSets.map(cloneRecord), annotation],
    commentThreads: [...workspace.commentThreads.map(cloneRecord), thread],
  };
}

export function addFeatureCommentThread(
  workspace: AnnotationWorkspaceState,
  input: AddFeatureThreadInput,
): AnnotationWorkspaceState {
  const body = normalizeBody(input.body);
  const createdAt = nowIso(input);
  const threadId = nextId(workspace, input, "thread");
  const actor = cloneActor(input.actor);
  const thread: PortalAnnotationThread = {
    id: threadId,
    kind: "comment-thread",
    title: summarizeBody(body, "Feature comment"),
    status: "open",
    moderation: createModeration(input),
    anchor: {
      kind: "feature",
      layerId: input.selected.layerId,
      featureId: input.selected.featureId,
      ...(input.label ? { label: input.label } : {}),
      ...(input.lngLat ? { lngLat: normalizeLngLat(input.lngLat) } : {}),
    },
    createdAt,
    updatedAt: createdAt,
    createdBy: actor,
    comments: [createComment(nextCommentId(workspace, input, threadId), body, actor, createdAt)],
  };

  return {
    ...cloneWorkspace(workspace),
    commentThreads: [...workspace.commentThreads.map(cloneRecord), thread],
  };
}

export function appendAnnotationReply(
  workspace: AnnotationWorkspaceState,
  input: AppendReplyInput,
): AnnotationWorkspaceState {
  const body = normalizeBody(input.body);
  const createdAt = nowIso(input);
  const actor = cloneActor(input.actor);
  return updateThread(workspace, input.threadId, (thread) => ({
    ...thread,
    updatedAt: createdAt,
    comments: [
      ...thread.comments.map(cloneComment),
      createComment(nextCommentId(workspace, input, thread.id), body, actor, createdAt),
    ],
  }));
}

export function setAnnotationThreadStatus(
  workspace: AnnotationWorkspaceState,
  input: SetThreadStatusInput,
): AnnotationWorkspaceState {
  const updatedAt = nowIso(input);
  const next = updateThread(workspace, input.threadId, (thread) => ({
    ...thread,
    status: input.status,
    updatedAt,
  }));
  return {
    ...next,
    annotationSets: next.annotationSets.map((record) => {
      const annotation = parsePointAnnotation(record);
      if (!annotation || annotation.threadId !== input.threadId) return cloneRecord(record);
      return { ...annotation, status: input.status };
    }),
  };
}

export function setAnnotationThreadModeration(
  workspace: AnnotationWorkspaceState,
  input: SetThreadModerationInput,
): AnnotationWorkspaceState {
  const updatedAt = nowIso(input);
  return updateThread(workspace, input.threadId, (thread) => ({
    ...thread,
    moderation: {
      ...thread.moderation,
      state: input.state,
      moderatedAt: updatedAt,
      moderatedBy: cloneActor(input.actor),
      ...(input.reason ? { reason: input.reason.trim() } : {}),
    },
    updatedAt,
  }));
}

export function getAnnotationThreads(
  workspace: AnnotationWorkspaceState,
  options: AnnotationReadOptions = {},
): PortalAnnotationThread[] {
  return workspace.commentThreads.flatMap((record) => {
    const thread = parseCommentThread(record);
    return thread && shouldIncludeThread(thread, options) ? [thread] : [];
  });
}

export function getPointAnnotations(workspace: AnnotationWorkspaceState): PortalPointAnnotation[] {
  return workspace.annotationSets.flatMap((record) => {
    const annotation = parsePointAnnotation(record);
    return annotation ? [annotation] : [];
  });
}

export function getShapeAnnotations(workspace: AnnotationWorkspaceState): PortalShapeAnnotation[] {
  return workspace.annotationSets.flatMap((record) => {
    const annotation = parseShapeAnnotation(record);
    return annotation ? [annotation] : [];
  });
}

export function getAnnotationPins(
  workspace: AnnotationWorkspaceState,
  options: AnnotationReadOptions = {},
): AnnotationPin[] {
  const pins = new Map<string, AnnotationPin>();
  const visibleThreads = getAnnotationThreads(workspace, options);
  const visibleThreadIds = new Set(visibleThreads.map((thread) => thread.id));
  for (const annotation of getPointAnnotations(workspace)) {
    if (options.audience === "public" && !visibleThreadIds.has(annotation.threadId)) continue;
    pins.set(annotation.threadId, {
      id: annotation.id,
      threadId: annotation.threadId,
      title: annotation.title,
      status: annotation.status,
      anchorKind: "map",
      lngLat: annotation.anchor.lngLat,
    });
  }
  for (const thread of visibleThreads) {
    const lngLat = thread.anchor.lngLat;
    if (!lngLat) continue;
    pins.set(thread.id, {
      id: pins.get(thread.id)?.id ?? `pin-${thread.id}`,
      threadId: thread.id,
      title: thread.title,
      status: thread.status,
      anchorKind: thread.anchor.kind,
      lngLat,
    });
  }
  return Array.from(pins.values());
}

export function countOpenThreads(workspace: AnnotationWorkspaceState, options: AnnotationReadOptions = {}): number {
  return getAnnotationThreads(workspace, options).filter((thread) => thread.status === "open").length;
}

function updateThread(
  workspace: AnnotationWorkspaceState,
  threadId: string,
  update: (thread: PortalAnnotationThread) => PortalAnnotationThread,
): AnnotationWorkspaceState {
  let found = false;
  const nextThreads = workspace.commentThreads.map((record) => {
    const thread = parseCommentThread(record);
    if (!thread || thread.id !== threadId) return cloneRecord(record);
    found = true;
    return update(thread);
  });
  if (!found) throw new Error(`Annotation thread not found: ${threadId}`);
  return {
    ...cloneWorkspace(workspace),
    commentThreads: nextThreads,
  };
}

function parseCommentThread(record: Record<string, unknown>): PortalAnnotationThread | null {
  if (record["kind"] !== "comment-thread") return null;
  if (typeof record["id"] !== "string" || typeof record["title"] !== "string") return null;
  const status = parseThreadStatus(record["status"]);
  if (!status) return null;
  const anchor = parseAnchor(record["anchor"]);
  if (!anchor) return null;
  const comments = Array.isArray(record["comments"])
    ? record["comments"].flatMap((entry) => {
        const comment = parseComment(entry);
        return comment ? [comment] : [];
      })
    : [];
  return {
    id: record["id"],
    kind: "comment-thread",
    title: record["title"],
    status,
    moderation: parseModeration(record["moderation"]),
    anchor,
    createdAt: typeof record["createdAt"] === "string" ? record["createdAt"] : "",
    updatedAt: typeof record["updatedAt"] === "string" ? record["updatedAt"] : "",
    createdBy: parseActor(record["createdBy"]) ?? { id: "unknown" },
    comments,
  };
}

function parseModeration(value: unknown): AnnotationModeration {
  if (!isRecord(value)) return { state: "approved", submittedBy: "member" };
  const state = parseModerationState(value["state"]) ?? "approved";
  const submittedBy = value["submittedBy"] === "guest" ? "guest" : "member";
  const moderatedBy = parseActor(value["moderatedBy"]);
  return {
    state,
    submittedBy,
    ...(typeof value["moderatedAt"] === "string" ? { moderatedAt: value["moderatedAt"] } : {}),
    ...(moderatedBy ? { moderatedBy } : {}),
    ...(typeof value["reason"] === "string" ? { reason: value["reason"] } : {}),
  };
}

function parsePointAnnotation(record: Record<string, unknown>): PortalPointAnnotation | null {
  if (record["kind"] !== "point") return null;
  if (typeof record["id"] !== "string" || typeof record["threadId"] !== "string") return null;
  const status = parseThreadStatus(record["status"]);
  if (!status) return null;
  const anchorRecord = isRecord(record["anchor"]) ? record["anchor"] : null;
  if (anchorRecord?.["kind"] !== "map") return null;
  const lngLat = parseLngLat(anchorRecord["lngLat"]);
  if (!lngLat) return null;
  return {
    id: record["id"],
    kind: "point",
    title: typeof record["title"] === "string" ? record["title"] : "Map comment",
    status,
    threadId: record["threadId"],
    anchor: { kind: "map", lngLat },
    createdAt: typeof record["createdAt"] === "string" ? record["createdAt"] : "",
    createdBy: parseActor(record["createdBy"]) ?? { id: "unknown" },
  };
}

function parseShapeAnnotation(record: Record<string, unknown>): PortalShapeAnnotation | null {
  if (record["kind"] !== "shape") return null;
  if (typeof record["id"] !== "string") return null;
  const shape = parseShapeAnnotationType(record["shape"]);
  if (!shape) return null;
  const status = parseThreadStatus(record["status"]);
  if (!status) return null;
  const geometry = parseShapeGeometry(record["geometry"], shape);
  if (!geometry) return null;
  const style = parseShapeStyle(record["style"]);
  return {
    id: record["id"],
    kind: "shape",
    shape,
    title: typeof record["title"] === "string" ? record["title"] : "Map shape",
    status,
    geometry,
    createdAt: typeof record["createdAt"] === "string" ? record["createdAt"] : "",
    createdBy: parseActor(record["createdBy"]) ?? { id: "unknown" },
    ...(style ? { style } : {}),
  };
}

function parseAnchor(value: unknown): AnnotationAnchor | null {
  if (!isRecord(value)) return null;
  if (value["kind"] === "map") {
    const lngLat = parseLngLat(value["lngLat"]);
    return lngLat ? { kind: "map", lngLat } : null;
  }
  if (value["kind"] === "feature" && typeof value["layerId"] === "string" && typeof value["featureId"] === "string") {
    const lngLat = parseLngLat(value["lngLat"]);
    return {
      kind: "feature",
      layerId: value["layerId"],
      featureId: value["featureId"],
      ...(typeof value["label"] === "string" ? { label: value["label"] } : {}),
      ...(lngLat ? { lngLat } : {}),
    };
  }
  return null;
}

function parseComment(value: unknown): AnnotationComment | null {
  if (!isRecord(value)) return null;
  if (typeof value["id"] !== "string" || typeof value["body"] !== "string") return null;
  const author = parseActor(value["author"]);
  if (!author || typeof value["createdAt"] !== "string") return null;
  return {
    id: value["id"],
    body: value["body"],
    author,
    createdAt: value["createdAt"],
  };
}

function createComment(id: string, body: string, actor: AnnotationActor, createdAt: string): AnnotationComment {
  return {
    id,
    body,
    author: cloneActor(actor),
    createdAt,
  };
}

function createModeration(input: AddMapThreadInput | AddFeatureThreadInput): AnnotationModeration {
  return {
    state: input.moderationState ?? "approved",
    submittedBy: input.submittedBy ?? (input.actor.id === "guest" ? "guest" : "member"),
  };
}

function shouldIncludeThread(thread: PortalAnnotationThread, options: AnnotationReadOptions): boolean {
  if (options.audience === "public") return thread.moderation.state === "approved";
  return true;
}

function parseActor(value: unknown): AnnotationActor | null {
  if (!isRecord(value) || typeof value["id"] !== "string") return null;
  return {
    id: value["id"],
    ...(typeof value["name"] === "string" ? { name: value["name"] } : {}),
  };
}

function parseThreadStatus(value: unknown): AnnotationThreadStatus | null {
  return value === "open" || value === "resolved" ? value : null;
}

function parseModerationState(value: unknown): AnnotationModerationState | null {
  return value === "approved" || value === "pending" || value === "hidden" ? value : null;
}

function parseShapeAnnotationType(value: unknown): ShapeAnnotationType | null {
  return value === "rectangle" || value === "polygon" || value === "freehand" ? value : null;
}

function parseShapeGeometry(value: unknown, fallbackType?: ShapeAnnotationType): ShapeAnnotationGeometry | null {
  if (!isRecord(value)) return null;
  const type = parseShapeAnnotationType(value["type"]) ?? fallbackType;
  if (!type || !Array.isArray(value["coordinates"])) return null;
  const coordinates =
    type === "freehand" ? parseLineString(value["coordinates"]) : parseLinearRing(value["coordinates"]);
  if (!coordinates) return null;
  return { type, coordinates };
}

function parseLineString(value: unknown[]): [number, number][] | null {
  const coordinates = parseCoordinateSequence(value);
  if (!coordinates) return null;
  try {
    return normalizeLineString(coordinates);
  } catch {
    return null;
  }
}

function parseLinearRing(value: unknown[]): [number, number][] | null {
  const coordinates = parseCoordinateSequence(value);
  if (!coordinates) return null;
  try {
    return normalizeLinearRing(coordinates);
  } catch {
    return null;
  }
}

function parseCoordinateSequence(value: unknown[]): [number, number][] | null {
  const coordinates = value.flatMap((entry) => {
    const lngLat = parseLngLat(entry);
    return lngLat ? [lngLat] : [];
  });
  if (coordinates.length !== value.length) return null;
  return coordinates;
}

function parseShapeStyle(value: unknown): ShapeAnnotationStyle | null {
  if (!isRecord(value)) return null;
  const style: ShapeAnnotationStyle = {};
  if (typeof value["strokeColor"] === "string") style.strokeColor = value["strokeColor"];
  if (typeof value["fillColor"] === "string") style.fillColor = value["fillColor"];
  if (typeof value["strokeWidth"] === "number" && Number.isFinite(value["strokeWidth"])) {
    style.strokeWidth = value["strokeWidth"];
  }
  if (typeof value["fillOpacity"] === "number" && Number.isFinite(value["fillOpacity"])) {
    style.fillOpacity = value["fillOpacity"];
  }
  return Object.keys(style).length > 0 ? style : null;
}

function parseLngLat(value: unknown): [number, number] | null {
  if (!Array.isArray(value) || value.length !== 2) return null;
  const [lng, lat] = value;
  if (typeof lng !== "number" || typeof lat !== "number") return null;
  if (!Number.isFinite(lng) || !Number.isFinite(lat)) return null;
  if (lng < -180 || lng > 180 || lat < -90 || lat > 90) return null;
  return [lng, lat];
}

function normalizeLngLat(value: [number, number]): [number, number] {
  const [lng, lat] = value;
  return [roundCoordinate(Math.max(-180, Math.min(180, lng))), roundCoordinate(Math.max(-90, Math.min(90, lat)))];
}

function normalizeShapeGeometry(shape: ShapeAnnotationType, coordinates: [number, number][]): ShapeAnnotationGeometry {
  return {
    type: shape,
    coordinates: shape === "freehand" ? normalizeLineString(coordinates) : normalizeLinearRing(coordinates),
  };
}

function normalizeLineString(coordinates: [number, number][]): [number, number][] {
  const line = coordinates.map(normalizeLngLat).filter((coordinate, index, normalized) => {
    const previous = normalized[index - 1];
    return !previous || previous[0] !== coordinate[0] || previous[1] !== coordinate[1];
  });
  const uniqueVertices = new Set(line.map((coordinate) => coordinate.join(",")));
  if (uniqueVertices.size < 2) throw new Error("Freehand annotation requires at least 2 unique coordinates.");
  return line;
}

function normalizeLinearRing(coordinates: [number, number][]): [number, number][] {
  if (coordinates.length < 3) throw new Error("Shape annotation requires at least 3 coordinates.");
  const ring = coordinates.map(normalizeLngLat);
  const first = ring[0];
  const last = ring.at(-1);
  if (first && last && (first[0] !== last[0] || first[1] !== last[1])) ring.push(first);
  const uniqueVertices = new Set(ring.slice(0, -1).map((coordinate) => coordinate.join(",")));
  if (uniqueVertices.size < 3) throw new Error("Shape annotation requires at least 3 unique coordinates.");
  return ring;
}

function normalizeShapeStyle(style: ShapeAnnotationStyle): ShapeAnnotationStyle {
  const normalized: ShapeAnnotationStyle = {};
  if (style.strokeColor) normalized.strokeColor = style.strokeColor;
  if (style.fillColor) normalized.fillColor = style.fillColor;
  if (typeof style.strokeWidth === "number" && Number.isFinite(style.strokeWidth)) {
    normalized.strokeWidth = Math.max(0, style.strokeWidth);
  }
  if (typeof style.fillOpacity === "number" && Number.isFinite(style.fillOpacity)) {
    normalized.fillOpacity = Math.max(0, Math.min(1, style.fillOpacity));
  }
  return normalized;
}

function roundCoordinate(value: number): number {
  return Math.round(value * 100_000) / 100_000;
}

function normalizeTitle(title: string): string {
  const normalized = title.trim();
  if (!normalized) throw new Error("Annotation title is required.");
  if (normalized.length > 120) throw new Error("Annotation title must be 120 characters or less.");
  return normalized;
}

function normalizeBody(body: string): string {
  const normalized = body.trim();
  if (!normalized) throw new Error("Annotation comment is required.");
  if (normalized.length > 1_000) throw new Error("Annotation comment must be 1000 characters or less.");
  return normalized;
}

function summarizeBody(body: string, fallback: string): string {
  const firstLine = body.split(/\r?\n/, 1)[0]?.trim() ?? "";
  if (!firstLine) return fallback;
  return firstLine.length > 72 ? `${firstLine.slice(0, 69)}...` : firstLine;
}

function nextId(workspace: AnnotationWorkspaceState, input: AnnotationEditContext, prefix: string): string {
  if (input.generateId) return input.generateId(prefix);
  const existing = new Set<string>();
  for (const record of [...workspace.annotationSets, ...workspace.commentThreads]) {
    if (typeof record["id"] === "string") existing.add(record["id"]);
  }
  let counter = Array.from(existing).filter((id) => id.startsWith(`${prefix}-`)).length + 1;
  let id = `${prefix}-${counter.toString().padStart(3, "0")}`;
  while (existing.has(id)) {
    counter += 1;
    id = `${prefix}-${counter.toString().padStart(3, "0")}`;
  }
  return id;
}

function nextCommentId(workspace: AnnotationWorkspaceState, input: AnnotationEditContext, threadId: string): string {
  if (input.generateId) return input.generateId("comment");
  const existing = new Set<string>();
  for (const thread of getAnnotationThreads(workspace)) {
    for (const comment of thread.comments) existing.add(comment.id);
  }
  let counter = existing.size + 1;
  let id = `${threadId}-comment-${counter.toString().padStart(3, "0")}`;
  while (existing.has(id)) {
    counter += 1;
    id = `${threadId}-comment-${counter.toString().padStart(3, "0")}`;
  }
  return id;
}

function nowIso(input: AnnotationEditContext): string {
  return (input.now ?? (() => new Date()))().toISOString();
}

function cloneWorkspace(workspace: AnnotationWorkspaceState): AnnotationWorkspaceState {
  return {
    version: ANNOTATION_WORKSPACE_VERSION as AnnotationWorkspaceVersion,
    visibility: {
      defaultAudience: "map",
      publicComments: workspace.visibility.publicComments,
    },
    annotationSets: workspace.annotationSets.map(cloneRecord),
    commentThreads: workspace.commentThreads.map(cloneRecord),
  };
}

function cloneActor(actor: AnnotationActor): AnnotationActor {
  return {
    id: actor.id,
    ...(actor.name ? { name: actor.name } : {}),
  };
}

function cloneComment(comment: AnnotationComment): AnnotationComment {
  return {
    id: comment.id,
    body: comment.body,
    author: cloneActor(comment.author),
    createdAt: comment.createdAt,
  };
}

function cloneRecord<T extends Record<string, unknown>>(record: T): T {
  if (typeof structuredClone === "function") return structuredClone(record);
  return JSON.parse(JSON.stringify(record)) as T;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === "object" && !Array.isArray(value);
}
