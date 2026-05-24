/**
 * Per-layer permission resolver for the embed route. Implements the
 * "public embed of a private dep" rule: a public-link or public embed
 * works only when each dep in the closure is shareable to the embed
 * audience. Deps the audience cannot read are surfaced to the embed
 * page as `unauthorized` cells in the legend slot — the rest of the
 * map still renders.
 */

import { buildLookup, getDependencyClosure } from "../share/closure.js";
import { canEmbedAudienceAccess } from "../share/policy.js";
import type { ClosureItem, ClosureLookup, ClosureNode, ShareAccess, SharingTier } from "../share/types.js";

export type EmbedAudienceTier = SharingTier;

/**
 * Resolution for a single dep in the embed legend slot.
 *
 *  - `visible`      — viewer audience can read this dep.
 *  - `unauthorized` — viewer audience cannot; render the cell as the
 *                     `unauthorized` empty-state inside the legend.
 *  - `unsupported`  — dep type does not yet have a sharing surface;
 *                     render as `unsupported` and never block.
 */
export type LegendCell =
  | { kind: "visible"; node: ClosureNode }
  | { kind: "unauthorized"; node: ClosureNode }
  | { kind: "unsupported"; node: ClosureNode };

/**
 * Why the root map cannot be embedded. `null` means the root is
 * readable.
 *
 *  - `unsupported` — the item type does not yet have a sharing surface.
 *  - `embeddable`  — the owner has not enabled the iframe surface
 *                    (`access.embeddable === false`). Maps onto the
 *                    `unsupported` empty-state at the embed page.
 *  - `tier`        — the viewer audience cannot read the item's
 *                    sharing tier. Maps onto the `unauthorized`
 *                    empty-state at the embed page.
 */
export type EmbedRootBlockedBy = "unsupported" | "embeddable" | "tier" | null;

export interface EmbedAuthorization {
  /** Whether the root map itself is readable by the viewer. */
  rootReadable: boolean;
  /** When `rootReadable` is false, why; null when readable. */
  rootBlockedBy: EmbedRootBlockedBy;
  /** Per-dep cells in walker order. */
  cells: LegendCell[];
  /** True when at least one cell is `unauthorized`. */
  hasUnauthorizedDeps: boolean;
}

/**
 * Public-link / public embeds: the viewer audience is the tier of the
 * embed itself. Token-mode embeds are scoped server-side and bypass this
 * check (the server's mint-time closure snapshot is authoritative).
 *
 * `access.embeddable` gates the iframe surface independently of
 * `access.sharing`: per `schemas/share-access-v1.json`, an owner may
 * share publicly without consenting to iframe-from-anywhere. The
 * embeddable check is evaluated before the tier check so the embed
 * page can render the `unsupported` empty-state when iframe embedding
 * is disabled, separately from the `unauthorized` empty-state used for
 * tier denial.
 */
export function resolveEmbedAuthorization(input: {
  rootId: string;
  rootAccess: ShareAccess | "unsupported";
  viewerTier: EmbedAudienceTier;
  closure?: readonly ClosureItem[];
  lookup?: ClosureLookup;
}): EmbedAuthorization {
  const lookup = input.lookup ?? (input.closure ? buildLookup(input.closure) : undefined);
  if (!lookup) {
    throw new Error("resolveEmbedAuthorization requires `closure` or `lookup`");
  }

  let rootReadable = false;
  let rootBlockedBy: EmbedRootBlockedBy = null;
  if (input.rootAccess === "unsupported") {
    rootBlockedBy = "unsupported";
  } else if (!input.rootAccess.embeddable) {
    rootBlockedBy = "embeddable";
  } else if (
    !canEmbedAudienceAccess({
      viewerTier: input.viewerTier,
      nodeTier: input.rootAccess.sharing,
    })
  ) {
    rootBlockedBy = "tier";
  } else {
    rootReadable = true;
  }

  const closure = getDependencyClosure(input.rootId, lookup);
  const cells: LegendCell[] = [];
  let hasUnauthorized = false;

  for (const node of closure.nodes) {
    if (!node.access || node.access === "unsupported") {
      cells.push({ kind: "unsupported", node });
      continue;
    }
    const allowed = canEmbedAudienceAccess({
      viewerTier: input.viewerTier,
      nodeTier: node.access.sharing,
    });
    if (allowed) {
      cells.push({ kind: "visible", node });
    } else {
      cells.push({ kind: "unauthorized", node });
      hasUnauthorized = true;
    }
  }

  return { rootReadable, rootBlockedBy, cells, hasUnauthorizedDeps: hasUnauthorized };
}
