/**
 * Catalog client surface — the consumer half of `content-item/v1`.
 *
 * Two implementations ship here:
 *
 * - {@link FixtureCatalogClient} — default for dev/tests/local demos. Reads
 *   the golden fixtures under `fixtures/catalog/`.
 * - {@link HttpCatalogClient} — production transport that calls the portal
 *   item endpoints documented in `docs/contracts/content-item-v1.md`. Stays
 *   behind a feature flag until the `honua-server` child ticket lands.
 *
 * The split keeps the catalog UI decoupled from the transport so the same
 * pages and dependency walker work in tests, in storybook-style demos, and
 * in production.
 */

import {
  CatalogError,
  type CatalogErrorEnvelope,
  type ContentItem,
  type ContentItemSummary,
  type GetDependenciesResponse,
  type ListItemsRequest,
  type ListItemsResponse,
  type PublishHandoff,
  type SortOption,
  summarize,
} from "../contracts/content-item.js";
import { FixtureShareClient, type PatchAccessRequest } from "../share/client.js";
import type {
  ClosureItem,
  ClosureNode,
  DependencyRole,
  DependencyType,
  PatchAccessResult,
  ShareAccess,
} from "../share/types.js";
import { getDependencyClosure, isUnsupportedByExtension } from "./dependencies.js";

export interface CatalogClient {
  listItems(request?: ListItemsRequest): Promise<ListItemsResponse>;
  getItem(id: string): Promise<ContentItem>;
  getDependencies(id: string, options?: { depth?: number; limit?: number }): Promise<GetDependenciesResponse>;
  patchAccess?(request: PatchAccessRequest): Promise<PatchAccessResult>;
}

// ── Fixture client ──────────────────────────────────────────────

export interface FixtureCatalogData {
  readonly items: ReadonlyMap<string, ContentItem>;
  readonly listOrder: readonly string[];
  readonly unauthorizedIds: ReadonlySet<string>;
  readonly unsupportedIds: ReadonlySet<string>;
}

export class FixtureCatalogClient implements CatalogClient {
  private readonly items: Map<string, ContentItem>;
  private readonly listOrder: readonly string[];
  private readonly unauthorizedIds: ReadonlySet<string>;
  private readonly unsupportedIds: ReadonlySet<string>;

  constructor(data: FixtureCatalogData) {
    this.items = new Map(data.items);
    this.listOrder = data.listOrder;
    this.unauthorizedIds = data.unauthorizedIds;
    this.unsupportedIds = data.unsupportedIds;
  }

  async listItems(request: ListItemsRequest = {}): Promise<ListItemsResponse> {
    const limit = clampListLimit(request.limit ?? 24);
    const filtered = this.listOrder
      .map((id) => this.items.get(id))
      .filter((item): item is ContentItem => Boolean(item))
      .filter((item) => matchesRequest(item, request));

    const sorted = sortItems(filtered, request);
    const start = parseCursor(request.cursor ?? null);
    const slice = sorted.slice(start, start + limit);
    const nextCursor = start + limit < sorted.length ? String(start + limit) : null;

    return {
      items: slice.map(summarize),
      nextCursor,
    };
  }

  async getItem(idOrSlug: string): Promise<ContentItem> {
    if (this.unauthorizedIds.has(idOrSlug)) {
      throw new CatalogError("unauthorized", `caller cannot view item ${idOrSlug}`);
    }
    const item = this.items.get(idOrSlug);
    if (item) return item;
    for (const candidate of this.items.values()) {
      if (candidate.slug !== idOrSlug) continue;
      if (this.unauthorizedIds.has(candidate.id)) {
        throw new CatalogError("unauthorized", `caller cannot view item ${candidate.id}`);
      }
      return candidate;
    }
    throw new CatalogError("missing", `no item with id ${idOrSlug}`);
  }

  async getDependencies(
    id: string,
    options: { depth?: number; limit?: number } = {},
  ): Promise<GetDependenciesResponse> {
    const root = await this.getItem(id);
    const unsupportedIds = this.unsupportedIds;
    return getDependencyClosure(root, this, {
      depth: options.depth,
      limit: options.limit,
      isUnsupported: (item) => unsupportedIds.has(item.id) || isUnsupportedByExtension(item),
    });
  }

