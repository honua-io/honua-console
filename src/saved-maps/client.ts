/**
 * Saved-map client interface plus a browser-safe in-memory fixture
 * implementation used in dev and tests.
 *
 * The HTTP implementation lives behind a feature flag and forwards to the
 * server endpoints recorded as a child ticket on honua-server. Until the
 * server side ships, `FixtureSavedMapClient` is the production default.
 */

import { SUMMARY_MAX_LENGTH, type ServiceLink, TAG_MAX_LENGTH, TITLE_MAX_LENGTH } from "../contracts/content-item.js";
import { assertContentItemId, createContentItemIdGenerator } from "../contracts/ids.js";
import { safeHttpUrl } from "../security/url.js";
import { cloneWebMapDoc, viewerStateToWebMapDoc } from "./serializer.js";
import type {
  ContentDependency,
  DuplicateMapInput,
  RenameMapInput,
  SaveMapInput,
  SavedMapItem,
  SharingLevel,
  WebMapDoc,
} from "./types.js";

export interface SavedMapClientContext {
  /** Authenticated actor id used as `owner.id` and history `actor`. */
  actorId: string;
  /** Display name surfaced in `owner.name`. */
  actorDisplayName?: string;
  /** Owner kind; defaults to "user". */
  actorKind?: "user" | "group" | "org";
  /** Now() override for deterministic tests. */
  now?: () => Date;
  /** Id generator override for deterministic tests. */
  generateId?: () => string;
}

export interface ListOptions {
  ownerId?: string;
  limit?: number;
  cursor?: string | null;
}

export interface ListResult {
  items: SavedMapItem[];
  nextCursor: string | null;
}

export interface SavedMapClient {
  create(input: SaveMapInput): Promise<SavedMapItem>;
  get(id: string): Promise<SavedMapItem | null>;
  getWebMap(id: string): Promise<WebMapDoc | null>;
  list(options?: ListOptions): Promise<ListResult>;
  patchMetadata(input: RenameMapInput): Promise<SavedMapItem>;
  replaceContent(id: string, doc: WebMapDoc): Promise<SavedMapItem>;
  duplicate(input: DuplicateMapInput): Promise<SavedMapItem>;
  delete(id: string): Promise<void>;
  uploadThumbnail(id: string, blob: Blob): Promise<string>;
}

export class SavedMapNotFoundError extends Error {
  override readonly name = "SavedMapNotFoundError";
  constructor(public readonly id: string) {
    super(`Saved map not found: ${id}`);
  }
}

export class SavedMapForbiddenError extends Error {
  override readonly name = "SavedMapForbiddenError";
  constructor(
    public readonly id: string,
    public readonly action: string,
  ) {
    super(`Forbidden: ${action} on ${id}`);
  }
}

interface FixtureRecord {
  item: SavedMapItem;
  doc: WebMapDoc;
}

const DEFAULT_BASEMAP_REQUIRED_ROLE = "baseMap" as const;
const DEFAULT_SUMMARY = "No summary provided.";
const DEFAULT_CONSOLE_ORIGIN = "https://console.honua.example";
const DEFAULT_API_ORIGIN = "https://api.honua.example";
const DEFAULT_LICENSE = {
  spdx: null,
  name: "Unspecified",
  url: null,
};

export class FixtureSavedMapClient implements SavedMapClient {
  private readonly records = new Map<string, FixtureRecord>();
  private readonly thumbnails = new Map<string, Blob>();
  private readonly ctx: Required<Pick<SavedMapClientContext, "actorId" | "actorKind" | "now" | "generateId">> & {
    actorDisplayName?: string;
  };

  constructor(ctx: SavedMapClientContext) {
    this.ctx = {
      actorId: ctx.actorId,
      ...(ctx.actorDisplayName !== undefined ? { actorDisplayName: ctx.actorDisplayName } : {}),
      actorKind: ctx.actorKind ?? "user",
      now: ctx.now ?? (() => new Date()),
      generateId: ctx.generateId ?? defaultIdGenerator(),
    };
  }

