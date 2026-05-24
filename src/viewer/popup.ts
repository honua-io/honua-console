/**
 * Popup + detail rendering helpers. Output is plain HTML/text models so
 * the viewer can reuse them in MapLibre popups, the sidebar detail panel,
 * and the tabular detail.
 *
 * Field labelling honors the SDK's `HonuaPopupConfig` when present
 * (preserving server-authored aliases) and falls back to property keys
 * otherwise.
 */

import type { HonuaPopupConfig } from "@honua/sdk-js/webmap";
import type { PortalDetailField, PortalGeoJsonFeature, PortalViewerLayer } from "./types.js";
import { resolveDetailFields } from "./types.js";

export interface PopupViewModel {
  title: string;
  subtitle?: string;
  rows: PopupRow[];
}

export interface PopupRow {
  label: string;
  value: string;
}

export function buildPopupViewModel(
  layer: PortalViewerLayer,
  feature: PortalGeoJsonFeature,
  index: number,
): PopupViewModel {
  const properties = feature.properties ?? {};
  const popupConfig = layer.popup;
  const fields = resolveDetailFields({ layer, sample: feature });
  const rows = fields.map((field) => ({
    label: field.label ?? field.name,
    value: formatValue(properties[field.name]),
  }));

  return {
    title: resolvePopupTitle(layer, properties, popupConfig, index),
    subtitle: layer.summary ?? layer.name,
    rows,
  };
}

export function buildDetailColumns(layer: PortalViewerLayer, sample?: PortalGeoJsonFeature): PortalDetailField[] {
  return resolveDetailFields({ layer, sample });
}

export function renderPopupHtml(model: PopupViewModel): string {
  const visibleRows = model.rows.slice(0, 8);
  const rowsHtml = visibleRows
    .map(
      (row) =>
        `<div class="popup-row"><span>${escapeHtml(row.label)}</span><strong>${escapeHtml(row.value)}</strong></div>`,
    )
    .join("");

  return [
    '<article class="popup-card">',
    model.subtitle ? `<p class="popup-kicker">${escapeHtml(model.subtitle)}</p>` : "",
    `<h3>${escapeHtml(model.title)}</h3>`,
    rowsHtml.length > 0 ? `<div class="popup-grid">${rowsHtml}</div>` : "",
    "</article>",
  ].join("");
}

export function escapeHtml(value: unknown): string {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

export function formatValue(value: unknown): string {
  if (value === null || value === undefined) return "—";
  if (typeof value === "number") return Number.isInteger(value) ? value.toString() : value.toFixed(2);
  if (typeof value === "boolean") return value ? "true" : "false";
  if (typeof value === "string") return value;
  if (typeof value === "object") {
    try {
      return JSON.stringify(value);
    } catch {
      return String(value);
    }
  }
  return String(value);
}

function resolvePopupTitle(
  layer: PortalViewerLayer,
  properties: Record<string, unknown>,
  popupConfig: HonuaPopupConfig | undefined,
  index: number,
): string {
  const templated = popupConfig?.title ? interpolateTitle(popupConfig.title, properties) : undefined;
  if (templated && templated.trim().length > 0) return templated;

  const candidate =
    properties["NAME"] ?? properties["name"] ?? properties["TITLE"] ?? properties["title"] ?? properties["label"];
  if (typeof candidate === "string" && candidate.length > 0) return candidate;

  return `${layer.name} feature #${index + 1}`;
}

function interpolateTitle(template: string, properties: Record<string, unknown>): string {
  return template.replaceAll(/\{([^{}]+)\}/g, (_match, key) => {
    const trimmed = key.trim();
    const value = properties[trimmed];
    return value === undefined || value === null ? "" : String(value);
  });
}
