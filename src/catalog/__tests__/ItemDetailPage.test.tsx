import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { memberSession, staticDriver } from "../../../tests/fixtures";
import { App } from "../../App.js";
import { harness } from "../../__tests__/testHarness.js";
import { SessionProvider } from "../../auth/SessionContext.js";
import type { AuthenticatedSession, UnauthenticatedSession } from "../../auth/types.js";
import type { ContentItem } from "../../contracts/content-item.js";
import { CatalogClientProvider } from "../CatalogContext.js";
import { ItemDetailPage } from "../ItemDetailPage.js";
import { FixtureCatalogClient } from "../client.js";
import { loadCatalogFixtures } from "../fixtures.js";

function TestRouter({ children }: { children: React.ReactNode }): JSX.Element {
  return <>{children}</>;
}

afterEach(() => {
  cleanup();
});

type RenderDetailOptions = {
  readonly override?: (item: ContentItem) => ContentItem;
  readonly session?: AuthenticatedSession | UnauthenticatedSession;
};

const alexSession: AuthenticatedSession = {
  ...memberSession,
  user: { id: "user_alex", displayName: "Alex Lee", email: "alex@demo.honua.example" },
};

function renderDetail(
  idOrSlug: string,
  overrideOrOptions?: ((item: ContentItem) => ContentItem) | RenderDetailOptions,
) {
  const fixtures = loadCatalogFixtures();
  const override = typeof overrideOrOptions === "function" ? overrideOrOptions : overrideOrOptions?.override;
  const session =
    typeof overrideOrOptions === "function" ? memberSession : (overrideOrOptions?.session ?? memberSession);
  let fixtureData = fixtures;
  if (override) {
    const item = fixtures.items.get(idOrSlug);
    if (!item) throw new Error(`fixture ${idOrSlug} missing`);
    const items = new Map(fixtures.items);
    items.set(item.id, override(item));
    fixtureData = { ...fixtures, items };
  }
  const client = new FixtureCatalogClient(fixtureData);
  return {
    client,
    rendered: render(
      <SessionProvider driver={staticDriver(session)}>
        <CatalogClientProvider client={client}>
          <MemoryRouter initialEntries={[`/catalog/${encodeURIComponent(idOrSlug)}`]}>
            <Routes>
              <Route path="/catalog/:idOrSlug" element={<ItemDetailPage />} />
            </Routes>
          </MemoryRouter>
        </CatalogClientProvider>
      </SessionProvider>,
    ),
  };
}