  async create(input: SaveMapInput): Promise<SavedMapItem> {
    const title = validateTitle(input.title);
    const summary = validateSummary(input.summary);
    const tags = validateTags(input.tags);
    const id = assertContentItemId(this.ctx.generateId(), "generated saved-map id");
    const now = this.ctx.now().toISOString();
    const sharing: SharingLevel = input.sharing ?? "private";
    const doc = structuredCloneDoc(viewerStateOrThrow(input));
    const dependencies = collectDependencies(doc);
    const extent = extentFromDoc(doc);
    const item: SavedMapItem = {
      id,
      slug: null,
      type: "map",
      title,
      summary,
      description: summary,
      tags,
      owner: {
        id: this.ctx.actorId,
        name: this.ctx.actorDisplayName ?? this.ctx.actorId,
        kind: this.ctx.actorKind,
      },
      timestamps: { created: now, modified: now, published: null, refreshed: null },
      extent,
      nativeCrs: null,
      license: { ...DEFAULT_LICENSE },
      attribution: null,
      source: {
        kind: "manual",
        sourceId: null,
        jobId: null,
        publishedBy: this.ctx.actorId,
        history: [{ at: now, kind: "manual", actor: this.ctx.actorId }],
      },
      target: {
        type: "map",
        webmapJsonRef: webmapJsonRefFor(id),
        operationalLayerCount: doc.operationalLayers.length,
      },
      endpoints: { self: portalLink(mapSelfUrlFor(id)), geoservices: null, ogcFeatures: null, stac: null, tiles: null },
      preview: { thumbnail: null, image: null },
      capabilities: ["render"],
      dependencies,
      access: { sharing, embeddable: false, openData: false },
      extensions: {},
    };

    if (input.thumbnail) {
      this.thumbnails.set(id, input.thumbnail);
      item.preview = {
        thumbnail: thumbnailUrlFor(id, now),
        image: null,
      };
    }

    this.records.set(id, { item, doc });
    return cloneItem(item);
  }

  async get(id: string): Promise<SavedMapItem | null> {
    const record = this.records.get(id);
    if (!record || record.item.timestamps.deleted) return null;
    if (!this.canRead(record.item)) {
      throw new SavedMapForbiddenError(id, "get");
    }
    return cloneItem(record.item);
  }

  async getWebMap(id: string): Promise<WebMapDoc | null> {
    const record = this.records.get(id);
    if (!record || record.item.timestamps.deleted) return null;
    if (!this.canRead(record.item)) {
      throw new SavedMapForbiddenError(id, "getWebMap");
    }
    return structuredCloneDoc(record.doc);
  }

  async list(options: ListOptions = {}): Promise<ListResult> {
    const items = Array.from(this.records.values())
      .filter((r) => !r.item.timestamps.deleted)
      .filter((r) => this.canRead(r.item))
      .filter((r) => !options.ownerId || r.item.owner.id === options.ownerId)
      .map((r) => cloneItem(r.item))
      .sort((a, b) => b.timestamps.modified.localeCompare(a.timestamps.modified));
    const limit = options.limit ?? items.length;
    return { items: items.slice(0, limit), nextCursor: null };
  }

  async patchMetadata(input: RenameMapInput): Promise<SavedMapItem> {
    const record = this.requireOwned(input.id, "patchMetadata");
    const next: SavedMapItem = {
      ...record.item,
      ...(input.title !== undefined ? { title: validateTitle(input.title) } : {}),
      ...(input.summary !== undefined ? { summary: validateSummary(input.summary) } : {}),
      ...(input.summary !== undefined ? { description: validateSummary(input.summary) } : {}),
      ...(input.tags !== undefined ? { tags: validateTags(input.tags) } : {}),
      timestamps: {
        ...record.item.timestamps,
        modified: this.ctx.now().toISOString(),
      },
      source: {
        ...record.item.source,
        history: [
          ...record.item.source.history,
          {
            at: this.ctx.now().toISOString(),
            kind: "metadata-edit",
            actor: this.ctx.actorId,
          },
        ],
      },
    };
    this.records.set(input.id, { item: next, doc: record.doc });
    return cloneItem(next);
  }

  async replaceContent(id: string, doc: WebMapDoc): Promise<SavedMapItem> {
    const record = this.requireOwned(id, "replaceContent");
    const cloned = structuredCloneDoc(doc);
    const dependencies = collectDependencies(cloned);
    const next: SavedMapItem = {
      ...record.item,
      timestamps: {
        ...record.item.timestamps,
        modified: this.ctx.now().toISOString(),
      },
      target: {
        type: "map",
        webmapJsonRef: record.item.target.webmapJsonRef,
        operationalLayerCount: cloned.operationalLayers.length,
      },
      extent: extentFromDoc(cloned),
      dependencies,
      source: {
        ...record.item.source,
        history: [
          ...record.item.source.history,
          {
            at: this.ctx.now().toISOString(),
            kind: "update",
            actor: this.ctx.actorId,
          },
        ],
      },
    };
    this.records.set(id, { item: next, doc: cloned });
    return cloneItem(next);
  }

