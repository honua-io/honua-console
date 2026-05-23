import type { AppPackage } from "@honua/sdk-js/operator";

import {
  type ContentItem,
  type HistoryEvent,
  type ServiceLink,
  type Ulid,
  summarize,
} from "../../transitional/content-item.js";
import {
  GENERATED_APP_EXTENSION,
  GENERATED_APP_EXTENSION_SCHEMA,
  type GeneratedAppArtifactRef,
  type GeneratedAppLifecycleExtension,
  type GeneratedAppLifecycleRecord,
  type GeneratedAppRevision,
  type GeneratedAppRevisionInput,
  type SaveGeneratedAppDraftInput,
} from "./types.js";

export interface GeneratedAppLifecycleContext {
  readonly consoleBaseUrl: string;
  readonly now?: string;
  readonly itemId?: Ulid;
  readonly revisionId?: string;
}

export interface GeneratedAppMutationContext {
  readonly consoleBaseUrl: string;
  readonly actor: string;
  readonly now?: string;
  readonly revisionId?: string;
}

let generatedItemCounter = 0;

export function materializeGeneratedAppDraft(
  input: SaveGeneratedAppDraftInput,
  context: GeneratedAppLifecycleContext,
): GeneratedAppLifecycleRecord {
  const now = context.now ?? new Date().toISOString();
  const id = input.id ?? context.itemId ?? issueGeneratedAppItemId();
  const revision = buildRevision(input, {
    consoleBaseUrl: context.consoleBaseUrl,
    itemId: id,
    sequence: 1,
    now,
    revisionId: context.revisionId,
  });
  const lifecycle: GeneratedAppLifecycleExtension = {
    schema: GENERATED_APP_EXTENSION_SCHEMA,
    state: input.unsupportedReason ? "unsupported" : "draft",
    source: {
      kind: input.source.kind,
      itemId: input.source.item.id,
      itemType: input.source.item.type,
      title: input.source.item.title,
    },
    activeRevisionId: revision.id,
    revisions: [revision],
    unsupportedReason: input.unsupportedReason ?? null,
  };
  const item: ContentItem = {
    id,
    slug: input.slug === undefined ? slugify(input.title) : input.slug,
    type: "app",
    title: input.title,
    summary: input.summary,
    description: input.description,
    tags: normalizeTags(input.tags ?? ["generated-app"]),
    owner: input.owner,
    timestamps: {
      created: now,
      modified: now,
      published: null,
      refreshed: null,
    },
    extent: input.source.item.extent,
    nativeCrs: null,
    license: input.source.item.license,
    attribution: input.source.item.attribution,
    source: {
      kind: "manual",
      sourceId: input.source.item.id,
      jobId: input.serverJob?.id ?? null,
      publishedBy: null,
      history: [historyEvent(now, "manual", input.actor)],
    },
    target: {
      type: "app",
      url: revision.previewUrl,
      framework: "honua",
    },
    endpoints: {
      self: selfLink(context.consoleBaseUrl, id),
      geoservices: null,
      ogcFeatures: null,
      stac: null,
      tiles: null,
    },
    preview: {
      thumbnail: input.source.item.preview.thumbnail,
      image: input.source.item.preview.image,
    },
    capabilities: ["render"],
    dependencies: [{ id: input.source.item.id, type: input.source.item.type, role: input.source.kind }],
    access: { sharing: "private", embeddable: false, openData: false },
    extensions: withGeneratedAppLifecycle({}, lifecycle),
  };
  return toGeneratedAppLifecycleRecord(item);
}

export function addGeneratedAppRevision(
  item: ContentItem,
  input: GeneratedAppRevisionInput,
  context: GeneratedAppMutationContext,
): GeneratedAppLifecycleRecord {
  const lifecycle = requireGeneratedAppLifecycle(item);
  const now = context.now ?? new Date().toISOString();
  const nextSequence = Math.max(...lifecycle.revisions.map((revision) => revision.sequence)) + 1;
  const revision = buildRevision(input, {
    consoleBaseUrl: context.consoleBaseUrl,
    itemId: item.id,
    sequence: nextSequence,
    now,
    revisionId: context.revisionId,
  });
  const nextLifecycle: GeneratedAppLifecycleExtension = {
    ...lifecycle,
    activeRevisionId: revision.id,
    revisions: [...lifecycle.revisions, revision],
  };
  return toGeneratedAppLifecycleRecord({
    ...item,
    timestamps: { ...item.timestamps, modified: now },
    source: {
      ...item.source,
      jobId: input.serverJob?.id ?? item.source.jobId,
      history: [...item.source.history, historyEvent(now, "update", input.actor)],
    },
    target: item.target.type === "app" ? { ...item.target, url: revision.previewUrl } : item.target,
    extensions: withGeneratedAppLifecycle(item.extensions, nextLifecycle),
  });
}

