/**
 * Admin → portal publish handoff client.
 *
 * The HTTP implementation lives behind a feature flag and forwards to the
 * server endpoints recorded as a child ticket on honua-server (see
 * `smoke/publish-handoff.smoke.md` for the cross-repo coordination list).
 * Until the server side ships, `FixturePublishHandoffClient` is the
 * production default for portal tests and dev wiring.
 *
 * Upsert semantics (see `mapping.ts` for the field-level invariants):
 *
 *   - Lookup key is `event.sourceServiceId`. The portal item id is
 *     assigned on first publish and never changes across re-publishes.
 *     This is what makes saved maps that depend on a published service
 *     survive a re-publish — the saved-map's `dependencies[i].id`
 *     keeps pointing at the same portal item id.
 *   - `eventKind="publish"` is allowed on an existing record; we treat
 *     it as a republish so accidental duplicates from admin retries do
 *     not split into two portal items.
 *   - `eventKind="metadataUpdate"` and `"statusChange"` on a missing
 *     record throw `PublishHandoffNotFoundError` — those flows assume
 *     the publish has already happened and silently creating an item
 *     would mask an admin-side bug.
 */

import { createContentItemIdGenerator } from "../contracts/ids.js";
import { applyHandoffEventToItem, buildServiceContentItem } from "./mapping.js";
import type { CatalogSurface, PublishHandoffEvent, ServiceContentItem } from "./types.js";

export class PublishHandoffNotFoundError extends Error {
  override readonly name = "PublishHandoffNotFoundError";
  constructor(public readonly sourceServiceId: string) {
    super(`Portal item for source service not found: ${sourceServiceId}`);
  }
}

export class PublishHandoffConflictError extends Error {
  override readonly name = "PublishHandoffConflictError";
}

export interface PublishHandoffContext {
  /** ULID-like id generator. Override in tests for deterministic ids. */
  generateId?: () => string;
  /** Now() override for deterministic tests. */
  now?: () => Date;
  /**
   * Optional registry that records saved-map → service-item dependencies.
   * Used by the upsert path to assert AC2: re-publishing must not strand
   * any saved-map reference.
   */
  savedMapReferences?: SavedMapReferenceRegistry;
}

/**
 * Minimal registry for "this saved map depends on this service item".
 * Lives in this slice so the upsert path can sanity-check that no saved
 * map is left pointing at a non-existent portal item id after a
 * re-publish. The full saved-map ContentItem store is owned by #14;
 * portal would replace this with a thin server-backed query in the
 * production wiring.
 */
export class SavedMapReferenceRegistry {
  private readonly byServiceItemId = new Map<string, Set<string>>();

  link(serviceItemId: string, savedMapId: string): void {
    let set = this.byServiceItemId.get(serviceItemId);
    if (!set) {
      set = new Set();
      this.byServiceItemId.set(serviceItemId, set);
    }
    set.add(savedMapId);
  }

  unlink(serviceItemId: string, savedMapId: string): void {
    const set = this.byServiceItemId.get(serviceItemId);
    if (!set) return;
    set.delete(savedMapId);
    if (set.size === 0) this.byServiceItemId.delete(serviceItemId);
  }

  savedMapsFor(serviceItemId: string): readonly string[] {
    const set = this.byServiceItemId.get(serviceItemId);
    return set ? Array.from(set) : [];
  }
}

export interface PublishHandoffClient {
  /**
   * Create or update a portal item from an admin publish event.
   *
   * Returns the upserted item. Throws `PublishHandoffNotFoundError` for
   * `metadataUpdate` / `statusChange` against an unknown
   * `sourceServiceId`. The receive path is operator-side (the caller
   * already supplied `event.adminDiagnosticsRef`), so the returned item
   * is not redacted — `target.adminDiagnosticsRef` round-trips intact.
   */
  receive(event: PublishHandoffEvent): Promise<ServiceContentItem>;

  /**
   * Look up the portal item for a source service id without applying
   * an event. Returns `null` (not throws) on miss to match the catalog
   * surface taxonomy. Catalog read redaction (see
   * `PublishHandoffReadContext`) applies — `target.adminDiagnosticsRef`
   * is `null` unless the actor's permissions include
   * `admin:diagnostics`.
   */
  findBySourceServiceId(sourceServiceId: string, ctx?: PublishHandoffReadContext): Promise<ServiceContentItem | null>;