  async patchAccess(request: PatchAccessRequest): Promise<PatchAccessResult> {
    if (this.unauthorizedIds.has(request.id)) return { kind: "forbidden" };
    const item = this.items.get(request.id);
    if (!item) return { kind: "forbidden" };

    const shareClient = new FixtureShareClient({
      items: [...this.items.values()].map(contentItemToClosureItem),
      callerId: "fixture-catalog",
      editableIds: [request.id],
    });
    const result = await shareClient.patchAccess(request);
    if (result.kind === "ok") {
      this.items.set(request.id, {
        ...item,
        access: {
          ...item.access,
          sharing: result.access.sharing,
          embeddable: result.access.embeddable,
        },
      });
    }
    return result;
  }
}

export function contentItemToClosureItem(item: ContentItem): ClosureItem {
  return {
    id: item.id,
    type: item.type as DependencyType,
    title: item.title,
    access: contentItemAccessToShareAccess(item),
    dependencies: item.dependencies.map((dependency) => ({
      id: dependency.id,
      type: dependency.type as DependencyType,
      role: dependency.role as DependencyRole,
    })),
  };
}

function contentItemAccessToShareAccess(item: ContentItem): ShareAccess {
  return {
    sharing: item.access.sharing,
    embeddable: item.access.embeddable,
  };
}

function matchesRequest(item: ContentItem, request: ListItemsRequest): boolean {
  if (request.type && item.type !== request.type) return false;
  if (request.owner && item.owner.id !== request.owner) return false;
  if (request.tag && !item.tags.includes(request.tag)) return false;
  if (request.sharing && item.access.sharing !== request.sharing) return false;
  if (request.q) {
    const needle = request.q.toLowerCase();
    const haystack = `${item.title}\n${item.summary}\n${item.tags.join(" ")}`.toLowerCase();
    if (!haystack.includes(needle)) return false;
  }
  return true;
}

function sortItems(items: readonly ContentItem[], request: ListItemsRequest): ContentItem[] {
  const sort: SortOption = request.sort ?? (request.q ? "relevance" : "modified-desc");
  const copy = [...items];
  switch (sort) {
    case "title-asc":
      return copy.sort((a, b) => a.title.localeCompare(b.title));
    case "title-desc":
      return copy.sort((a, b) => b.title.localeCompare(a.title));
    case "modified-asc":
      return copy.sort((a, b) => a.timestamps.modified.localeCompare(b.timestamps.modified));
    case "modified-desc":
      return copy.sort((a, b) => b.timestamps.modified.localeCompare(a.timestamps.modified));
    case "relevance": {
      const needle = (request.q ?? "").toLowerCase();
      if (needle === "") {
        return copy.sort((a, b) => b.timestamps.modified.localeCompare(a.timestamps.modified));
      }
      return copy.sort((a, b) => relevanceScore(b, needle) - relevanceScore(a, needle));
    }
  }
}

function relevanceScore(item: ContentItem, needle: string): number {
  let score = 0;
  if (item.title.toLowerCase().includes(needle)) score += 4;
  if (item.tags.some((tag) => tag.toLowerCase().includes(needle))) score += 3;
  if (item.summary.toLowerCase().includes(needle)) score += 2;
  if (item.description.toLowerCase().includes(needle)) score += 1;
  return score;
}

function parseCursor(cursor: string | null): number {
  if (cursor === null) return 0;
  const parsed = Number.parseInt(cursor, 10);
  return Number.isNaN(parsed) || parsed < 0 ? 0 : parsed;
}

function clampListLimit(value: number): number {
  if (!Number.isFinite(value)) return 24;
  return Math.max(1, Math.min(100, Math.trunc(value)));
}

// ── HTTP client ─────────────────────────────────────────────────

export interface HttpCatalogClientOptions {
  readonly baseUrl: string;
  readonly fetch?: typeof fetch;
  readonly headers?: Readonly<Record<string, string>>;
}

export class HttpCatalogClient implements CatalogClient {
  private readonly baseUrl: string;
  private readonly fetchImpl: typeof fetch;
  private readonly headers: Readonly<Record<string, string>>;
  private readonly etagCache = new Map<string, { etag: string; body: unknown }>();

  constructor(options: HttpCatalogClientOptions) {
    this.baseUrl = options.baseUrl.replace(/\/+$/, "");
    this.fetchImpl = options.fetch ?? globalThis.fetch;
    this.headers = options.headers ?? {};
  }