export function publishGeneratedAppItem(
  item: ContentItem,
  context: GeneratedAppMutationContext,
): GeneratedAppLifecycleRecord {
  const lifecycle = requireGeneratedAppLifecycle(item);
  if (lifecycle.state === "unsupported") {
    throw new GeneratedAppLifecycleContractError(lifecycle.unsupportedReason ?? "Generated app is not publishable.");
  }
  const activeRevision = requireActiveGeneratedAppRevision(lifecycle);
  const now = context.now ?? new Date().toISOString();
  const nextLifecycle: GeneratedAppLifecycleExtension = {
    ...lifecycle,
    state: "published",
    unsupportedReason: null,
  };
  return toGeneratedAppLifecycleRecord({
    ...item,
    timestamps: {
      ...item.timestamps,
      modified: now,
      published: item.timestamps.published ?? now,
    },
    source: {
      ...item.source,
      publishedBy: context.actor,
      history: [...item.source.history, historyEvent(now, "publish", context.actor)],
    },
    target: item.target.type === "app" ? { ...item.target, url: activeRevision.previewUrl } : item.target,
    extensions: withGeneratedAppLifecycle(item.extensions, nextLifecycle),
  });
}

export function rollbackGeneratedAppItem(
  item: ContentItem,
  targetRevisionId: string,
  context: GeneratedAppMutationContext,
): GeneratedAppLifecycleRecord {
  const lifecycle = requireGeneratedAppLifecycle(item);
  const targetRevision = lifecycle.revisions.find((revision) => revision.id === targetRevisionId);
  if (!targetRevision) {
    throw new GeneratedAppLifecycleContractError(`Unknown generated app revision: ${targetRevisionId}`);
  }
  const now = context.now ?? new Date().toISOString();
  const nextLifecycle: GeneratedAppLifecycleExtension = {
    ...lifecycle,
    activeRevisionId: targetRevision.id,
  };
  return toGeneratedAppLifecycleRecord({
    ...item,
    timestamps: { ...item.timestamps, modified: now },
    source: {
      ...item.source,
      history: [...item.source.history, historyEvent(now, "update", context.actor)],
    },
    target: item.target.type === "app" ? { ...item.target, url: targetRevision.previewUrl } : item.target,
    extensions: withGeneratedAppLifecycle(item.extensions, nextLifecycle),
  });
}

export function toGeneratedAppLifecycleRecord(item: ContentItem): GeneratedAppLifecycleRecord {
  const lifecycle = requireGeneratedAppLifecycle(item);
  const activeRevision = requireActiveGeneratedAppRevision(lifecycle);
  return {
    item,
    summary: summarize(item),
    lifecycle,
    activeRevision,
  };
}

export function readGeneratedAppLifecycle(item: ContentItem): GeneratedAppLifecycleExtension | null {
  if (item.type !== "app") return null;
  const raw = item.extensions[GENERATED_APP_EXTENSION] as unknown;
  if (!isGeneratedAppLifecycleExtension(raw)) return null;
  return raw;
}

export function previousGeneratedAppRevision(lifecycle: GeneratedAppLifecycleExtension): GeneratedAppRevision | null {
  const active = requireActiveGeneratedAppRevision(lifecycle);
  const previous = lifecycle.revisions
    .filter((revision) => revision.sequence < active.sequence)
    .sort((a, b) => b.sequence - a.sequence)[0];
  return previous ?? null;
}

export function buildGeneratedAppPreviewUrl(consoleBaseUrl: string, itemId: string, revisionId: string): string {
  const url = new URL(`/studio/apps/${encodeURIComponent(itemId)}/preview`, normalizeBaseUrl(consoleBaseUrl));
  url.searchParams.set("revision", revisionId);
  return url.toString();
}

export class GeneratedAppLifecycleContractError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "GeneratedAppLifecycleContractError";
  }
}

function buildRevision(
  input: GeneratedAppRevisionInput,
  context: {
    consoleBaseUrl: string;
    itemId: string;
    sequence: number;
    now: string;
    revisionId?: string;
  },
): GeneratedAppRevision {
  const id = context.revisionId ?? `rev-${String(context.sequence).padStart(3, "0")}`;
  return {
    id,
    sequence: context.sequence,
    label: input.label ?? `Revision ${context.sequence}`,
    createdAt: context.now,
    actor: input.actor,
    manifestVersion: input.manifestVersion,
    buildSpecRef: input.buildSpecRef,
    planRef: {
      id: input.plan.id,
      artifact: input.planArtifact ?? null,
      warnings: input.plan.warnings ?? [],
    },
    appPackageRef: selectAppPackageArtifact(input.appPackage),
    manifestArtifact: input.manifestArtifact,
    serverJob: input.serverJob ?? null,
    provenance: input.provenance ?? [],
    previewUrl: buildGeneratedAppPreviewUrl(context.consoleBaseUrl, context.itemId, id),
    rollbackOf: null,
  };
}

