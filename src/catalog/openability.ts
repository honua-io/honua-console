/**
 * "Open in map" / "Open externally" gating for catalog items.
 *
 * Pure function. No DOM, no React, no router. The portal viewer Beta does not
 * render every item type, so detail and card UIs MUST consult this gate before
 * offering an open action — never wire a generic "Open in map" link that the
 * viewer cannot honor.
 *
 * Decision matrix (see honua-portal#12 design):
 *
 * | type         | condition                                | result          |
 * | ------------ | ---------------------------------------- | --------------- |
 * | service      | renders or query + supported endpoint    | open-in-map     |
 * | layer        | query capability                         | open-in-map     |
 * | map          | always                                   | open-in-map     |
 * | scene        | always (Beta has no scene viewer)        | unsupported     |
 * | app          | has target.url                           | open-external   |
 * | document     | has target.url                           | open-external   |
 * | external-url | always                                   | open-external   |
 *
 * `extensions["honua-portal-viewer"].supported === false` overrides any
 * type-derived openability — the publisher knows the viewer can't render it
 * (e.g. a WMS-only service). The reason field is surfaced verbatim.
 *
 * Summary projections (`ContentItemSummary`) do NOT carry `endpoints`. The
 * gate is conservative there: a query-only service summary cannot prove it
 * has a portal-viewable endpoint, so it is treated as `unsupported` until
 * the detail page resolves the endpoint set. Render-capable summaries
 * (`render` / `tiles` / `pbf`) remain openable because those capabilities
 * are sufficient on their own. This keeps card and detail decisions in
 * sync — a card never promises an `Open in map` action that the detail
 * page would later disable.
 */

import type { ContentItem, ContentItemSummary, ItemType } from "../contracts/content-item.js";
import { safeHttpUrl } from "../security/url.js";

export type OpenActionKind = "open-in-map" | "open-external" | "unsupported";

export interface OpenAction {
  readonly kind: OpenActionKind;
  readonly label: string;
  readonly href: string | null;
  readonly reason: string | null;
}

/**
 * Loose shape so list summaries and detail items both flow through the gate.
 * `endpoints`, `target`, and `extensions` are absent on summaries; the gate
 * degrades gracefully when only summary fields are present.
 */
export type OpenableItem = ContentItem | ContentItemSummary;

interface ItemView {
  readonly id: string;
  readonly type: ItemType;
  readonly capabilities: readonly string[];
  readonly target: ContentItem["target"] | null;
  readonly endpoints: ContentItem["endpoints"] | null;
  readonly viewerOverride: ViewerOverride;
}

interface ViewerOverride {
  readonly supported: boolean | null;
  readonly reason: string | null;
}

const NO_OVERRIDE: ViewerOverride = { supported: null, reason: null };

export function getOpenAction(item: OpenableItem): OpenAction {
  const view = toView(item);

  if (view.viewerOverride.supported === false) {
    return unsupported(
      view.viewerOverride.reason ?? "Publisher marked this item as not viewable in the portal viewer.",
    );
  }

  switch (view.type) {
    case "service":
      return forService(view);
    case "layer":
      return forLayer(view);
    case "map":
      return forMap(view);
    case "scene":
      return unsupported("3D scenes are not yet renderable in the portal Beta viewer.");
    case "app":
      return forExternal(view, "Open app", "App is missing a launch URL.");
    case "document":
      return forExternal(view, "Open document", "Document is missing a download URL.");
    case "external-url":
      return forExternal(view, "Open external link", "External link is missing a URL.");
  }
}

function forService(view: ItemView): OpenAction {
  const supportedEndpoint = hasSupportedEndpoint(view);
  const renderable =
    view.capabilities.includes("render") || view.capabilities.includes("tiles") || view.capabilities.includes("pbf");
  const queryable = view.capabilities.includes("query") && supportedEndpoint;
  if (renderable || queryable) {
    return openInMap(`/maps/new?from=${encodeURIComponent(view.id)}`);
  }
  return unsupported(
    "Service has no portal-viewable endpoint. Render, tiles, pbf, or query (with a supported endpoint) is required.",
  );
}

function forLayer(view: ItemView): OpenAction {
  if (view.capabilities.includes("query") || view.capabilities.includes("render")) {
    return openInMap(`/maps/new?from=${encodeURIComponent(view.id)}`);
  }
  return unsupported("Layer has no query or render capability — the portal viewer cannot draw it.");
}

function forMap(view: ItemView): OpenAction {
  const target = view.target;
  if (target && target.type === "map" && target.webmapJsonRef) {
    return openInMap(`/maps/${encodeURIComponent(target.webmapJsonRef)}`);
  }
  return openInMap(`/maps/new?from=${encodeURIComponent(view.id)}`);
}

function forExternal(view: ItemView, label: string, missingReason: string): OpenAction {
  const target = view.target;
  if (target && (target.type === "app" || target.type === "document" || target.type === "external-url") && target.url) {
    const href = safeHttpUrl(target.url);
    return href ? { kind: "open-external", label, href, reason: null } : unsupported(missingReason);
  }
  if (!target) {
    return { kind: "open-external", label, href: null, reason: null };
  }
  return unsupported(missingReason);
}

function openInMap(href: string): OpenAction {
  return { kind: "open-in-map", label: "Open in map", href, reason: null };
}

function unsupported(reason: string): OpenAction {
  return { kind: "unsupported", label: "Unsupported", href: null, reason };
}

function hasSupportedEndpoint(view: ItemView): boolean {
  const endpoints = view.endpoints;
  if (!endpoints) return false;
  return Boolean(endpoints.geoservices ?? endpoints.ogcFeatures ?? endpoints.tiles ?? endpoints.stac);
}

function toView(item: OpenableItem): ItemView {
  const isDetail = "target" in item && "endpoints" in item && "extensions" in item;
  if (isDetail) {
    const detail = item as ContentItem;
    return {
      id: detail.id,
      type: detail.type,
      capabilities: detail.capabilities,
      target: detail.target,
      endpoints: detail.endpoints,
      viewerOverride: readViewerOverride(detail),
    };
  }
  const summary = item as ContentItemSummary;
  return {
    id: summary.id,
    type: summary.type,
    capabilities: summary.capabilities,
    target: null,
    endpoints: null,
    viewerOverride: summary.viewerSupport ?? NO_OVERRIDE,
  };
}

function readViewerOverride(item: ContentItem): ViewerOverride {
  const ext = item.extensions["honua-portal-viewer"];
  if (!ext) return NO_OVERRIDE;
  const supported = typeof ext["supported"] === "boolean" ? (ext["supported"] as boolean) : null;
  const reason = typeof ext["reason"] === "string" ? (ext["reason"] as string) : null;
  return { supported, reason };
}