describe("ItemDetailPage — section coverage by type", () => {
  it("renders all sections for a service detail in one round-trip", async () => {
    const { client } = renderDetail("01HXY3ZK7N1J2Q9V8M0FQ2PWAB");
    const getSpy = vi.spyOn(client, "getItem");

    const heading = await screen.findByRole("heading", { name: "City Parcels 2026" });
    expect(heading).toBeInTheDocument();

    expect(screen.getByRole("heading", { name: "Description" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Extent" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Related items" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Metadata" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Revision history" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Endpoints" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Capabilities" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Dependencies" })).toBeInTheDocument();

    expect(screen.getByRole("link", { name: /Open in map/i })).toBeInTheDocument();
    expect(screen.getByText(/Creative Commons Attribution 4\.0/)).toBeInTheDocument();
    expect(screen.getByText(/City of Honua, Department of Planning/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /City Parcels.*Active/ })).toHaveAttribute(
      "href",
      "/public/items/01HXY3ZK7N1J2Q9V8M0FQ2PWAC",
    );
    expect(screen.getByText("city-parcels-2026.2026-05-06")).toBeInTheDocument();
    expect(screen.getByText(/row\/feature-level diffs/i)).toBeInTheDocument();

    expect(getSpy).toHaveBeenCalledTimes(0);
  });

  it("embeds Schema.org Dataset JSON-LD for public open-data detail pages", async () => {
    renderDetail("city-parcels-2026");
    await screen.findByRole("heading", { name: "City Parcels 2026" });

    const script = screen.getByTestId("dataset-json-ld");
    expect(script).toHaveAttribute("type", "application/ld+json");
    expect(JSON.parse(script.textContent ?? "{}")).toMatchObject({
      "@context": "https://schema.org",
      "@type": "Dataset",
      identifier: "01HXY3ZK7N1J2Q9V8M0FQ2PWAB",
      name: "City Parcels 2026",
      version: "city-parcels-2026.2026-05-06",
    });
  });

  it("omits Schema.org Dataset JSON-LD from non-open-data detail pages", async () => {
    renderDetail("city-parcels-overview-map");
    await screen.findByRole("heading", { name: "City Parcels Overview Map" });

    expect(screen.queryByTestId("dataset-json-ld")).not.toBeInTheDocument();
  });

  it("renders a layer detail with one direct dependency listed", async () => {
    renderDetail("01HXY3ZK7N1J2Q9V8M0FQ2PWAC");
    await screen.findByRole("heading", { name: /City Parcels — Active/ });
    const deps = screen.getByRole("heading", { name: "Dependencies" }).parentElement!;
    expect(within(deps).getByText("01HXY3ZK7N1J2Q9V8M0FQ2PWAB")).toBeInTheDocument();
  });

  it("renders a saved web map detail and points the action at the webmap ref", async () => {
    renderDetail("01HXY3ZK7N1J2Q9V8M0FQ2PWAD");
    const action = await screen.findByRole("link", { name: /Open in map/i });
    expect(action).toHaveAttribute("href", expect.stringMatching(/^\/maps\//));
  });

  it("disables Open in map for scenes with an explicit unsupported reason", async () => {
    renderDetail("01HXY3ZK7N1J2Q9V8M0FQ2PWAF");
    const button = await screen.findByRole("button", { name: /Unsupported/i });
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute("aria-disabled", "true");
    expect(screen.getByTestId("unsupported-reason")).toHaveTextContent(/scene/i);
  });

  it("opens an app externally with target.url in a new tab", async () => {
    renderDetail("01HXY3ZK7N1J2Q9V8M0FQ2PWAG");
    const link = await screen.findByRole("link", { name: /Open app/i });
    expect(link).toHaveAttribute("href", "https://apps.honua.example/permit-finder");
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", expect.stringContaining("noopener"));
  });

  it("opens a document externally", async () => {
    renderDetail("01HXY3ZK7N1J2Q9V8M0FQ2PWAH");
    const link = await screen.findByRole("link", { name: /Open document/i });
    expect(link).toHaveAttribute("href", "https://docs.honua.example/parcels-data-dictionary.pdf");
  });

  it("opens an external-url externally", async () => {
    renderDetail("01HXY3ZK7N1J2Q9V8M0FQ2PWAJ");
    const link = await screen.findByRole("link", { name: /Open external link/i });
    expect(link).toHaveAttribute("href", "https://state.honua.example/items/dem-mosaic-2024");
  });

  it("renders unsafe license URLs as text, not links", async () => {
    renderDetail("01HXY3ZK7N1J2Q9V8M0FQ2PWAB", (item) => ({
      ...item,
      license: { ...item.license, url: "javascript:alert(1)" },
    }));
    await screen.findByRole("heading", { name: "City Parcels 2026" });
    const licenseText = screen.getByText("Creative Commons Attribution 4.0");
    expect(licenseText.closest("a")).toBeNull();
  });

  it("disables Open in map for a publisher-asserted unsupported service", async () => {
    renderDetail("01HXY3ZK7N1J2Q9V8M0FQ2PWAM");
    const button = await screen.findByRole("button", { name: /Unsupported/i });
    expect(button).toBeDisabled();
    expect(screen.getByTestId("unsupported-reason")).toHaveTextContent(/WMS/);
  });

  it("renders empty cells consistently for items missing extent / attribution / spdx", async () => {
    renderDetail("01HXY3ZK7N1J2Q9V8M0FQ2PWAJ");
    await screen.findByRole("heading", { name: /State Elevation Mosaic/ });
    expect(screen.queryByRole("heading", { name: "Extent" })).not.toBeInTheDocument();
    const cells = screen.getAllByLabelText(/not provided/i);
    expect(cells.length).toBeGreaterThan(0);
  });
});

describe("ItemDetailPage — error surfaces", () => {
  it("renders the unauthorized empty surface for a 403", async () => {
    renderDetail("01HXY3ZK7N1J2Q9V8M0FQ2PWAN");
    const heading = await screen.findByRole("heading", { name: /you don't have access/i });
    expect(heading).toBeInTheDocument();
  });

  it("renders the missing empty surface for an unknown id (distinct from unauthorized)", async () => {
    renderDetail("01HXY3ZK7N1J2Q9V8M0FQ2PWZZZ");
    const heading = await screen.findByRole("heading", { name: /item not found/i });
    expect(heading).toBeInTheDocument();
  });
});

describe("ItemDetailPage — closure", () => {
  it("walks the dependency closure on demand only", async () => {
    const user = userEvent.setup();
    const fixtures = loadCatalogFixtures();
    const client = new FixtureCatalogClient(fixtures);
    const closureSpy = vi.spyOn(client, "getDependencies");

    render(
      <SessionProvider driver={staticDriver(alexSession)}>
        <CatalogClientProvider client={client}>
          <MemoryRouter initialEntries={["/catalog/01HXY3ZK7N1J2Q9V8M0FQ2PWAK"]}>
            <Routes>
              <Route path="/catalog/:idOrSlug" element={<ItemDetailPage />} />
            </Routes>
          </MemoryRouter>
        </CatalogClientProvider>
      </SessionProvider>,
    );

    await screen.findByRole("heading", { name: /City Overview/ });
    expect(closureSpy).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: /show full dependency closure/i }));
    await waitFor(() => {
      expect(closureSpy).toHaveBeenCalledTimes(1);
    });
    const summary = await screen.findByTestId("closure-summary");
    expect(summary.textContent).toMatch(/unauthorized/);
    expect(summary.textContent).toMatch(/unsupported/);
    expect(summary.textContent).toMatch(/missing/);
  });
});

describe("ItemDetailPage — sharing and RBAC", () => {
  it("renders the role matrix and disables sharing controls for viewers", async () => {
    renderDetail("city-parcels-overview-map");

    await screen.findByRole("heading", { name: "City Parcels Overview Map" });
    const panel = await screen.findByTestId("share-panel");

    expect(within(panel).getByRole("table", { name: "Role matrix" })).toBeInTheDocument();
    expect(within(panel).getByText(/only owners and editors/i)).toBeInTheDocument();
    expect(within(panel).getByRole("button", { name: /apply sharing/i })).toBeDisabled();
    expect(within(panel).getByRole("button", { name: /add invite/i })).toBeDisabled();
  });

  it("lets an owner apply a permitted sharing change through dependency review", async () => {
    const user = userEvent.setup();
    const { client } = renderDetail("city-parcels-overview-map", { session: alexSession });
    const closureSpy = vi.spyOn(client, "getDependencies");

    await screen.findByRole("heading", { name: "City Parcels Overview Map" });
    const panel = await screen.findByTestId("share-panel");
    await waitFor(() => {
      expect(within(panel).getByRole("button", { name: /apply sharing/i })).toBeEnabled();
    });

    await user.selectOptions(within(panel).getByLabelText("Visibility"), "org");
    await user.click(within(panel).getByRole("button", { name: /apply sharing/i }));

    await waitFor(() => {
      expect(closureSpy).toHaveBeenCalledTimes(1);
    });
    expect(await within(panel).findByTestId("share-status")).toHaveTextContent(/sharing updated to organization/i);
  });

  it("blocks public sharing when dependency access was revoked or cannot be reviewed", async () => {
    const user = userEvent.setup();
    renderDetail("01HXY3ZK7N1J2Q9V8M0FQ2PWAK", { session: alexSession });

    await screen.findByRole("heading", { name: /City Overview/ });
    const panel = await screen.findByTestId("share-panel");
    await waitFor(() => {
      expect(within(panel).getByRole("button", { name: /apply sharing/i })).toBeEnabled();
    });

    await user.selectOptions(within(panel).getByLabelText("Visibility"), "public");
    await user.click(within(panel).getByRole("button", { name: /apply sharing/i }));

    const status = await within(panel).findByTestId("share-status");
    expect(status).toHaveTextContent(/revoked or denied dependency/i);
    expect(status).toHaveTextContent(/missing dependency/i);
  });

  it("records owner invite intents for the MVP share workflow", async () => {
    const user = userEvent.setup();
    renderDetail("city-parcels-overview-map", { session: alexSession });

    await screen.findByRole("heading", { name: "City Parcels Overview Map" });
    const panel = await screen.findByTestId("share-panel");
    await waitFor(() => {
      expect(within(panel).getByRole("button", { name: /add invite/i })).toBeEnabled();
    });

    await user.type(within(panel).getByLabelText("Invite email"), "editor@example.com");
    await user.selectOptions(within(panel).getByLabelText("Invite role"), "editor");
    await user.click(within(panel).getByRole("button", { name: /add invite/i }));

    expect(await within(panel).findByTestId("pending-invites")).toHaveTextContent("editor@example.com");
    expect(within(panel).getByTestId("pending-invites")).toHaveTextContent("Editor");
  });
});

describe("App routes", () => {
  it("navigates from a catalog card to the item detail page", async () => {
    const user = userEvent.setup();
    render(
      harness(<App Router={TestRouter} sessionDriver={staticDriver(memberSession)} />, {
        initialEntries: ["/catalog"],
      }),
    );
    const grid = await screen.findByTestId("catalog-grid");
    const link = within(grid).getAllByRole("link", { name: /City Parcels 2026/i })[0]!;
    await user.click(link);
    const heading = await screen.findByRole("heading", { name: "City Parcels 2026" });
    expect(heading).toBeInTheDocument();
  });

  it("redirects / to /catalog", async () => {
    render(
      harness(<App Router={TestRouter} sessionDriver={staticDriver(memberSession)} />, {
        initialEntries: ["/catalog"],
      }),
    );
    const heading = await screen.findByRole("heading", { name: /^Catalog$/ });
    expect(heading).toBeInTheDocument();
  });
});
