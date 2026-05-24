/**
 * Fixture share client. Mirrors the wire shape of the recorded server child
 * ticket (`PATCH /api/v1/portal/items/{id}` accepting `access.sharing` and
 * `access.embeddable`) and the per-tier 403/409 contract.
 *
 * The fixture is the test-time stand-in for the SDK surface from the
 * `honua-sdk-js` child ticket. When that lands, replace `FixtureShareClient`
 * with the SDK client and remove the bundled fixture data.
 */

import { buildLookup, getDependencyClosure } from "./closure.js";
import { evaluateShareEscalation } from "./policy.js";
import type { ClosureItem, ClosureNode, PatchAccessResult, ShareAccess, SharingTier } from "./types.js";

export interface PatchAccessRequest {
  id: string;
  access: ShareAccess;
  /** Used by the server to detect concurrent edits. */
  ifMatch?: string | undefined;
}

export interface ShareClient {
  patchAccess(request: PatchAccessRequest): Promise<PatchAccessResult>;
  listMyGroups(): Promise<
    | { kind: "ok"; groups: { id: string; name: string }[] }
    | { kind: "unsupported" }
    | { kind: "error"; message: string }
  >;
}

export interface FixtureShareClientOptions {
  items: ClosureItem[];
  /** Caller identity. Determines 403 outcomes. */
  callerId: string;
  /** Items the caller can edit (id list). Defaults to all owned-by-caller items. */
  editableIds?: ReadonlyArray<string>;
  ownerOf?: ReadonlyMap<string, string>;
  groups?: { id: string; name: string }[] | "unsupported";
  /** Inject a network-layer failure for one id (for the error-path test). */
  failPatchFor?: string | undefined;
}

/**
 * In-memory share client used by tests and smoke validation. Mirrors the
 * server's authoritative behavior:
 *   - 200 OK with the new access on a permitted patch.
 *   - 403 when the caller lacks edit rights on the item.
 *   - 409 when the patch widens visibility past at least one dep's tier.
 *
 * The 409 path uses the same `evaluateShareEscalation` policy the dialog
 * runs client-side, which is the intent: client and server share one
 * decision function so they can never disagree on a dependency block.
 */
export class FixtureShareClient implements ShareClient {
  private readonly itemsById: Map<string, ClosureItem>;
  private readonly editable: Set<string>;
  private readonly groups: { id: string; name: string }[] | "unsupported";
  private readonly failPatchFor: string | undefined;

  constructor(options: FixtureShareClientOptions) {
    this.itemsById = new Map(options.items.map((i) => [i.id, structuredClone(i)]));
    this.failPatchFor = options.failPatchFor;
    this.groups = options.groups ?? [];
    if (options.editableIds) {
      this.editable = new Set(options.editableIds);
    } else {
      this.editable = new Set();
      const ownerOf = options.ownerOf;
      for (const item of options.items) {
        if (ownerOf && ownerOf.get(item.id) === options.callerId) {
          this.editable.add(item.id);
        }
      }
    }
  }

  /** Read-only access for tests/smoke that need the server-side state. */
  getItem(id: string): ClosureItem | undefined {
    const item = this.itemsById.get(id);
    return item ? structuredClone(item) : undefined;
  }

  async patchAccess(request: PatchAccessRequest): Promise<PatchAccessResult> {
    if (this.failPatchFor === request.id) {
      return { kind: "error", message: "network failure (fixture)" };
    }

    if (!this.editable.has(request.id)) {
      return { kind: "forbidden" };
    }

    const item = this.itemsById.get(request.id);
    if (!item) return { kind: "forbidden" };

    const current =
      item.access === undefined || item.access === "unsupported"
        ? ({ sharing: "private", embeddable: false } satisfies ShareAccess)
        : item.access;

    const closure = getDependencyClosure(request.id, buildLookup([...this.itemsById.values()])).nodes;

    const outcome = evaluateShareEscalation({
      current: current.sharing,
      proposed: request.access.sharing,
      closure,
    });

    if (outcome.kind === "blocked") {
      return { kind: "closureBlocked", blockers: outcome.blockers };
    }

    item.access = { ...request.access };
    this.itemsById.set(request.id, item);

    return {
      kind: "ok",
      access: { ...request.access },
      etag: `"v${Date.now()}-${request.id}"`,
    };
  }

  async listMyGroups(): Promise<
    | { kind: "ok"; groups: { id: string; name: string }[] }
    | { kind: "unsupported" }
    | { kind: "error"; message: string }
  > {
    if (this.groups === "unsupported") return { kind: "unsupported" };
    return { kind: "ok", groups: [...this.groups] };
  }
}

/**
 * Convenience: classify a `PatchAccessResult` into the empty-state vocabulary
 * the dialog uses. Centralized so callers cannot accidentally introduce a
 * share-only `kind` value.
 */
export function classifyPatchResult(
  result: PatchAccessResult,
):
  | { kind: "ok"; access: ShareAccess }
  | { kind: "unauthorized" }
  | { kind: "blocked"; blockers: ClosureNode[] }
  | { kind: "error"; message: string } {
  switch (result.kind) {
    case "ok":
      return { kind: "ok", access: result.access };
    case "forbidden":
      return { kind: "unauthorized" };
    case "closureBlocked":
      return { kind: "blocked", blockers: result.blockers };
    case "error":
      return { kind: "error", message: result.message };
  }
}

export type { SharingTier };
