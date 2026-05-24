/**
 * Embed-route auth resolver. Pure logic that turns an inbound embed
 * request into one of three postures:
 *
 *   - `anonymous` — public/public-link map, no token, no session.
 *     Renders without portal chrome and never writes localStorage.
 *   - `session`  — caller has a portal session cookie. Treated like a
 *     normal in-portal viewer mount; used for the `?chrome=full` test
 *     surface and for authenticated org embeds.
 *   - `token`    — caller presented `#embedToken=…`. The embed page
 *     redeems the token via the SDK child ticket's client and uses the
 *     resulting bearer for catalog + viewer reads. No localStorage,
 *     no session-cookie mixing.
 *
 * The resolver itself never performs network IO — it just classifies
 * input and returns a descriptor the embed page consumes. Token
 * redemption is delegated to the injected `redeemEmbedToken` callback,
 * which the SDK child ticket implements.
 */

import type { EmbedTokenDescriptor, ShareDialogEmptyKind } from "../share/types.js";
import { parseEmbedTokenFragment } from "./route.js";

export type EmbedAuthPosture = { kind: "anonymous" } | { kind: "session" } | { kind: "token"; token: string };

export interface EmbedAuthInput {
  /** Raw `window.location.hash` (with or without leading `#`). */
  fragment: string | null;
  /** Truthy when the request carries a valid portal session. */
  hasSession: boolean;
}

export function resolveEmbedAuth(input: EmbedAuthInput): EmbedAuthPosture {
  const token = parseEmbedTokenFragment(input.fragment);
  if (token) {
    return { kind: "token", token };
  }
  if (input.hasSession) {
    return { kind: "session" };
  }
  return { kind: "anonymous" };
}

export type RedeemEmbedToken = (
  token: string,
) => Promise<
  | { kind: "ok"; descriptor: EmbedTokenDescriptor }
  | { kind: "expired" }
  | { kind: "invalid" }
  | { kind: "error"; message: string }
>;

export interface EmbedReadyState {
  /** Resolved auth posture for the embed page mount. */
  posture: EmbedAuthPosture;
  /**
   * For token mode: the descriptor returned by the SDK redeem call.
   * For other postures: `null`.
   */
  descriptor: EmbedTokenDescriptor | null;
  /**
   * When non-null, the embed page renders an `<EmptyState />` of this kind
   * instead of the viewer. Used for expired/invalid/error token outcomes.
   */
  fallback: ShareDialogEmptyKind | null;
}

/**
 * One round-trip wrapper. Used by the embed page on first paint:
 * resolve the posture, redeem any token, and produce a single
 * `EmbedReadyState` the viewer mount can consume. Network failures and
 * expired tokens map onto the standard empty-state vocabulary so the
 * embed surface stays consistent with the rest of the portal.
 */
export async function prepareEmbedAuth(input: {
  fragment: string | null;
  hasSession: boolean;
  redeemEmbedToken?: RedeemEmbedToken;
}): Promise<EmbedReadyState> {
  const posture = resolveEmbedAuth({
    fragment: input.fragment,
    hasSession: input.hasSession,
  });

  if (posture.kind !== "token") {
    return { posture, descriptor: null, fallback: null };
  }

  if (!input.redeemEmbedToken) {
    return { posture, descriptor: null, fallback: "unsupported" };
  }

  const result = await input.redeemEmbedToken(posture.token);
  switch (result.kind) {
    case "ok":
      return { posture, descriptor: result.descriptor, fallback: null };
    case "expired":
    case "invalid":
      return { posture, descriptor: null, fallback: "unauthorized" };
    case "error":
      return { posture, descriptor: null, fallback: "error" };
  }
}
