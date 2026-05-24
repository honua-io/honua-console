import { describe, expect, it, vi } from "vitest";
import { buildSamplePortalItem, getSampleSourceFeatures } from "../catalog/sample-portal-item.js";
import { createFeatureTable } from "./feature-detail.js";
import { deriveFeatureId } from "./feature-id.js";

describe("createFeatureTable collaboration cues", () => {
  it("marks rows another participant is editing", () => {
    const item = buildSamplePortalItem();
    const layer = item.layers[0];
    const features = getSampleSourceFeatures(item, layer.sourceId);
    const selectedFeatureId = deriveFeatureId(layer.id, features[0], 0);
    const lockedFeatureId = deriveFeatureId(layer.id, features[1], 1);
    const hosts = createHosts();

    createFeatureTable(hosts, vi.fn()).render(layer, features, {
      selectedFeatureId,
      collaborationLocks: [
        {
          layerId: layer.id,
          featureId: lockedFeatureId,
          participantId: "u-kai",
          participantName: "Kai Torres",
          color: "#f3b562",
          status: "editing",
        },
      ],
    });

    expect(hosts.head).toHaveTextContent("Collaboration");
    const lockedRow = hosts.body.querySelector<HTMLTableRowElement>(`[data-feature-id="${lockedFeatureId}"]`);
    expect(lockedRow).not.toBeNull();
    expect(lockedRow).toHaveAttribute("data-collaboration", "editing");
    expect(lockedRow).toHaveAttribute("data-collaboration-participant", "u-kai");
    expect(lockedRow).toHaveTextContent("Kai Torres");
    expect(lockedRow).toHaveTextContent("Editing");
    expect(hosts.body.querySelector(`[data-feature-id="${selectedFeatureId}"]`)).toHaveAttribute(
      "data-selected",
      "true",
    );
  });

  it("keeps the original table shape when no collaboration locks are present", () => {
    const item = buildSamplePortalItem();
    const layer = item.layers[0];
    const features = getSampleSourceFeatures(item, layer.sourceId);
    const selectedFeatureId = deriveFeatureId(layer.id, features[0], 0);
    const hosts = createHosts();

    createFeatureTable(hosts, vi.fn()).render(layer, features, selectedFeatureId);

    expect(hosts.head).not.toHaveTextContent("Collaboration");
    expect(hosts.body.querySelector("[data-collaboration]")).toBeNull();
    expect(hosts.body.querySelector(`[data-feature-id="${selectedFeatureId}"]`)).toHaveAttribute(
      "data-selected",
      "true",
    );
  });
});

function createHosts() {
  return {
    head: document.createElement("thead"),
    body: document.createElement("tbody"),
    layerLabel: document.createElement("span"),
    rowCount: document.createElement("span"),
  };
}
