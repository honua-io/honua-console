/**
 * Share link and embed snippet builders. The dialog renders the strings
 * these functions return; the smoke spec verifies the exact strings the
 * clipboard receives.
 *
 * Both builders accept a `portalHost` that already includes the scheme
 * (e.g. `https://portal.honua.io`). Hosts without a scheme are rejected
 * to prevent surprising `//host` relative urls leaking through copy.
 */

import type { ContentItemKind, EmbedRouteParams } from "../embed/route.js";
import { stringifyEmbedParams } from "../embed/route.js";

export interface ShareLinkInput {
  portalHost: string;
  itemId: string;
  /**
   * `map` items resolve to `/maps/:id`, every other type resolves to
   * `/catalog/:id`. The split mirrors #14 (saved maps own a stable URL
   * separate from generic catalog items) and #12 (catalog item detail).
   */
  itemKind: ContentItemKind;
  /** Public-link tier appends `?token=…` to the resolved URL. */
  publicLinkToken?: string | null;
}

export function buildShareLink(input: ShareLinkInput): string {
  const host = normalizeHost(input.portalHost);
  const path = input.itemKind === "map" ? `/maps/${input.itemId}` : `/catalog/${input.itemId}`;
  const url = new URL(path, host);
  if (input.publicLinkToken) {
    url.searchParams.set("token", input.publicLinkToken);
  }
  return url.toString();
}

export interface EmbedSnippetInput {
  portalHost: string;
  itemId: string;
  /**
   * Embed chrome/legend/zoom/extent. Defaults match the snippet defaults
   * documented in the share dialog: `chrome=minimal`, `legend=on`,
   * `zoom=on`, no extent override (use the persisted default extent).
   */
  embed?: Partial<EmbedRouteParams>;
  /** iframe attributes — defaults match the dialog's snippet copy. */
  width?: number;
  height?: number;
  /**
   * Authenticated-embed token. Appended as `#embedToken=…` so the token
   * is kept out of access logs and referer headers.
   */
  embedToken?: string | null;
}

export const DEFAULT_EMBED_WIDTH = 800;
export const DEFAULT_EMBED_HEIGHT = 600;
export const DEFAULT_EMBED_PARAMS: EmbedRouteParams = {
  chrome: "minimal",
  legend: true,
  zoom: true,
  extent: null,
};

export function buildEmbedUrl(input: EmbedSnippetInput): string {
  const host = normalizeHost(input.portalHost);
  const url = new URL(`/embed/maps/${input.itemId}`, host);
  const params = { ...DEFAULT_EMBED_PARAMS, ...input.embed };
  const query = stringifyEmbedParams(params);
  if (query) url.search = query;
  if (input.embedToken) {
    url.hash = `embedToken=${encodeURIComponent(input.embedToken)}`;
  }
  return url.toString();
}

export function buildEmbedSnippet(input: EmbedSnippetInput): string {
  const src = buildEmbedUrl(input);
  const width = input.width ?? DEFAULT_EMBED_WIDTH;
  const height = input.height ?? DEFAULT_EMBED_HEIGHT;
  return `<iframe src="${src}" width="${width}" height="${height}" allow="fullscreen" loading="lazy" referrerpolicy="strict-origin-when-cross-origin"></iframe>`;
}

function normalizeHost(host: string): string {
  if (!/^https?:\/\//i.test(host)) {
    throw new Error(`portalHost must include a scheme (got ${JSON.stringify(host)})`);
  }
  return host.replace(/\/+$/, "");
}
