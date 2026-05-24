/**
 * Empty/error surface used when a portal item cannot be loaded. The id
 * comes from `window.location.hash`, so the message must be escaped
 * before every `innerHTML` insertion or a crafted hash like
 * `#item=<img src=x onerror=alert(1)>` would inject markup into the
 * unavailable-item state.
 */

import { escapeHtml } from "./popup.js";

export interface MissingItemHosts {
  metadataGrid: HTMLElement;
  layerList: HTMLElement;
  featureDetail: HTMLElement;
  itemTitle: HTMLElement;
  shareButton: HTMLButtonElement;
  status: HTMLElement;
  tableHead: HTMLElement;
  tableBody: HTMLElement;
  tableLayerLabel: HTMLElement;
  tableRowCount: HTMLElement;
}

export function renderItemMissing(hosts: MissingItemHosts, message: string): void {
  const safe = escapeHtml(message);
  hosts.itemTitle.textContent = "Item unavailable";
  hosts.metadataGrid.innerHTML = `<p class="empty-copy">${safe}</p>`;
  hosts.layerList.innerHTML = `<li class="layer-row"><span>—</span><span>${safe}</span><span></span></li>`;
  hosts.featureDetail.innerHTML = `<p class="empty-copy">${safe}</p>`;
  hosts.tableLayerLabel.textContent = "Item unavailable";
  hosts.tableRowCount.textContent = "";
  hosts.tableHead.innerHTML = "";
  hosts.tableBody.innerHTML = `<tr><td class="empty-copy" colspan="6">${safe}</td></tr>`;
  hosts.status.textContent = "Portal item unavailable";
  hosts.status.dataset["state"] = "error";
  hosts.shareButton.disabled = true;
}

export function buildMissingItemMessage(
  result: { status: "not-found"; itemId: string } | { status: "error"; itemId: string; message: string },
): string {
  if (result.status === "not-found") {
    return `No portal item with id "${result.itemId}" is available in the viewer fixture.`;
  }
  return `Failed to load portal item "${result.itemId}": ${result.message}`;
}