function selectAppPackageArtifact(appPackage: AppPackage): GeneratedAppArtifactRef {
  const appAsset = appPackage.assets.find((asset) => asset.kind === "app-package");
  const urlAsset = appPackage.assets.find((asset) => asset.url);
  return {
    id: appAsset?.id ?? urlAsset?.id ?? appPackage.id,
    kind: appAsset?.kind ?? urlAsset?.kind ?? "app-package",
    ...((appAsset?.url ?? urlAsset?.url) ? { url: appAsset?.url ?? urlAsset?.url } : {}),
    version: appPackage.version,
  };
}

function requireGeneratedAppLifecycle(item: ContentItem): GeneratedAppLifecycleExtension {
  const lifecycle = readGeneratedAppLifecycle(item);
  if (!lifecycle) {
    throw new GeneratedAppLifecycleContractError(`Item ${item.id} is not a generated app lifecycle item.`);
  }
  return lifecycle;
}

function requireActiveGeneratedAppRevision(lifecycle: GeneratedAppLifecycleExtension): GeneratedAppRevision {
  const activeRevision = lifecycle.revisions.find((revision) => revision.id === lifecycle.activeRevisionId);
  if (!activeRevision) {
    throw new GeneratedAppLifecycleContractError(
      `Generated app active revision ${lifecycle.activeRevisionId} is missing from revision history.`,
    );
  }
  return activeRevision;
}

function withGeneratedAppLifecycle(
  existing: ContentItem["extensions"],
  lifecycle: GeneratedAppLifecycleExtension,
): ContentItem["extensions"] {
  return {
    ...existing,
    [GENERATED_APP_EXTENSION]: lifecycle as unknown as Readonly<Record<string, unknown>>,
  };
}

function selfLink(consoleBaseUrl: string, itemId: string): ServiceLink {
  return {
    accessURL: new URL(`/catalog/${encodeURIComponent(itemId)}`, normalizeBaseUrl(consoleBaseUrl)).toString(),
    format: "Honua:Console:v1",
    mediaType: "application/json",
    describedBy: null,
    describedByType: null,
    conformsTo: ["https://schemas.honua.io/content-item/v1"],
  };
}

function historyEvent(at: string, kind: HistoryEvent["kind"], actor: string): HistoryEvent {
  return { at, kind, actor };
}

function normalizeBaseUrl(consoleBaseUrl: string): string {
  return consoleBaseUrl.replace(/\/+$/, "") || "https://console.honua.example";
}

function normalizeTags(tags: readonly string[]): readonly string[] {
  const seen = new Set<string>();
  const normalized: string[] = [];
  for (const tag of tags) {
    const value = tag.trim();
    if (!value || seen.has(value)) continue;
    seen.add(value);
    normalized.push(value);
  }
  return normalized.length > 0 ? normalized : ["generated-app"];
}

function slugify(title: string): string {
  const slug = title
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 96);
  return slug || "generated-app";
}

function issueGeneratedAppItemId(): Ulid {
  generatedItemCounter += 1;
  return `01J7APPS${String(generatedItemCounter).padStart(18, "0")}`;
}

function isGeneratedAppLifecycleExtension(value: unknown): value is GeneratedAppLifecycleExtension {
  if (!value || typeof value !== "object") return false;
  const candidate = value as Partial<GeneratedAppLifecycleExtension>;
  return (
    candidate.schema === GENERATED_APP_EXTENSION_SCHEMA &&
    (candidate.state === "draft" || candidate.state === "published" || candidate.state === "unsupported") &&
    Boolean(candidate.source) &&
    typeof candidate.activeRevisionId === "string" &&
    Array.isArray(candidate.revisions) &&
    candidate.revisions.every(isGeneratedAppRevision)
  );
}

function isGeneratedAppRevision(value: unknown): value is GeneratedAppRevision {
  if (!value || typeof value !== "object") return false;
  const candidate = value as Partial<GeneratedAppRevision>;
  return (
    typeof candidate.id === "string" &&
    typeof candidate.sequence === "number" &&
    typeof candidate.createdAt === "string" &&
    typeof candidate.actor === "string" &&
    typeof candidate.manifestVersion === "string" &&
    typeof candidate.previewUrl === "string" &&
    Boolean(candidate.buildSpecRef) &&
    Boolean(candidate.planRef) &&
    Boolean(candidate.appPackageRef) &&
    Boolean(candidate.manifestArtifact)
  );
}
