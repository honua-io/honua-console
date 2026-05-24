import { describe, expect, it } from "vitest";
import { buildMissingItemMessage, renderItemMissing } from "../../src/viewer/error-surface.js";

function createHosts() {
  document.body.innerHTML = `
    <div data-metadata-grid></div>
    <ul data-layer-list></ul>
    <div data-feature-detail></div>
    <h1 data-portal-item-title></h1>
    <button data-share-url-button></button>
    <p data-status></p>
    <table>
      <thead data-feature-table-head></thead>
      <tbody data-feature-table-body></tbody>
    </table>
    <span data-table-layer-label></span>
    <span data-table-row-count></span>
  `;
  return {
    metadataGrid: document.querySelector<HTMLElement>("[data-metadata-grid]")!,
    layerList: document.querySelector<HTMLElement>("[data-layer-list]")!,
    featureDetail: document.querySelector<HTMLElement>("[data-feature-detail]")!,
    itemTitle: document.querySelector<HTMLElement>("[data-portal-item-title]")!,
    shareButton: document.querySelector<HTMLButtonElement>("[data-share-url-button]")!,
    status: document.querySelector<HTMLElement>("[data-status]")!,
    tableHead: document.querySelector<HTMLElement>("[data-feature-table-head]")!,
    tableBody: document.querySelector<HTMLElement>("[data-feature-table-body]")!,
    tableLayerLabel: document.querySelector<HTMLElement>("[data-table-layer-label]")!,
    tableRowCount: document.querySelector<HTMLElement>("[data-table-row-count]")!,
  };
}

describe("renderItemMissing", () => {
  it("escapes a hostile item id so the empty-state cannot inject markup", () => {
    const hosts = createHosts();
    const message = buildMissingItemMessage({
      status: "not-found",
      itemId: '<img src=x onerror="alert(1)"><script>alert(2)</script>',
    });

    renderItemMissing(hosts, message);

    // No parsed nodes for the hostile payload should appear in any host.
    for (const host of [hosts.metadataGrid, hosts.layerList, hosts.featureDetail, hosts.tableBody]) {
      expect(host.querySelector("img")).toBeNull();
      expect(host.querySelector("script")).toBeNull();
      // The id text round-trips through innerHTML, but as escaped HTML entities
      // — `&lt;` is the canonical encoding used by escapeHtml.
      expect(host.innerHTML).toContain("&lt;img src=x");
      expect(host.innerHTML).toContain("&lt;script&gt;");
    }
  });

  it("disables the share button and marks status as error", () => {
    const hosts = createHosts();
    renderItemMissing(hosts, "anything");
    expect(hosts.shareButton.disabled).toBe(true);
    expect(hosts.status.dataset["state"]).toBe("error");
    expect(hosts.itemTitle.textContent).toBe("Item unavailable");
  });
});

describe("buildMissingItemMessage", () => {
  it("formats a not-found message with the requested id", () => {
    const message = buildMissingItemMessage({ status: "not-found", itemId: "missing" });
    expect(message).toContain('"missing"');
  });

  it("formats an error message with the underlying reason", () => {
    const message = buildMissingItemMessage({ status: "error", itemId: "x", message: "boom" });
    expect(message).toContain('"x"');
    expect(message).toContain("boom");
  });
});
