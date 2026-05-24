/**
 * Layer/portal metadata panel. Surfaces the catalog metadata fields the
 * viewer needs for AC2 ("Users can inspect features and understand layer
 * metadata"), with consistent empty-state copy when a field is missing.
 */

import { safeHttpUrl } from "../security/url.js";
import { escapeHtml } from "./popup.js";
import type { PortalViewerItemMetadata } from "./types.js";

export interface MetadataPanelController {
  render: (metadata: PortalViewerItemMetadata) => void;
}

export function createMetadataPanel(root: HTMLElement, titleHost?: HTMLElement | null): MetadataPanelController {
  return {
    render: (metadata) => {
      if (titleHost) titleHost.textContent = metadata.title;
      root.innerHTML = renderMetadata(metadata);
    },
  };
}

function renderMetadata(metadata: PortalViewerItemMetadata): string {
  const rows: Array<[string, string]> = [];
  pushRow(rows, "Title", metadata.title);
  pushRow(rows, "Summary", metadata.summary);
  pushRow(rows, "Owner", metadata.owner);
  pushRow(rows, "Organization", metadata.organization);
  pushRow(rows, "License", metadata.license);
  pushRow(rows, "Attribution", metadata.attribution);
  pushRow(rows, "Coordinate system", metadata.coordinateSystem);
  if (metadata.tags && metadata.tags.length > 0) pushRow(rows, "Tags", metadata.tags.join(", "));
  if (metadata.modified) pushRow(rows, "Last modified", formatDate(metadata.modified));
  if (metadata.serviceUrl) {
    const serviceUrl = safeHttpUrl(metadata.serviceUrl);
    if (serviceUrl) {
      rows.push([
        "Service URL",
        `<a href="${escapeHtml(serviceUrl)}" target="_blank" rel="noopener">${escapeHtml(serviceUrl)}</a>`,
      ]);
    } else {
      pushRow(rows, "Service URL", metadata.serviceUrl);
    }
  }
  if (metadata.description) pushRow(rows, "Description", metadata.description);

  if (rows.length === 0) return '<p class="empty-copy">No metadata available for this item.</p>';

  return rows
    .map(([label, value]) => `<dl class="metadata-row"><dt>${escapeHtml(label)}</dt><dd>${value}</dd></dl>`)
    .join("");
}

function pushRow(rows: Array<[string, string]>, label: string, value: string | undefined): void {
  if (value === undefined || value === null || value === "") return;
  rows.push([label, escapeHtml(value)]);
}

function formatDate(value: string): string {
  const parsed = Date.parse(value);
  if (Number.isNaN(parsed)) return value;
  const date = new Date(parsed);
  return date.toISOString().slice(0, 10);
}
