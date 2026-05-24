import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";

import { studioPublishingClient } from "../studio/publishing/fixtureClient.js";
import { StudioPublishingError } from "../studio/publishing/types.js";
import { StudioDraftPage } from "./StudioDraftPage.js";
import { StudioItemEditPage } from "./StudioItemEditPage.js";
import { StudioPreviewPage } from "./StudioPreviewPage.js";

describe("Studio loader error surfaces", () => {
  beforeEach(() => {
    studioPublishingClient.reset();
    window.sessionStorage.clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("preserves unauthorized draft loader failures", async () => {
    vi.spyOn(studioPublishingClient, "getDraft").mockRejectedValueOnce(
      new StudioPublishingError("unauthorized", "You cannot read this Studio draft.")
    );

    render(
      <MemoryRouter initialEntries={["/studio/drafts/restricted-draft"]}>
        <Routes>
          <Route path="/studio/drafts/:draftId" element={<StudioDraftPage />} />
        </Routes>
      </MemoryRouter>
    );

    expect(await screen.findByTestId("unauthorized-state")).toBeVisible();
    expect(screen.getByRole("heading", { name: "Draft unavailable" })).toBeVisible();
    expect(screen.getByText("You cannot read this Studio draft.")).toBeVisible();
  });

  it("preserves unsupported preview loader failures", async () => {
    vi.spyOn(studioPublishingClient, "getDraft").mockRejectedValueOnce(
      new StudioPublishingError("unsupported", "Generated preview package binding is not supported.")
    );

    render(
      <MemoryRouter initialEntries={["/studio/previews/unsupported-draft"]}>
        <Routes>
          <Route path="/studio/previews/:draftId" element={<StudioPreviewPage />} />
        </Routes>
      </MemoryRouter>
    );

    expect(await screen.findByTestId("unsupported-state")).toBeVisible();
    expect(screen.getByRole("heading", { name: "Preview unavailable" })).toBeVisible();
    expect(screen.getByText("Generated preview package binding is not supported.")).toBeVisible();
  });

  it("preserves unsupported reopen loader failures", async () => {
    vi.spyOn(studioPublishingClient, "reopenPublishedItem").mockRejectedValueOnce(
      new StudioPublishingError("unsupported", "Published package binding is not supported by Studio.")
    );

    render(
      <MemoryRouter initialEntries={["/studio/items/unsupported-item/edit"]}>
        <Routes>
          <Route path="/studio/items/:itemId/edit" element={<StudioItemEditPage />} />
        </Routes>
      </MemoryRouter>
    );

    expect(await screen.findByTestId("unsupported-state")).toBeVisible();
    expect(screen.getByRole("heading", { name: "Cannot reopen item" })).toBeVisible();
    expect(screen.getByText("Published package binding is not supported by Studio.")).toBeVisible();
  });
});
