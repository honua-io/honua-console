/**
 * Pure escalation policy. Single source of truth for the client-side
 * dependency-closure block decision. The server child ticket reuses this
 * function shape as a parity-test fixture so client and server cannot
 * silently diverge.
 *
 * Server is still authoritative — UI never bypasses a 409 — but a
 * defensive client check avoids one round-trip per escalation attempt and
 * lets the dialog show actionable blockers as soon as the user picks a
 * wider tier.
 */

import { type ClosureNode, type EscalationOutcome, SHARING_TIER_ORDER, type SharingTier } from "./types.js";

export function tierRank(tier: SharingTier): number {
  return SHARING_TIER_ORDER.indexOf(tier);
}

/**
 * Negative when `a` is narrower than `b`, zero when equal, positive when
 * `a` is wider. `org` and `group` are sibling tiers but we still rank
 * `group` as wider than `org` for escalation purposes — sharing to a
 * specific group requires the same audience as org and additionally
 * surfaces in that group's catalog. The exact ordering is encoded in
 * `SHARING_TIER_ORDER` and reused by the server parity test.
 */
export function compareTier(a: SharingTier, b: SharingTier): number {
  return tierRank(a) - tierRank(b);
}

/**
 * Evaluate whether escalating `item.access.sharing` to `proposed` is
 * blocked by any dep in `closure` whose own sharing is narrower than
 * `proposed`.
 *
 * - Narrowing or equal-tier changes are always `ok` (`widening: false`).
 * - `unsupported` deps never block (they are surfaced in the dialog with
 *   the `unsupported` empty-state, but they do not gate the patch).
 * - `private` deps always block public-link/public escalation.
 *
 * The function is deterministic and order-stable: blockers are returned
 * in the order they appear in the input closure. The server parity test
 * relies on this stability.
 */
export function evaluateShareEscalation(input: {
  current: SharingTier;
  proposed: SharingTier;
  closure: readonly ClosureNode[];
}): EscalationOutcome {
  const widening = compareTier(input.proposed, input.current) > 0;
  if (!widening) {
    return { kind: "ok", widening: false };
  }
  const blockers: ClosureNode[] = [];
  for (const node of input.closure) {
    if (!node.access || node.access === "unsupported") continue;
    if (compareTier(node.access.sharing, input.proposed) < 0) {
      blockers.push(node);
    }
  }
  if (blockers.length === 0) {
    return { kind: "ok", widening: true };
  }
  return { kind: "blocked", blockers };
}

/**
 * For embed-time per-layer rendering: would `viewerTier` (the audience
 * actually viewing the embed) be allowed to read `nodeTier`?
 *
 * - `public` viewers can read `public` and `public-link` items only.
 * - `public-link` viewers can read `public` and `public-link` items.
 *   (The link itself is the unforgeable handle for `public-link`.)
 * - `org` viewers can read everything except `private` items they do not
 *   own — ownership is checked separately by the server.
 * - Token-mode viewers are scoped server-side at mint time; this function
 *   is not the authority for token mode.
 */
export function canEmbedAudienceAccess(input: {
  viewerTier: SharingTier;
  nodeTier: SharingTier;
}): boolean {
  const { viewerTier, nodeTier } = input;
  if (nodeTier === "private") return false;
  if (nodeTier === "public") return true;
  if (nodeTier === "public-link") {
    return viewerTier === "public-link" || viewerTier === "public" || viewerTier === "org" || viewerTier === "group";
  }
  if (nodeTier === "org" || nodeTier === "group") {
    return viewerTier === "org" || viewerTier === "group";
  }
  return false;
}
