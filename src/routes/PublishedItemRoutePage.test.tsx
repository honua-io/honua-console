import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Route, Routes } from "react-router-dom";

import { studioPublishingClient } from "../studio/publishing/fixtureClient.js";
import type { ShareEmbedSettings } from "../studio/publishing/types.js";
import { StudioPublishingError } from "../studio/publishing/types.js";
import { PublishedItemRoutePage } from "./PublishedItemRoutePage.js";

const WORKSPACE_SHARE: ShareEmbedSettings = {
  visibility: "workspace",
  groupIds: [],
  publicLinkEnabled: false,
  embedEnabled: false,
  embedPolicy: "disabled"
};

describe("PublishedItemRoutePage", () => {
  beforeEach(() => {
    studioPublishingClient.reset();
    window.sessionStorage.clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("recovers from a missing item when route params change to a valid item", async () => {
    await studioPublishingClient.publishDraft({
      draftId: "draft-map-operations",
      title: "Route recovery map",
      summary: "Published after a stale missing route.",
      tags: ["route", "recovery"],
      targetAudience: "Console builders",
      versionNote: "Route recovery",
      share: WORKSPACE_SHARE
    });

    render(
      <MemoryRouter initialEntries={["/catalog/missing-item"]}>
        <Link to="/catalog/console-map-operations">Open valid item</Link>
        <Routes>
          <Route path="/catalog/:itemId" element={<PublishedItemRoutePage surface="catalog" />} />
        </Routes>
      </MemoryRouter>
    );

    expect(await screen.findByText("Published item missing-item was not found.")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("link", { name: "Open valid item" }));

    expect(await screen.findByTestId("route-catalog")).toBeVisible();
    expect(screen.getByRole("heading", { name: "Route recovery map" })).toBeVisible();
    expect(screen.queryByText("Published item missing-item was not found.")).not.toBeInTheDocument();
  });

  it("preserves unauthorized loader failures through the shared empty-state taxonomy", async () => {
    vi.spyOn(studioPublishingClient, "getPublishedItem").mockRejectedValueOnce(
      new StudioPublishingError("unauthorized", "You do not have access to this published item.")
    );

    render(
      <MemoryRouter initialEntries={["/catalog/restricted-item"]}>
        <Routes>
          <Route path="/catalog/:itemId" element={<PublishedItemRoutePage surface="catalog" />} />
        </Routes>
      </MemoryRouter>
    );

    expect(await screen.findByTestId("unauthorized-state")).toBeVisible();
    expect(screen.getByRole("heading", { name: "Published item unavailable" })).toBeVisible();
    expect(screen.getByText("You do not have access to this published item.")).toBeVisible();
  });

  it("preserves unsupported loader failures through the shared empty-state taxonomy", async () => {
    vi.spyOn(studioPublishingClient, "getPublishedItem").mockRejectedValueOnce(
      new StudioPublishingError("unsupported", "This package binding is not supported by Console.")
    );

    render(
      <MemoryRouter initialEntries={["/apps/unsupported-item/preview"]}>
        <Routes>
          <Route path="/apps/:itemId/preview" element={<PublishedItemRoutePage surface="app-preview" />} />
        </Routes>
      </MemoryRouter>
    );

    expect(await screen.findByTestId("unsupported-state")).toBeVisible();
    expect(screen.getByRole("heading", { name: "Published item unavailable" })).toBeVisible();
    expect(screen.getByText("This package binding is not supported by Console.")).toBeVisible();
  });
});
