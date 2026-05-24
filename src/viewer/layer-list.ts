/**
 * Layer list panel: visibility toggle, opacity slider, ordering buttons,
 * and inline legend. Pure DOM rendering; the `onChange` callback hands
 * intent back to the viewer orchestrator so state lives in one place.
 */

import { escapeHtml } from "./popup.js";
import type { PortalViewerItem, PortalViewerLayer, ViewerState } from "./types.js";

export type LayerListChangeEvent =
  | { kind: "toggle-visibility"; layerId: string; visible: boolean }
  | { kind: "set-opacity"; layerId: string; opacity: number }
  | { kind: "reorder"; layerId: string; direction: "up" | "down" }
  | { kind: "select-layer"; layerId: string };

export interface LayerListController {
  render: (state: {
    item: PortalViewerItem;
    layerOrder: readonly string[];
    viewer: ViewerState;
    selectedLayerId?: string;
  }) => void;
}

export function createLayerList(
  root: HTMLElement,
  onChange: (event: LayerListChangeEvent) => void,
): LayerListController {
  return {
    render: ({ item, layerOrder, viewer, selectedLayerId }) => {
      root.innerHTML = "";
      const visibleSet = new Set(viewer.visibleLayerIds);

      // Render render-order top → bottom (reverse of MapLibre paint order)
      const display = [...layerOrder].reverse();
      display.forEach((layerId) => {
        const layer = item.layers.find((l) => l.id === layerId);
        if (!layer) return;
        root.appendChild(renderLayerRow(layer, layerOrder, visibleSet.has(layerId), selectedLayerId, onChange));
      });
    },
  };
}

function renderLayerRow(
  layer: PortalViewerLayer,
  layerOrder: readonly string[],
  visible: boolean,
  selectedLayerId: string | undefined,
  onChange: (event: LayerListChangeEvent) => void,
): HTMLElement {
  const li = document.createElement("li");
  li.className = "layer-row";
  li.dataset["layerId"] = layer.id;
  if (selectedLayerId === layer.id) li.dataset["active"] = "true";

  const toggle = document.createElement("input");
  toggle.type = "checkbox";
  toggle.className = "layer-toggle";
  toggle.checked = visible;
  toggle.title = `Toggle visibility for ${layer.name}`;
  toggle.addEventListener("change", (event) => {
    event.stopPropagation();
    onChange({ kind: "toggle-visibility", layerId: layer.id, visible: toggle.checked });
  });

  const labelWrap = document.createElement("div");
  labelWrap.innerHTML = `
    <div class="layer-row-name">${escapeHtml(layer.name)}</div>
    ${layer.summary ? `<div class="layer-row-summary">${escapeHtml(layer.summary)}</div>` : ""}
    ${renderLegendHtml(layer)}
  `;
  labelWrap.style.cursor = "pointer";
  labelWrap.addEventListener("click", () => onChange({ kind: "select-layer", layerId: layer.id }));

  const controls = document.createElement("div");
  controls.className = "layer-row-controls";

  const opacity = document.createElement("input");
  opacity.type = "range";
  opacity.className = "layer-opacity";
  opacity.min = "0";
  opacity.max = "1";
  opacity.step = "0.05";
  opacity.value = layer.defaultOpacity.toString();
  opacity.title = `Opacity for ${layer.name}`;
  opacity.addEventListener("input", () => {
    const value = Number.parseFloat(opacity.value);
    if (Number.isFinite(value)) {
      onChange({ kind: "set-opacity", layerId: layer.id, opacity: value });
    }
  });

  const orderButtons = document.createElement("div");
  orderButtons.className = "layer-order-buttons";
  const upButton = document.createElement("button");
  upButton.type = "button";
  upButton.textContent = "▲";
  upButton.title = "Move layer up";
  upButton.disabled = layerOrder.indexOf(layer.id) === layerOrder.length - 1;
  upButton.addEventListener("click", (event) => {
    event.stopPropagation();
    onChange({ kind: "reorder", layerId: layer.id, direction: "up" });
  });
  const downButton = document.createElement("button");
  downButton.type = "button";
  downButton.textContent = "▼";
  downButton.title = "Move layer down";
  downButton.disabled = layerOrder.indexOf(layer.id) === 0;
  downButton.addEventListener("click", (event) => {
    event.stopPropagation();
    onChange({ kind: "reorder", layerId: layer.id, direction: "down" });
  });
  orderButtons.append(upButton, downButton);

  controls.append(opacity, orderButtons);
  li.append(toggle, labelWrap, controls);
  return li;
}

function renderLegendHtml(layer: PortalViewerLayer): string {
  if (layer.legend.length === 0) return "";
  const rows = layer.legend
    .map(
      (row) =>
        `<div class="legend-row"><span class="legend-swatch" data-shape="${row.shape}" style="background-color:${escapeHtml(row.color)}"></span><span>${escapeHtml(row.label)}</span></div>`,
    )
    .join("");
  return `<div class="legend">${rows}</div>`;
}
