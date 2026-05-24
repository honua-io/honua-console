import { cleanup, render, screen, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { CatalogClientProvider } from "../catalog/CatalogContext.js";
import type { CatalogClient } from "../catalog/client.js";
import { FixtureCatalogClient } from "../catalog/client.js";
import { loadCatalogFixtures } from "../catalog/fixtures.js";
import { OpenDataCollectionPage } from "./OpenDataCollectionPage.js";
import { OpenDataItemPage } from "./OpenDataItemPage.js";

afterEach(() => {
  cleanup();
});

function renderPublicRoute(
  initialEntry: string,
  client: CatalogClient = new FixtureCatalogClient(loadCatalogFixtures()),
): void {
  render(
    <CatalogClientProvider client={client}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route path="/public" element={<OpenDataCollectionPage />} />
          <Route path="/public/items/:idOrSlug" element={<OpenDataItemPage />} />
        </Routes>
      </MemoryRouter>
    </CatalogClientProvider>,
  );
}

describe("OpenDataCollectionPage", () => {
  it("lists public open-data services, layers, and documents without protected catalog actions", async () => {
    renderPublicRoute("/public");

    const grid = await screen.findByTestId("public-open-data-grid");
    expect(within(grid).getByRole("link", { name: "City Parcels 2026" })).toHaveAttribute(
      "href",
      "/public/items/city-parcels-2026",
    );
    expect(within(grid).getByRole("link", { name: "City Parcels Data Dictionary (PDF)" })).toBeInTheDocument();
    expect(within(grid).getByRole("link", { name: "Permit Status Feed (no docs URL)" })).toBeInTheDocument();
    expect(screen.queryByText("City Parcels Overview Map")).not.toBeInTheDocument();
    expect(screen.queryByText("Honua Events Stream")).not.toBeInTheDocument();
    expect(screen.queryByText(/Build app proof/i)).not.toBeInTheDocument();
  });

  it("searches within the public open-data collection", async () => {
    renderPublicRoute("/public?q=permit");

    const grid = await screen.findByTestId("public-open-data-grid");
    expect(within(grid).getByRole("link", { name: "Permit Status Feed (no docs URL)" })).toBeInTheDocument();
    expect(within(grid).queryByRole("link", { name: "City Parcels 2026" })).not.toBeInTheDocument();
  });
});

describe("OpenDataItemPage", () => {
  it("renders a public service with metadata, preview, access rows, API examples, and JSON-LD", async () => {
    renderPublicRoute("/public/items/city-parcels-2026");

    expect(await screen.findByRole("heading", { name: "City Parcels 2026" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Preview map" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Related items" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Metadata" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Revision history" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Downloads and APIs" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "API examples" })).toBeInTheDocument();
    expect(screen.getAllByText("City of Honua").length).toBeGreaterThan(0);
    expect(screen.getByText(/Creative Commons Attribution 4\.0/)).toBeInTheDocument();
    expect(screen.getByText("City of Honua, Department of Planning")).toBeInTheDocument();
    expect(screen.getByText("ArcGIS REST endpoint")).toBeInTheDocument();
    expect(screen.getByText("OGC API Features endpoint")).toBeInTheDocument();
    expect(screen.getByText("ArcGIS REST GeoJSON")).toBeInTheDocument();
    expect(screen.getByText(/resultRecordCount=10/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /City Parcels.*Active/ })).toHaveAttribute(
      "href",
      "/public/items/01HXY3ZK7N1J2Q9V8M0FQ2PWAC",
    );
    expect(screen.getByRole("link", { name: /City Parcels Data Dictionary/ })).toHaveAttribute(
      "href",
      "/public/items/01HXY3ZK7N1J2Q9V8M0FQ2PWAH",
    );
    expect(screen.getByText("city-parcels-2026.2026-05-06")).toBeInTheDocument();
    expect(screen.getByText(/assessor-export-2026-05-06T03:00Z/)).toBeInTheDocument();
    expect(screen.getByText(/metadata-level context only/i)).toBeInTheDocument();
    expect(screen.queryByTestId("share-panel")).not.toBeInTheDocument();

    const script = screen.getByTestId("dataset-json-ld");
    expect(script).toHaveAttribute("type", "application/ld+json");
    expect(JSON.parse(script.textContent ?? "{}")).toMatchObject({
      "@type": "Dataset",
      identifier: "01HXY3ZK7N1J2Q9V8M0FQ2PWAB",
      name: "City Parcels 2026",
      version: "city-parcels-2026.2026-05-06",
    });
  });

  it("renders document downloads and copyable download commands", async () => {
    renderPublicRoute("/public/items/parcels-data-dictionary");

    expect(await screen.findByRole("heading", { name: /City Parcels Data Dictionary/i })).toBeInTheDocument();
    expect(screen.getByText("Document download")).toBeInTheDocument();
    expect(screen.getByText("Download file")).toBeInTheDocument();
    expect(
      screen.getByText(/curl -L "https:\/\/docs\.honua\.example\/parcels-data-dictionary\.pdf"/),
    ).toBeInTheDocument();
  });

  it("does not expose non-open-data public items on public item URLs", async () => {
    renderPublicRoute("/public/items/city-parcels-overview-map");

    expect(await screen.findByRole("heading", { name: "Public item not found" })).toBeInTheDocument();
    expect(screen.queryByText("City Parcels Overview Map")).not.toBeInTheDocument();
    expect(screen.queryByTestId("dataset-json-ld")).not.toBeInTheDocument();
  });

  it("does not leak private item titles on public item URLs", async () => {
    renderPublicRoute("/public/items/internal-staff-roster-layer");

    expect(await screen.findByRole("heading", { name: "Public item not found" })).toBeInTheDocument();
    expect(screen.queryByText("Internal Staff Roster Layer")).not.toBeInTheDocument();
  });

  it("keeps real load errors distinct from private or missing items", async () => {
    const client = new FixtureCatalogClient(loadCatalogFixtures());
    vi.spyOn(client, "getItem").mockRejectedValue(new Error("network down"));

    renderPublicRoute("/public/items/city-parcels-2026", client);

    expect(await screen.findByRole("heading", { name: "Public item could not load" })).toBeInTheDocument();
    expect(screen.queryByText("Public item not found")).not.toBeInTheDocument();
  });
});