  async duplicate(input: DuplicateMapInput): Promise<SavedMapItem> {
    const source = this.records.get(input.fromId);
    if (!source || source.item.timestamps.deleted) {
      throw new SavedMapNotFoundError(input.fromId);
    }
    if (!this.canRead(source.item)) {
      throw new SavedMapForbiddenError(input.fromId, "duplicate");
    }
    const newId = assertContentItemId(this.ctx.generateId(), "generated saved-map id");
    const now = this.ctx.now().toISOString();
    const clonedDoc = cloneWebMapDoc(source.doc);
    const title = input.title !== undefined ? validateTitle(input.title) : defaultDuplicateTitle(source.item.title);
    // Re-key the preview URL to the duplicate's id when we carry the blob
    // forward so deleting or restricting the original cannot break the
    // duplicate's catalog card.
    const sourceBlob = this.thumbnails.get(input.fromId);
    const previewThumbnail = sourceBlob ? thumbnailUrlFor(newId, now) : null;
    const item: SavedMapItem = {
      id: newId,
      slug: null,
      type: "map",
      title,
      summary: source.item.summary,
      description: source.item.description,
      tags: [...source.item.tags],
      owner: {
        id: this.ctx.actorId,
        name: this.ctx.actorDisplayName ?? this.ctx.actorId,
        kind: this.ctx.actorKind,
      },
      timestamps: { created: now, modified: now, published: null, refreshed: null },
      extent: source.item.extent ? { ...source.item.extent, bbox: [...source.item.extent.bbox] } : null,
      nativeCrs: source.item.nativeCrs,
      license: { ...source.item.license },
      attribution: source.item.attribution,
      source: {
        kind: "manual",
        sourceId: input.fromId,
        jobId: null,
        publishedBy: this.ctx.actorId,
        history: [{ at: now, kind: "manual", actor: this.ctx.actorId }],
      },
      target: {
        type: "map",
        webmapJsonRef: webmapJsonRefFor(newId),
        operationalLayerCount: clonedDoc.operationalLayers.length,
      },
      endpoints: {
        self: portalLink(mapSelfUrlFor(newId)),
        geoservices: null,
        ogcFeatures: null,
        stac: null,
        tiles: null,
      },
      preview: {
        thumbnail: previewThumbnail,
        image: source.item.preview.image,
      },
      capabilities: [...source.item.capabilities],
      dependencies: source.item.dependencies.map((d) => ({ ...d })),
      access: { sharing: "private", embeddable: false, openData: false },
      extensions: cloneItem(source.item.extensions),
    };
    this.records.set(newId, { item, doc: clonedDoc });
    if (sourceBlob) {
      this.thumbnails.set(newId, sourceBlob);
    }
    return cloneItem(item);
  }

  async delete(id: string): Promise<void> {
    const record = this.requireOwned(id, "delete");
    const now = this.ctx.now().toISOString();
    this.records.set(id, {
      item: {
        ...record.item,
        timestamps: { ...record.item.timestamps, deleted: now, modified: now },
        source: {
          ...record.item.source,
          history: [...record.item.source.history, { at: now, kind: "update", actor: this.ctx.actorId }],
        },
      },
      doc: record.doc,
    });
    this.thumbnails.delete(id);
  }

  async uploadThumbnail(id: string, blob: Blob): Promise<string> {
    const record = this.requireOwned(id, "uploadThumbnail");
    this.thumbnails.set(id, blob);
    const now = this.ctx.now().toISOString();
    const url = thumbnailUrlFor(id, now);
    this.records.set(id, {
      item: {
        ...record.item,
        timestamps: { ...record.item.timestamps, modified: now },
        preview: { thumbnail: url, image: record.item.preview.image },
      },
      doc: record.doc,
    });
    return url;
  }

  /** Test-only: read the stored thumbnail blob. */
  _peekThumbnail(id: string): Blob | undefined {
    return this.thumbnails.get(id);
  }

  /** Test-only: read the raw record. */
  _peekRecord(id: string): FixtureRecord | undefined {
    const record = this.records.get(id);
    if (!record) return undefined;
    return { item: cloneItem(record.item), doc: structuredCloneDoc(record.doc) };
  }