  async listItems(request: ListItemsRequest = {}): Promise<ListItemsResponse> {
    const url = this.buildUrl("/items", request);
    return this.requestJson<ListItemsResponse>(url);
  }

  async getItem(id: string): Promise<ContentItem> {
    const url = this.buildUrl(`/items/${encodeURIComponent(id)}`);
    return this.requestJson<ContentItem>(url);
  }

  async getDependencies(
    id: string,
    options: { depth?: number; limit?: number } = {},
  ): Promise<GetDependenciesResponse> {
    const url = this.buildUrl(`/items/${encodeURIComponent(id)}/dependencies`, options);
    return this.requestJson<GetDependenciesResponse>(url);
  }

  async patchAccess(request: PatchAccessRequest): Promise<PatchAccessResult> {
    const url = this.buildUrl(`/items/${encodeURIComponent(request.id)}`);
    const headers: Record<string, string> = { "content-type": "application/json", ...this.headers };
    if (request.ifMatch) headers["if-match"] = request.ifMatch;
    const response = await this.fetchImpl(url, {
      method: "PATCH",
      headers,
      body: JSON.stringify({ access: request.access }),
    });
    if (response.status === 403) return { kind: "forbidden" };
    if (response.status === 409) return readPatchConflict(response);
    if (!response.ok) {
      const error = await readError(response);
      return { kind: "error", message: error.message };
    }
    const body = (await response.json()) as { access?: ShareAccess; etag?: string };
    return {
      kind: "ok",
      access: body.access ?? request.access,
      etag: body.etag ?? response.headers.get("etag") ?? undefined,
    };
  }

  async publishHandoff(payload: PublishHandoff): Promise<{ item: ContentItemSummary }> {
    const url = this.buildUrl("/items");
    const response = await this.fetchImpl(url, {
      method: "POST",
      headers: { "content-type": "application/json", ...this.headers },
      body: JSON.stringify(payload),
    });
    if (!response.ok) throw await readError(response);
    return (await response.json()) as { item: ContentItemSummary };
  }

  private buildUrl(path: string, query?: object): string {
    const url = new URL(`${this.baseUrl}${path}`);
    if (query) {
      for (const [key, value] of Object.entries(query)) {
        if (value === undefined || value === null) continue;
        url.searchParams.set(key, String(value));
      }
    }
    return url.toString();
  }

  private async requestJson<T>(url: string): Promise<T> {
    const cached = this.etagCache.get(url);
    const headers: Record<string, string> = { ...this.headers };
    if (cached) headers["if-none-match"] = cached.etag;

    const response = await this.fetchImpl(url, { headers });
    if (response.status === 304 && cached) return cached.body as T;
    if (!response.ok) throw await readError(response);

    const body = (await response.json()) as T;
    const etag = response.headers.get("etag");
    if (etag) this.etagCache.set(url, { etag, body });
    return body;
  }
}

async function readError(response: Response): Promise<CatalogError> {
  const fallback = `${response.status} ${response.statusText || "request failed"}`;
  let envelope: CatalogErrorEnvelope | null = null;
  try {
    envelope = (await response.json()) as CatalogErrorEnvelope;
  } catch {
    // body wasn't JSON — fall back to status-derived code below.
  }
  if (envelope?.error) {
    return new CatalogError(envelope.error.code, envelope.error.message, envelope.error.details);
  }
  const code: CatalogError["code"] =
    response.status === 404
      ? "missing"
      : response.status === 403
        ? "unauthorized"
        : response.status === 409
          ? "conflict"
          : response.status >= 500
            ? "server"
            : "invalid";
  return new CatalogError(code, fallback);
}

async function readPatchConflict(response: Response): Promise<PatchAccessResult> {
  try {
    const body = (await response.json()) as { blockers?: unknown };
    if (Array.isArray(body.blockers)) {
      const blockers = body.blockers.filter(isClosureNode);
      if (blockers.length === body.blockers.length) return { kind: "closureBlocked", blockers };
    }
  } catch {
    // Fall through to a generic conflict. The server remains authoritative;
    // this only controls how much detail the portal can display.
  }
  return { kind: "error", message: "sharing update conflicted with dependency policy" };
}

function isClosureNode(value: unknown): value is ClosureNode {
  if (!value || typeof value !== "object") return false;
  const node = value as Partial<ClosureNode>;
  return typeof node.id === "string" && typeof node.type === "string" && typeof node.role === "string";
}