  /**
   * Look up the portal item by its portal id. Catalog read redaction
   * applies (see `PublishHandoffReadContext`).
   */
  get(id: string, ctx?: PublishHandoffReadContext): Promise<ServiceContentItem | null>;

  /**
   * List all service items, newest-modified first. Catalog read
   * redaction applies (see `PublishHandoffReadContext`).
   */
  list(ctx?: PublishHandoffReadContext): Promise<ServiceContentItem[]>;

  /**
   * Read surface for the catalog UI. Returns one of the four cases in
   * `CatalogSurface` so callers do not have to reinvent the union.
   *
   * `ctx.canRead` is the per-call existence gate. If supplied and it
   * returns `false` for the located item, the surface is `unauthorized`
   * — the existence of the source service is intentionally not leaked
   * to a caller that cannot read it. The HTTP-backed implementation
   * will translate a server-side 403 into the same surface; the
   * fixture client invokes the callback so tests can simulate it.
   *
   * `ctx.permissions` is the field-level gate on the returned item:
   * the `ok` branch redacts `target.adminDiagnosticsRef` to `null`
   * unless the actor has `admin:diagnostics`.
   */
  surfaceForSourceService(sourceServiceId: string, ctx?: PublishHandoffReadContext): Promise<CatalogSurface>;
}

export interface PublishHandoffReadContext {
  /**
   * Permissions for the actor performing the read. Operator-only fields
   * on returned items are gated by this set: in particular,
   * `target.adminDiagnosticsRef` is forced to `null` unless the set
   * contains `admin:diagnostics`. This stops the operator correlation
   * id from leaking through catalog read surfaces — `adminDiagnosticLink`
   * then has nothing to compose for non-operators, even if a UI binding
   * tries to render the field directly.
   *
   * Default (omitted ctx, or omitted permissions): the read is treated
   * as a non-admin catalog read and operator-only fields are stripped.
   * The HTTP-backed client will gate the same field server-side; the
   * fixture client mirrors that policy on the client.
   */
  permissions?: ReadonlySet<string> | readonly string[];
  /**
   * Predicate `surfaceForSourceService` consults to decide whether the
   * calling actor is allowed to see the located item. If supplied and
   * false, the surface is `unauthorized` instead of `ok`.
   *
   * Only consulted by `surfaceForSourceService`; the other read methods
   * use `permissions` for the field-level gate, and the HTTP transport
   * gates existence at the request layer instead.
   */
  canRead?: (item: ServiceContentItem) => boolean;
}

/**
 * Permission required to see operator-only fields on returned items.
 * Kept as a string constant so the gate is consistent across
 * `adminDiagnosticLink` and the catalog read redaction.
 */
const ADMIN_DIAGNOSTICS_PERMISSION = "admin:diagnostics";

interface FixtureRecord {
  item: ServiceContentItem;
}

export class FixturePublishHandoffClient implements PublishHandoffClient {
  private readonly bySourceServiceId = new Map<string, string>();
  private readonly records = new Map<string, FixtureRecord>();
  private readonly ctx: Required<Pick<PublishHandoffContext, "generateId" | "now">> & {
    savedMapReferences?: SavedMapReferenceRegistry;
  };

  constructor(ctx: PublishHandoffContext = {}) {
    this.ctx = {
      generateId: ctx.generateId ?? defaultIdGenerator(),
      now: ctx.now ?? (() => new Date()),
      ...(ctx.savedMapReferences !== undefined ? { savedMapReferences: ctx.savedMapReferences } : {}),
    };
  }

  async receive(event: PublishHandoffEvent): Promise<ServiceContentItem> {
    if (!event.sourceServiceId || event.sourceServiceId.trim().length === 0) {
      throw new Error("sourceServiceId is required");
    }
    const existingId = this.bySourceServiceId.get(event.sourceServiceId);
    const now = this.ctx.now().toISOString();

    if (existingId === undefined) {
      if (event.eventKind === "metadataUpdate" || event.eventKind === "statusChange") {
        throw new PublishHandoffNotFoundError(event.sourceServiceId);
      }
      const id = this.ctx.generateId();
      const item = buildServiceContentItem(event, { id, now });
      this.bySourceServiceId.set(event.sourceServiceId, id);
      this.records.set(id, { item });
      return cloneItem(item);
    }

    const existing = this.records.get(existingId);
    if (!existing) {
      // Index/record drift — recover by rebuilding the record. Should not
      // happen in production because both maps mutate together.
      throw new PublishHandoffConflictError(`Index drift for ${event.sourceServiceId} → ${existingId}`);
    }

    const referencesBefore = this.referencesFor(existingId);
    const updated = applyHandoffEventToItem(existing.item, event, { now });
    if (updated.id !== existing.item.id) {
      throw new PublishHandoffConflictError(`applyHandoffEventToItem must preserve id (${existing.item.id})`);
    }
    this.records.set(existingId, { item: updated });

    // AC2 guard. The id never changes (asserted above), so any registered
    // saved-map → service-item link is still valid after the upsert. This
    // is here as a runtime tripwire so a future change that *does* break
    // the invariant is caught immediately rather than at viewer load time.
    const referencesAfter = this.referencesFor(existingId);
    if (referencesBefore.length !== referencesAfter.length) {
      throw new PublishHandoffConflictError(`Saved-map references changed across upsert for ${existingId}`);
    }

    return cloneItem(updated);
  }

