import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { Link, MemoryRouter, Route, Routes } from "react-router-dom";

import { FixtureStudioPublishingClient, studioPublishingClient } from "./fixtureClient.js";
import { StudioPublishReviewPage } from "./StudioPublishReviewPage.js";
import type { PublishedContentItem, StudioPublishReviewInput } from "./types.js";
import { StudioPublishingError } from "./types.js";

describe("StudioPublishReviewPage", () => {
  beforeEach(() => {
    studioPublishingClient.reset();
    window.sessionStorage.clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
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

  it("clears a stale submit error when route params change to another draft", async () => {
    render(
      <MemoryRouter initialEntries={["/studio/drafts/draft-map-operations/publish"]}>
        <Link to="/studio/drafts/draft-dashboard-operations/publish">Open dashboard draft</Link>
        <Routes>
          <Route path="/studio/drafts/:draftId/publish" element={<StudioPublishReviewPage />} />
        </Routes>
      </MemoryRouter>
    );

    const form = await screen.findByRole("form", { name: "Publish review form" });
    fireEvent.change(screen.getByLabelText("Visibility"), { target: { value: "group" } });
    fireEvent.submit(form);

    expect(await screen.findByRole("alert")).toHaveTextContent("Choose at least one group");

    fireEvent.click(screen.getByRole("link", { name: "Open dashboard draft" }));

    expect(await screen.findByRole("heading", { name: "Operations response dashboard" })).toBeVisible();
    await waitFor(() => {
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    });
    expect(screen.getByTestId("publish-submit")).toBeEnabled();
  });

  it("ignores an in-flight publish success after route params change to another draft", async () => {
    const publish = createDeferred<PublishedContentItem>();
    let submittedInput: StudioPublishReviewInput | undefined;
    vi.spyOn(studioPublishingClient, "publishDraft").mockImplementationOnce((input) => {
      submittedInput = input;
      return publish.promise;
    });

    render(
      <MemoryRouter initialEntries={["/studio/drafts/draft-map-operations/publish"]}>
        <Link to="/studio/drafts/draft-dashboard-operations/publish">Open dashboard draft</Link>
        <Routes>
          <Route path="/studio/drafts/:draftId/publish" element={<StudioPublishReviewPage />} />
        </Routes>
      </MemoryRouter>
    );

    const form = await screen.findByRole("form", { name: "Publish review form" });
    fireEvent.submit(form);
    expect(screen.getByTestId("publish-submit")).toHaveTextContent("Publishing");

    fireEvent.click(screen.getByRole("link", { name: "Open dashboard draft" }));
    expect(await screen.findByRole("heading", { name: "Operations response dashboard" })).toBeVisible();

    const staleItem = await publishItemFromInput(submittedInput);
    await act(async () => {
      publish.resolve(staleItem);
      await publish.promise;
    });

    expect(screen.getByRole("heading", { name: "Operations response dashboard" })).toBeVisible();
    await waitFor(() => {
      expect(screen.queryByTestId("publish-result")).not.toBeInTheDocument();
    });
    expect(screen.getByTestId("publish-submit")).toBeEnabled();
  });

  it("ignores an in-flight publish failure after route params change to another draft", async () => {
    const publish = createDeferred<PublishedContentItem>();
    vi.spyOn(studioPublishingClient, "publishDraft").mockImplementationOnce(() => publish.promise);

    render(
      <MemoryRouter initialEntries={["/studio/drafts/draft-map-operations/publish"]}>
        <Link to="/studio/drafts/draft-dashboard-operations/publish">Open dashboard draft</Link>
        <Routes>
          <Route path="/studio/drafts/:draftId/publish" element={<StudioPublishReviewPage />} />
        </Routes>
      </MemoryRouter>
    );

    const form = await screen.findByRole("form", { name: "Publish review form" });
    fireEvent.submit(form);

    fireEvent.click(screen.getByRole("link", { name: "Open dashboard draft" }));
    expect(await screen.findByRole("heading", { name: "Operations response dashboard" })).toBeVisible();

    await act(async () => {
      publish.reject(new StudioPublishingError("server", "The old draft publish failed."));
      try {
        await publish.promise;
      } catch {
        // The component owns the rejected publish; this await keeps React state updates inside act.
      }
    });

    expect(screen.getByRole("heading", { name: "Operations response dashboard" })).toBeVisible();
    await waitFor(() => {
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    });
    expect(screen.queryByText("The old draft publish failed.")).not.toBeInTheDocument();
    expect(screen.getByTestId("publish-submit")).toBeEnabled();
  });

  it("keeps a newer publish submit pending when an older draft submit completes", async () => {
    const publishes = [createDeferred<PublishedContentItem>(), createDeferred<PublishedContentItem>()];
    const submittedInputs: StudioPublishReviewInput[] = [];
    vi.spyOn(studioPublishingClient, "publishDraft").mockImplementation((input) => {
      submittedInputs.push(input);
      return publishes[submittedInputs.length - 1].promise;
    });

    render(
      <MemoryRouter initialEntries={["/studio/drafts/draft-map-operations/publish"]}>
        <Link to="/studio/drafts/draft-dashboard-operations/publish">Open dashboard draft</Link>
        <Routes>
          <Route path="/studio/drafts/:draftId/publish" element={<StudioPublishReviewPage />} />
        </Routes>
      </MemoryRouter>
    );

    const mapForm = await screen.findByRole("form", { name: "Publish review form" });
    fireEvent.submit(mapForm);

    fireEvent.click(screen.getByRole("link", { name: "Open dashboard draft" }));
    expect(await screen.findByRole("heading", { name: "Operations response dashboard" })).toBeVisible();

    const dashboardForm = await screen.findByRole("form", { name: "Publish review form" });
    fireEvent.submit(dashboardForm);
    expect(screen.getByTestId("publish-submit")).toHaveTextContent("Publishing");

    const staleItem = await publishItemFromInput(submittedInputs[0]);
    await act(async () => {
      publishes[0].resolve(staleItem);
      await publishes[0].promise;
    });

    expect(screen.getByTestId("publish-submit")).toHaveTextContent("Publishing");
    expect(screen.queryByTestId("publish-result")).not.toBeInTheDocument();

    const currentItem = await publishItemFromInput(submittedInputs[1]);
    await act(async () => {
      publishes[1].resolve(currentItem);
      await publishes[1].promise;
    });

    expect(await screen.findByTestId("publish-result")).toBeVisible();
    expect(screen.getByTestId("publish-submit")).toBeEnabled();
  });
});

function createDeferred<T>(): {
  readonly promise: Promise<T>;
  readonly resolve: (value: T) => void;
  readonly reject: (reason: unknown) => void;
} {
  let resolve: (value: T) => void = () => undefined;
  let reject: (reason: unknown) => void = () => undefined;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

async function publishItemFromInput(input: StudioPublishReviewInput | undefined): Promise<PublishedContentItem> {
  if (!input) {
    throw new Error("Expected publish input to be captured.");
  }
  const client = new FixtureStudioPublishingClient();
  const item = await client.publishDraft(input);
  window.sessionStorage.clear();
  return item;
}
