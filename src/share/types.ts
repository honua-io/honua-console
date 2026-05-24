/**
 * Sharing & embed — TypeScript types.
 *
 * `ShareAccess` mirrors `schemas/share-access-v1.json`. `EmbedTokenDescriptor`
 * mirrors `schemas/embed-token-v1.json`. Hand-rolled to keep #15 unblocked
 * before #10 ships canonical generated types via `json-schema-to-typescript`.
 *
 * The dependency / item shapes mirror #10's `ContentItem` projection that
 * #14 already encodes. #15 only depends on the `id`, `type`, and
 * `access.sharing` fields plus a `dependencies[]` adjacency list — keep the
 * surface narrow so coordination with #10 stays cheap.
 */

export type SharingTier = "private" | "org" | "group" | "public-link" | "public";

/**
 * Five tiers ordered narrowest → widest. Index = rank used by
 * `compareTier` and `evaluateShareEscalation`.
 */
export const SHARING_TIER_ORDER: readonly SharingTier[] = ["private", "org", "group", "public-link", "public"] as const;

export interface ShareAccess {
  sharing: SharingTier;
  embeddable: boolean;
  groupIds?: string[];
  publicLinkToken?: string | null;
}

export type DependencyType =
  | "service"
  | "layer"
  | "map"
  | "scene"
  | "app"
  | "document"
  | "external-url"
  | "style"
  | "popup";
export type DependencyRole = "operationalLayer" | "baseMap" | "datasource" | "style" | "popup" | (string & {});

/**
 * A node in the dependency closure. `access` is the dep's *current* access,
 * which the dependency review compares against the *proposed* tier.
 *
 * `unsupported` means the dep type does not yet have a sharing surface
 * (e.g. inline style refs). The dialog renders these with the
 * `unsupported` empty-state and never blocks on them.
 */
export interface ClosureNode {
  id: string;
  type: DependencyType;
  role: DependencyRole;
  title?: string;
  access?: ShareAccess | "unsupported";
}

export interface ClosureItem {
  id: string;
  type: DependencyType;
  dependencies: { id: string; type: DependencyType; role: DependencyRole }[];
  access?: ShareAccess | "unsupported";
  title?: string;
}

export interface ClosureLookup {
  /** Resolve a node by id. Returning `undefined` is treated as `unsupported`. */
  get(id: string): ClosureItem | undefined;
}

export type EscalationOutcome = { kind: "ok"; widening: boolean } | { kind: "blocked"; blockers: ClosureNode[] };

/**
 * Outcome of a `patchAccess` call. The shape mirrors what the server child
 * ticket returns: 200 → ok, 403 → forbidden, 409 → closureBlocked with the
 * server's authoritative blocker list, network/other → error.
 */
export type PatchAccessResult =
  | { kind: "ok"; access: ShareAccess; etag?: string }
  | { kind: "forbidden" }
  | { kind: "closureBlocked"; blockers: ClosureNode[] }
  | { kind: "error"; message: string };

export type ShareDialogEmptyKind = "empty" | "unauthorized" | "unsupported" | "missing" | "error";

export type EmbedAudience = "pilot" | "internal" | "partner";

export interface EmbedTokenDescriptor {
  token: string;
  itemId: string;
  audience: EmbedAudience;
  expiresAt: string;
  closure: string[];
}