  async findBySourceServiceId(
    sourceServiceId: string,
    ctx?: PublishHandoffReadContext,
  ): Promise<ServiceContentItem | null> {
    const id = this.bySourceServiceId.get(sourceServiceId);
    if (id === undefined) return null;
    const record = this.records.get(id);
    if (!record) return null;
    return cloneItemForRead(record.item, ctx);
  }

  async get(id: string, ctx?: PublishHandoffReadContext): Promise<ServiceContentItem | null> {
    const record = this.records.get(id);
    if (!record) return null;
    return cloneItemForRead(record.item, ctx);
  }

  async list(ctx?: PublishHandoffReadContext): Promise<ServiceContentItem[]> {
    return Array.from(this.records.values())
      .map((r) => cloneItemForRead(r.item, ctx))
      .sort((a, b) => b.timestamps.modified.localeCompare(a.timestamps.modified));
  }

  async surfaceForSourceService(sourceServiceId: string, ctx?: PublishHandoffReadContext): Promise<CatalogSurface> {
    // Resolve against the unredacted record so `canRead` sees the full
    // item shape and the unsupported branch reads `statusDetail`. Any
    // returned item is then redacted via `cloneItemForRead`.
    const id = this.bySourceServiceId.get(sourceServiceId);
    if (id === undefined) return { kind: "missing", sourceServiceId };
    const record = this.records.get(id);
    if (!record) return { kind: "missing", sourceServiceId };
    const item = record.item;

    if (ctx?.canRead && !ctx.canRead(item)) {
      // Existence intentionally not leaked: an unauthorized caller gets
      // the same shape as a missing item plus a hint that auth, not
      // absence, is the reason. This mirrors how the HTTP-backed client
      // will translate a server 403 — see PublishHandoffReadContext.
      return { kind: "unauthorized", sourceServiceId };
    }
    if (item.target.serviceType === "unsupported") {
      return {
        kind: "unsupported",
        sourceServiceId,
        reason: item.target.statusDetail ?? "Service type not supported.",
      };
    }
    return { kind: "ok", item: cloneItemForRead(item, ctx) };
  }

  private referencesFor(serviceItemId: string): readonly string[] {
    return this.ctx.savedMapReferences?.savedMapsFor(serviceItemId) ?? [];
  }
}

function cloneItem<T>(item: T): T {
  if (typeof structuredClone === "function") return structuredClone(item);
  return JSON.parse(JSON.stringify(item)) as T;
}

/**
 * Clone a stored item for return through a catalog read surface,
 * redacting operator-only fields based on `ctx.permissions`.
 *
 * Today the only operator-only field is `target.adminDiagnosticsRef`:
 * it is forced to `null` unless the caller's permission set contains
 * `admin:diagnostics`. The full ref remains in storage so an authorized
 * later read can still resolve the admin diagnostic URL via
 * `adminDiagnosticLink`.
 */
function cloneItemForRead(item: ServiceContentItem, ctx?: PublishHandoffReadContext): ServiceContentItem {
  const cloned = cloneItem(item);
  if (!hasAdminDiagnosticsPermission(ctx?.permissions)) {
    cloned.target.adminDiagnosticsRef = null;
  }
  return cloned;
}

function hasAdminDiagnosticsPermission(perms: PublishHandoffReadContext["permissions"] | undefined): boolean {
  if (!perms) return false;
  if (perms instanceof Set) return perms.has(ADMIN_DIAGNOSTICS_PERMISSION);
  for (const p of perms) {
    if (p === ADMIN_DIAGNOSTICS_PERMISSION) return true;
  }
  return false;
}

function defaultIdGenerator(): () => string {
  return createContentItemIdGenerator();
}
