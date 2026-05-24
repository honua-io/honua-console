import { cleanup, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it } from "vitest";

import { type ContentItem, summarize } from "../../contracts/content-item.js";
import { CatalogCard } from "../components/CatalogCard.js";
import { loadCatalogFixtures } from "../fixtures.js";

afterEach(() => {
  cleanup();
});

const fixtures = loadCatalogFixtures();
const byId = (id: string): ContentItem => {
  const item = fixtures.items.get(id);
  if (!item) throw new Error(`fixture ${id} missing`);
  return item;
};

describe("CatalogCard openability gate", () => {
  it("renders the publisher-asserted unsupported pill on the legacy WMS imagery card", () => {
    const summaryView = summarize(byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAM"));
    expect(summaryView.viewerSupport).toEqual({
      supported: false,
      reason: expect.stringMatching(/WMS/i),
    });
    render(
      <MemoryRouter>
        <CatalogCard item={summaryView} />
      </MemoryRouter>,
    );
    const pill = screen.getByText(/Unsupported/i);
    expect(pill).toBeInTheDocument();
    expect(pill.getAttribute("title") ?? "").toMatch(/WMS/);
    expect(screen.queryByText(/Open in map/i)).not.toBeInTheDocument();
  });

  it("renders the unsupported pill for scenes (Beta has no scene viewer) without an extension override", () => {
    const summaryView = summarize(byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAF"));
    expect(summaryView.viewerSupport).toBeNull();
    render(
      <MemoryRouter>
        <CatalogCard item={summaryView} />
      </MemoryRouter>,
    );
    expect(screen.getByText(/Unsupported/i)).toBeInTheDocument();
    expect(screen.queryByText(/Open in map/i)).not.toBeInTheDocument();
  });

  it("renders the open-in-map pill for an openable service", () => {
    const summaryView = summarize(byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAB"));
    render(
      <MemoryRouter>
        <CatalogCard item={summaryView} />
      </MemoryRouter>,
    );
    expect(screen.getByText(/Open in map/i)).toBeInTheDocument();
  });

  // The app-builder proof link on catalog cards is a Studio surface and moves
  // with honua-console#5 (Port Studio app-builder and generated-app lifecycle).
  // The catalog card here only exposes the openability gate (open-in-map /
  // open-external / unsupported).

  it("renders the open-external pill for an external-url card", () => {
    const summaryView = summarize(byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAJ"));
    render(
      <MemoryRouter>
        <CatalogCard item={summaryView} />
      </MemoryRouter>,
    );
    expect(screen.getByText(/Open external link/i)).toBeInTheDocument();
  });
});