  private requireOwned(id: string, action: string): FixtureRecord {
    const record = this.records.get(id);
    if (!record || record.item.timestamps.deleted) {
      throw new SavedMapNotFoundError(id);
    }
    if (record.item.owner.id !== this.ctx.actorId) {
      throw new SavedMapForbiddenError(id, action);
    }
    return record;
  }

  /**
   * Read permission helper.
   *
   * Mirrors the saved-map permission model recorded in the design brief:
   * the owner can always read; other actors can read items shared at
   * `org` (any authenticated actor in the fixture, since membership is a
   * server concern), `public-link`, or `public`. Mutation paths still go
   * through `requireOwned` — only the owner can patch/delete/replace.
   *
   * The fixture's `org` rule is intentionally permissive (any non-empty
   * actorId): real organization membership lives server-side. The HTTP
   * client mirrors the *shape* of these decisions and trusts the server
   * for the org-membership check.
   */
  private canRead(item: SavedMapItem): boolean {
    if (item.owner.id === this.ctx.actorId) return true;
    switch (item.access.sharing) {
      case "public":
      case "public-link":
        return true;
      case "org":
      case "group":
        return this.ctx.actorId.length > 0;
      case "private":
        return false;
    }
  }
}

function viewerStateOrThrow(input: SaveMapInput): WebMapDoc {
  if (!input.state) throw new Error("create: state is required");
  return viewerStateToWebMapDoc(input.state);
}

function collectDependencies(doc: WebMapDoc): ContentDependency[] {
  const deps: ContentDependency[] = [];
  const seen = new Set<string>();
  for (const layer of doc.operationalLayers) {
    const itemId = layer.sourceRef.itemId;
    const key = `layer:${itemId}`;
    if (!seen.has(key)) {
      seen.add(key);
      deps.push({
        id: assertContentItemId(itemId, "operational layer dependency id"),
        type: "layer",
        role: "operationalLayer",
      });
    }
    if (layer.styleRef?.itemId) {
      const styleKey = `style:${layer.styleRef.itemId}`;
      if (!seen.has(styleKey)) {
        seen.add(styleKey);
        deps.push({
          id: assertContentItemId(layer.styleRef.itemId, "style dependency id"),
          type: "document",
          role: "style",
        });
      }
    }
  }
  for (const baseLayer of doc.baseMap.baseMapLayers) {
    if (baseLayer.sourceRef?.itemId) {
      const key = `service:${baseLayer.sourceRef.itemId}`;
      if (!seen.has(key)) {
        seen.add(key);
        deps.push({
          id: assertContentItemId(baseLayer.sourceRef.itemId, "base map dependency id"),
          type: "service",
          role: DEFAULT_BASEMAP_REQUIRED_ROLE,
        });
      }
    }
  }
  return deps;
}

/**
 * Project a WebMap viewpoint extent into the WGS84 lon/lat bbox stored on
 * the catalog ContentItem. The portal contract is that `ContentItem.extent`
 * is always WGS84 (EPSG:4326) so catalog cards, search filters, and
 * open-data exports can compare bounds without a per-item CRS lookup. We
 * normalize the two CRSes that show up in real WebMaps (4326 and Web
 * Mercator 3857/102100); anything else is treated as unknown extent so we
 * never publish meter coordinates as lon/lat bounds.
 */
function extentFromDoc(doc: WebMapDoc): { bbox: [number, number, number, number]; crs: "EPSG:4326" } | null {
  const e = doc.initialState.viewpoint.extent;
  const wkid =
    e.spatialReference?.latestWkid ??
    e.spatialReference?.wkid ??
    doc.spatialReference?.latestWkid ??
    doc.spatialReference?.wkid;
  let bbox: [number, number, number, number] | null = null;
  if (wkid === undefined || wkid === 4326) {
    bbox = [e.xmin, e.ymin, e.xmax, e.ymax];
  } else if (wkid === 3857 || wkid === 102100) {
    const sw = webMercatorToWgs84(e.xmin, e.ymin);
    const ne = webMercatorToWgs84(e.xmax, e.ymax);
    bbox = [sw.lng, sw.lat, ne.lng, ne.lat];
  }

  if (!bbox || !isFiniteWgs84Bbox(bbox)) return null;
  return { bbox, crs: "EPSG:4326" };
}

/** WGS84 semi-major axis in metres; the only constant the inverse needs. */
const WEB_MERCATOR_R = 6378137;

