import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";

import { studioPublishingClient } from "./fixtureClient.js";
import { StudioPublishReviewPage } from "./StudioPublishReviewPage.js";

describe("StudioPublishReviewPage", () => {
  beforeEach(() => {
    studioPublishingClient.reset();
    window.sessionStorage.clear();
  });

  it("shows the error state when the initial draft lookup fails", async () => {
    render(
      <MemoryRouter initialEntries={["/studio/drafts/missing-draft/publish"]}>
        <Routes>
          <Route path="/studio/drafts/:draftId/publish" element={<StudioPublishReviewPage />} />
        </Routes>
      </MemoryRouter>
    );

    expect(await screen.findByRole("heading", { name: "Publish review unavailable" })).toBeVisible();
    expect(screen.getByText("Studio draft missing-draft was not found.")).toBeVisible();
    expect(screen.queryByRole("heading", { name: "Loading publish review" })).not.toBeInTheDocument();
  });

  it("clears a previous publish result when a later submit fails validation", async () => {
    render(
      <MemoryRouter initialEntries={["/studio/drafts/draft-map-operations/publish"]}>
        <Routes>
          <Route path="/studio/drafts/:draftId/publish" element={<StudioPublishReviewPage />} />
        </Routes>
      </MemoryRouter>
    );

    const form = await screen.findByRole("form", { name: "Publish review form" });

    fireEvent.submit(form);
    expect(await screen.findByTestId("publish-result")).toBeVisible();

    fireEvent.change(screen.getByLabelText("Visibility"), { target: { value: "group" } });
    fireEvent.submit(form);

    expect(await screen.findByRole("alert")).toHaveTextContent("Choose at least one group");
    await waitFor(() => {
      expect(screen.queryByTestId("publish-result")).not.toBeInTheDocument();
    });
  });
});
