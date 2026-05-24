/**
 * Feature detail panel and tabular detail. The two share a single
 * "selection model" so clicking a row in the table selects the same
 * feature on the map and updates the URL hash.
 */

import { deriveFeatureId } from "./feature-id.js";
import { buildPopupViewModel, escapeHtml, formatValue } from "./popup.js";
import type { PortalGeoJsonFeature, PortalViewerLayer } from "./types.js";

export interface FeatureDetailController {
  renderEmpty: (reason?: string) => void;
  render: (layer: PortalViewerLayer, feature: PortalGeoJsonFeature, index: number) => void;
}

export function createFeatureDetail(root: HTMLElement): FeatureDetailController {
  return {
    renderEmpty: (reason) => {
      const message = reason ?? "Click a feature on the map to inspect its attributes.";
      root.innerHTML = `<p class="empty-copy">${escapeHtml(message)}</p>`;
    },
    render: (layer, feature, index) => {
      const model = buildPopupViewModel(layer, feature, index);
      const rowsHtml = model.rows
        .map((row) => `<dt>${escapeHtml(row.label)}</dt><dd>${escapeHtml(row.value)}</dd>`)
        .join("");
      root.innerHTML = `
        <h3 style="margin:0 0 0.5rem;font-size:1rem;">${escapeHtml(model.title)}</h3>
        ${model.subtitle ? `<p class="layer-row-summary" style="margin:0 0 0.6rem;">${escapeHtml(model.subtitle)}</p>` : ""}
        <dl>${rowsHtml || "<dt>No attributes</dt><dd>—</dd>"}</dl>
      `;
    },
  };
}

export interface FeatureTableHosts {
  body: HTMLElement;
  head: HTMLElement;
  layerLabel?: HTMLElement | null;
  rowCount?: HTMLElement | null;
}

export interface FeatureTableSelectEvent {
  layerId: string;
  featureId: string;
}

export interface FeatureTableCollaborationLock {
  layerId: string;
  featureId: string;
  participantId: string;
  participantName: string;
  color: string;
  status: "selecting" | "editing";
}

export interface FeatureTableRenderOptions {
  selectedFeatureId?: string;
  collaborationLocks?: readonly FeatureTableCollaborationLock[];
}

export interface FeatureTableController {
  renderEmpty: (label: string, message: string) => void;
  render: (
    layer: PortalViewerLayer,
    features: ReadonlyArray<PortalGeoJsonFeature>,
    selectedFeatureOrOptions?: string | FeatureTableRenderOptions,
  ) => void;
}

export function createFeatureTable(
  hosts: FeatureTableHosts,
  onSelect: (event: FeatureTableSelectEvent) => void,
): FeatureTableController {
  return {
    renderEmpty: (label, message) => {
      if (hosts.layerLabel) hosts.layerLabel.textContent = label;
      if (hosts.rowCount) hosts.rowCount.textContent = "";
      hosts.head.innerHTML = "";
      hosts.body.innerHTML = `<tr><td class="empty-copy" colspan="6">${escapeHtml(message)}</td></tr>`;
    },
    render: (layer, features, selectedFeatureOrOptions) => {
      const options = normalizeFeatureTableOptions(selectedFeatureOrOptions);
      const lockByFeatureId = new Map(
        (options.collaborationLocks ?? [])
          .filter((lock) => lock.layerId === layer.id)
          .map((lock) => [lock.featureId, lock] as const),
      );
      const showCollaborationColumn = lockByFeatureId.size > 0;
      if (hosts.layerLabel) hosts.layerLabel.textContent = layer.name;
      if (hosts.rowCount) hosts.rowCount.textContent = `${features.length} ${features.length === 1 ? "row" : "rows"}`;

      const fields = layer.detailFields ?? deriveDetailFieldsFromFeatures(features);
      const collaborationHead = showCollaborationColumn ? "<th>Collaboration</th>" : "";
      hosts.head.innerHTML = `<tr>${fields
        .map((field) => `<th>${escapeHtml(field.label ?? field.name)}</th>`)
        .join("")}${collaborationHead}</tr>`;

      const rowsHtml = features
        .map((feature, index) => {
          const featureId = deriveFeatureId(layer.id, feature, index);
          const lock = lockByFeatureId.get(featureId);
          const cellsHtml = fields
            .map((field) => `<td>${escapeHtml(formatValue(feature.properties?.[field.name]))}</td>`)
            .join("");
          const collaborationCell = showCollaborationColumn
            ? `<td>${lock ? renderCollaborationLockBadge(lock) : ""}</td>`
            : "";
          const collaborationAttrs = lock
            ? ` data-collaboration="${escapeHtml(lock.status)}" data-collaboration-participant="${escapeHtml(lock.participantId)}" title="${escapeHtml(`${lock.participantName} is ${lock.status === "editing" ? "editing" : "selecting"} this feature`)}"`
            : "";
          return `<tr data-feature-id="${escapeHtml(featureId)}" data-selected="${
            options.selectedFeatureId === featureId
          }"${collaborationAttrs}>${cellsHtml}${collaborationCell}</tr>`;
        })
        .join("");

      hosts.body.innerHTML = rowsHtml;

      hosts.body.querySelectorAll<HTMLTableRowElement>("tr[data-feature-id]").forEach((row) => {
        row.addEventListener("click", () => {
          const featureId = row.dataset["featureId"];
          if (featureId) onSelect({ layerId: layer.id, featureId });
        });
      });
    },
  };
}

function normalizeFeatureTableOptions(
  selectedFeatureOrOptions?: string | FeatureTableRenderOptions,
): FeatureTableRenderOptions {
  if (typeof selectedFeatureOrOptions === "string") {
    return { selectedFeatureId: selectedFeatureOrOptions };
  }
  return selectedFeatureOrOptions ?? {};
}

function renderCollaborationLockBadge(lock: FeatureTableCollaborationLock): string {
  const status = lock.status === "editing" ? "Editing" : "Selecting";
  return `<span class="feature-table__collaboration-badge" style="--collaboration-color:${escapeHtml(
    lock.color,
  )}"><span>${escapeHtml(lock.participantName)}</span><strong>${status}</strong></span>`;
}

function deriveDetailFieldsFromFeatures(
  features: ReadonlyArray<PortalGeoJsonFeature>,
): Array<{ name: string; label?: string }> {
  const sample = features[0];
  if (!sample || !sample.properties) return [];
  return Object.keys(sample.properties).map((name) => ({ name, label: name }));
}
