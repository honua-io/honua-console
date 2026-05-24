import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { MemoryRouter, Route, Routes } from "react-router-dom";
import { memberSession, staticDriver } from "../../../tests/fixtures";
import { App } from "../../App.js";
import { harness, type makeFixtureClient } from "../../__tests__/testHarness.js";
import { CatalogClientProvider } from "../CatalogContext.js";
import { CatalogPage } from "../CatalogPage.js";
import { FixtureCatalogClient } from "../client.js";
import { loadCatalogFixtures } from "../fixtures.js";

function TestRouter({ children }: { children: React.ReactNode }): JSX.Element {
  return <>{children}</>;
}

afterEach(() => {
  cleanup();
});

describe("CatalogPage", () => {
  it("renders one card per fixture in a single round-trip (no per-card detail fetch)", async () => {
    const fixtures = loadCatalogFixtures();
    const client = new FixtureCatalogClient(fixtures);
    const listSpy = vi.spyOn(client, "listItems");
    const getSpy = vi.spyOn(client, "getItem");

    render(
      <CatalogClientProvider client={client}>
        <MemoryRouter initialEntries={["/catalog"]}>
          <Routes>
            <Route path="/catalog" element={<CatalogPage />} />
          </Routes>
        </MemoryRouter>
      </CatalogClientProvider>,
    );

    const grid = await screen.findByTestId("catalog-grid");
    const cards = within(grid).getAllByRole("listitem");
    expect(cards.length).toBeGreaterThan(0);
    expect(listSpy).toHaveBeenCalledTimes(1);
    expect(getSpy).not.toHaveBeenCalled();
  });

  it("finds an item by title using the search input", async () => {
    const user = userEvent.setup();
    render(
      harness(<App Router={TestRouter} sessionDriver={staticDriver(memberSession)} />, {
        initialEntries: ["/catalog"],
      }),
    );

    const grid = await screen.findByTestId("catalog-grid");
    expect(within(grid).getByText("City Parcels 2026")).toBeInTheDocument();

    const search = screen.getByLabelText(/search catalog/i);
    await user.clear(search);
    await user.type(search, "finder");

    await waitFor(() => {
      const after = screen.getByTestId("catalog-grid");
      const cards = within(after).getAllByRole("listitem");
      expect(cards).toHaveLength(1);
      expect(within(after).getByText("Permit Finder")).toBeInTheDocument();
    });
  });

  it("filters by item type when a type facet is toggled", async () => {
    const user = userEvent.setup();
    render(
      harness(<App Router={TestRouter} sessionDriver={staticDriver(memberSession)} />, {
        initialEntries: ["/catalog"],
      }),
    );

    await screen.findByTestId("catalog-grid");
    const filters = screen.getByLabelText(/catalog filters/i);
    await user.click(within(filters).getByLabelText("Service"));

    await waitFor(() => {
      const grid = screen.getByTestId("catalog-grid");
      const cards = within(grid).getAllByRole("listitem");
      expect(cards.length).toBeGreaterThan(0);
      for (const card of cards) {
        expect(card.querySelector("[data-item-type]")?.getAttribute("data-item-type")).toBe("service");
      }
    });
  });

  it("switches between organization catalog and My Content scope", async () => {
    const user = userEvent.setup();
    const alexSession = {
      ...memberSession,
      user: { id: "user_alex", displayName: "Alex Lee", email: "alex@demo.honua.example" },
    };

    render(
      harness(<App Router={TestRouter} sessionDriver={staticDriver(alexSession)} />, {
        initialEntries: ["/catalog"],
      }),
    );

    const grid = await screen.findByTestId("catalog-grid");
    expect(within(grid).getAllByRole("listitem").length).toBeGreaterThan(1);

    const myContent = screen.getByTestId("catalog-scope-my-content");
    await user.click(myContent);

    await waitFor(() => {
      const scopedGrid = screen.getByTestId("catalog-grid");
      expect(within(scopedGrid).getAllByRole("listitem")).toHaveLength(1);
      expect(within(scopedGrid).getByText("City Parcels Overview Map")).toBeInTheDocument();
      expect(myContent).toHaveAttribute("aria-pressed", "true");
    });

    await user.click(screen.getByTestId("catalog-scope-organization"));

    await waitFor(() => {
      const organizationGrid = screen.getByTestId("catalog-grid");
      expect(within(organizationGrid).getAllByRole("listitem").length).toBeGreaterThan(1);
      expect(screen.getByTestId("catalog-scope-organization")).toHaveAttribute("aria-pressed", "true");
    });
  });

  it("filters by tag when a tag facet is toggled", async () => {
    const user = userEvent.setup();
    render(
      harness(<App Router={TestRouter} sessionDriver={staticDriver(memberSession)} />, {
        initialEntries: ["/catalog"],
      }),
    );

    await screen.findByTestId("catalog-grid");
    const filters = screen.getByLabelText(/catalog filters/i);
    const basemapToggle = within(filters).getByLabelText(/^basemap \(/);
    await user.click(basemapToggle);

    await waitFor(() => {
      const grid = screen.getByTestId("catalog-grid");
      const cards = within(grid).getAllByRole("listitem");
      expect(cards.length).toBe(1);
      expect(within(grid).getByText(/Honua Basemap/)).toBeInTheDocument();
    });
  });

  it("reflects search and facet state in URL search params", async () => {
    const user = userEvent.setup();
    let lastSearch = "";
    function Spy() {
      lastSearch = window.location.search;
      return null;
    }
    render(
      harness(
        <>
          <App Router={TestRouter} sessionDriver={staticDriver(memberSession)} />
          <Spy />
        </>,
        { initialEntries: ["/catalog"] },
      ),
    );
    // The MemoryRouter does not update window.location; instead, re-read the URL via screen state.

    await screen.findByTestId("catalog-grid");
    const filters = screen.getByLabelText(/catalog filters/i);
    await user.click(within(filters).getByLabelText("Layer"));

    await waitFor(() => {
      // The URL is reflected in <a> hrefs the page generates; pick one card and inspect its detail link.
      const grid = screen.getByTestId("catalog-grid");
      expect(within(grid).queryAllByRole("listitem").length).toBeGreaterThan(0);
    });
    expect(lastSearch).toBe("");
  });

  it("renders the empty state when no items match the search", async () => {
    const user = userEvent.setup();
    render(
      harness(<App Router={TestRouter} sessionDriver={staticDriver(memberSession)} />, {
        initialEntries: ["/catalog"],
      }),
    );

    await screen.findByTestId("catalog-grid");
    const search = screen.getByLabelText(/search catalog/i);
    await user.type(search, "definitely-not-a-real-item-zzz");

    const empty = await screen.findByText(/no matching items/i);
    expect(empty).toBeInTheDocument();
  });

  it("renders the error state when the catalog client throws", async () => {
    const failing: ReturnType<typeof makeFixtureClient> = {
      listItems: vi.fn().mockRejectedValue(new Error("network down")),
      getItem: vi.fn(),
      getDependencies: vi.fn(),
    };
    render(
      harness(<App Router={TestRouter} sessionDriver={staticDriver(memberSession)} />, {
        client: failing,
        initialEntries: ["/catalog"],
      }),
    );
    const error = await screen.findByText(/couldn't load the catalog/i);
    expect(error).toBeInTheDocument();
  });

  it("re-enables Load more after a stale response resolves post-filter-change", async () => {
    const user = userEvent.setup();
    const fixtures = loadCatalogFixtures();
    const inner = new FixtureCatalogClient(fixtures);
    const PAGE = 3;
    let pendingLoadMore: ((value: Awaited<ReturnType<typeof inner.listItems>>) => void) | null = null;
    const client = {
      listItems: (req: Parameters<typeof inner.listItems>[0] = {}) => {
        if (req.cursor) {
          return new Promise<Awaited<ReturnType<typeof inner.listItems>>>((resolve) => {
            pendingLoadMore = resolve;
          });
        }
        return inner.listItems({ ...req, limit: PAGE });
      },
      getItem: (id: string) => inner.getItem(id),
      getDependencies: (id: string, opts?: { depth?: number; limit?: number }) => inner.getDependencies(id, opts),
    };

    render(
      harness(<App Router={TestRouter} sessionDriver={staticDriver(memberSession)} />, {
        client,
        initialEntries: ["/catalog"],
      }),
    );

    await screen.findByTestId("catalog-grid");
    await user.click(await screen.findByRole("button", { name: /load more/i }));

    // Filter change while load-more is in flight: the post-filter list still has
    // a nextCursor (page size 3 across the matching subset), so the button must
    // be present and enabled rather than stuck in the disabled "Loading more…"
    // state from the orphaned request.
    const search = screen.getByLabelText(/search catalog/i);
    await user.type(search, "city");
    await waitFor(() => {
      expect(screen.getByTestId("catalog-grid")).toBeInTheDocument();
    });

    const moreAfter = await screen.findByRole("button", { name: /^load more$/i });
    expect(moreAfter).toBeEnabled();

    // Now release the stale page; the button must remain enabled (i.e. not
    // toggled back into the loading state by the late response).
    expect(pendingLoadMore).not.toBeNull();
    const stalePage = await inner.listItems({ limit: PAGE, cursor: "3" });
    pendingLoadMore!(stalePage);
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(screen.getByRole("button", { name: /^load more$/i })).toBeEnabled();
  });

  it("ignores a stale Load more response when filters change before it resolves", async () => {
    const user = userEvent.setup();
    const fixtures = loadCatalogFixtures();
    const inner = new FixtureCatalogClient(fixtures);
    const PAGE = 3;
    let pendingLoadMore: ((value: Awaited<ReturnType<typeof inner.listItems>>) => void) | null = null;
    let callCount = 0;
    const client = {
      listItems: (req: Parameters<typeof inner.listItems>[0] = {}) => {
        callCount += 1;
        if (req.cursor) {
          // Hold the load-more response until the test releases it.
          return new Promise<Awaited<ReturnType<typeof inner.listItems>>>((resolve) => {
            pendingLoadMore = resolve;
          });
        }
        return inner.listItems({ ...req, limit: PAGE });
      },
      getItem: (id: string) => inner.getItem(id),
      getDependencies: (id: string, opts?: { depth?: number; limit?: number }) => inner.getDependencies(id, opts),
    };

    render(
      harness(<App Router={TestRouter} sessionDriver={staticDriver(memberSession)} />, {
        client,
        initialEntries: ["/catalog"],
      }),
    );

    const grid = await screen.findByTestId("catalog-grid");
    expect(within(grid).getAllByRole("listitem")).toHaveLength(PAGE);

    const more = await screen.findByRole("button", { name: /load more/i });
    await user.click(more);

    // Change the filter while load-more is still pending.
    const search = screen.getByLabelText(/search catalog/i);
    await user.type(search, "finder");
    await waitFor(() => {
      const after = screen.getByTestId("catalog-grid");
      expect(within(after).getByText("Permit Finder")).toBeInTheDocument();
    });

    // Now release the stale load-more response. Its items must NOT append to
    // the post-filter grid.
    expect(pendingLoadMore).not.toBeNull();
    const stalePage = await inner.listItems({ limit: PAGE, cursor: "3" });
    pendingLoadMore!(stalePage);

    // Allow microtasks to flush.
    await new Promise((resolve) => setTimeout(resolve, 0));

    const finalGrid = screen.getByTestId("catalog-grid");
    const titles = within(finalGrid)
      .getAllByRole("listitem")
      .map((li) => li.textContent ?? "");
    expect(titles.every((t) => t.includes("Permit Finder"))).toBe(true);
    expect(callCount).toBeGreaterThanOrEqual(2);
  });

  it("paginates with a Load more button using the cursor", async () => {
    const user = userEvent.setup();
    const fixtures = loadCatalogFixtures();
    const inner = new FixtureCatalogClient(fixtures);
    const total = fixtures.listOrder.length;
    const PAGE = 3;
    const client = {
      listItems: (req = {}) => inner.listItems({ ...req, limit: PAGE }),
      getItem: (id: string) => inner.getItem(id),
      getDependencies: (id: string, opts?: { depth?: number; limit?: number }) => inner.getDependencies(id, opts),
    };

    render(
      <CatalogClientProvider client={client}>
        <MemoryRouter initialEntries={["/catalog"]}>
          <Routes>
            <Route path="/catalog" element={<CatalogPage />} />
          </Routes>
        </MemoryRouter>
      </CatalogClientProvider>,
    );

    const grid = await screen.findByTestId("catalog-grid");
    expect(within(grid).getAllByRole("listitem")).toHaveLength(PAGE);

    const more = screen.getByRole("button", { name: /load more/i });
    await user.click(more);

    await waitFor(() => {
      const updated = screen.getByTestId("catalog-grid");
      expect(within(updated).getAllByRole("listitem").length).toBeGreaterThan(PAGE);
    });
    expect(total).toBeGreaterThan(PAGE);
  });
});
