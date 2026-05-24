import { describe, expect, it } from "vitest";
import { createMetadataPanel } from "./metadata-panel.js";

describe("createMetadataPanel", () => {
  it("renders http service URLs as links", () => {
    const root = document.createElement("div");
    createMetadataPanel(root).render({
      id: "item-1",
      title: "Layer",
      serviceUrl: "https://api.honua.example/layers/1",
    });

    const link = root.querySelector("a");
    expect(link).toHaveAttribute("href", "https://api.honua.example/layers/1");
  });

  it("renders unsafe service URL schemes as escaped text", () => {
    const root = document.createElement("div");
    createMetadataPanel(root).render({
      id: "item-1",
      title: "Layer",
      serviceUrl: "javascript:alert(1)",
    });

    expect(root.querySelector("a")).toBeNull();
    expect(root.textContent).toContain("javascript:alert(1)");
  });
});