function webMercatorToWgs84(x: number, y: number): { lng: number; lat: number } {
  const lng = (x / WEB_MERCATOR_R) * (180 / Math.PI);
  const lat = (2 * Math.atan(Math.exp(y / WEB_MERCATOR_R)) - Math.PI / 2) * (180 / Math.PI);
  return { lng, lat };
}

function isFiniteWgs84Bbox(bbox: readonly [number, number, number, number]): boolean {
  const [west, south, east, north] = bbox;
  return (
    Number.isFinite(west) &&
    Number.isFinite(south) &&
    Number.isFinite(east) &&
    Number.isFinite(north) &&
    west >= -180 &&
    west <= 180 &&
    east >= -180 &&
    east <= 180 &&
    south >= -90 &&
    south <= 90 &&
    north >= -90 &&
    north <= 90 &&
    west <= east &&
    south <= north
  );
}

function webmapJsonRefFor(id: string): string {
  return `/api/v1/portal/maps/${id}/webmap`;
}

function thumbnailUrlFor(id: string, modified: string): string {
  const url = new URL(`/api/v1/portal/maps/${encodeURIComponent(id)}/thumb.png`, DEFAULT_API_ORIGIN);
  url.searchParams.set("v", modified);
  return url.toString();
}

function mapSelfUrlFor(id: string): string {
  return new URL(`/maps/${encodeURIComponent(id)}`, DEFAULT_CONSOLE_ORIGIN).toString();
}

function structuredCloneDoc(doc: WebMapDoc): WebMapDoc {
  if (typeof structuredClone === "function") return structuredClone(doc);
  return JSON.parse(JSON.stringify(doc)) as WebMapDoc;
}

function cloneItem<T>(item: T): T {
  if (typeof structuredClone === "function") return structuredClone(item);
  return JSON.parse(JSON.stringify(item)) as T;
}

function defaultIdGenerator(): () => string {
  return createContentItemIdGenerator();
}

function portalLink(accessURL: string): ServiceLink {
  const safeAccessURL = safeHttpUrl(accessURL);
  if (!safeAccessURL) {
    throw new Error("saved-map self accessURL must be an absolute http(s) URL");
  }
  return {
    accessURL: safeAccessURL,
    format: "Honua:Portal:v1",
    mediaType: "text/html",
    describedBy: null,
    describedByType: null,
    conformsTo: ["https://schemas.honua.io/content-item/v1"],
  };
}

// ── Metadata validation ─────────────────────────────────────────────────────
// Mirrors content-item/v1 via the shared constants in
// `src/contracts/content-item.ts`. Any write path that touches these fields
// must run them through these helpers so we never persist a record that
// violates the repo's own schema.

const TITLE_MAX = TITLE_MAX_LENGTH;
const SUMMARY_MAX = SUMMARY_MAX_LENGTH;
const TAG_MAX = TAG_MAX_LENGTH;
const DUPLICATE_SUFFIX = " (copy)";

function validateTitle(title: unknown): string {
  if (typeof title !== "string" || title.trim().length === 0) {
    throw new Error("title is required");
  }
  if (title.length > TITLE_MAX) {
    throw new Error(`title exceeds ${TITLE_MAX} characters (got ${title.length})`);
  }
  return title;
}

function validateSummary(summary: unknown): string {
  if (summary == null) return DEFAULT_SUMMARY;
  if (typeof summary !== "string") {
    throw new Error("summary must be a string or null");
  }
  if (summary.length > SUMMARY_MAX) {
    throw new Error(`summary exceeds ${SUMMARY_MAX} characters (got ${summary.length})`);
  }
  return summary.trim().length === 0 ? DEFAULT_SUMMARY : summary;
}

function validateTags(tags: unknown): string[] {
  if (tags == null) return [];
  if (!Array.isArray(tags)) {
    throw new Error("tags must be an array of strings");
  }
  const seen = new Set<string>();
  const out: string[] = [];
  for (const tag of tags) {
    if (typeof tag !== "string" || tag.length === 0) continue;
    if (tag.length > TAG_MAX) {
      throw new Error(`tag exceeds ${TAG_MAX} characters: ${JSON.stringify(tag)} (got ${tag.length})`);
    }
    if (seen.has(tag)) continue;
    seen.add(tag);
    out.push(tag);
  }
  return out;
}

function defaultDuplicateTitle(sourceTitle: string): string {
  const max = TITLE_MAX - DUPLICATE_SUFFIX.length;
  const base = sourceTitle.length > max ? sourceTitle.slice(0, max) : sourceTitle;
  return `${base}${DUPLICATE_SUFFIX}`;
}
