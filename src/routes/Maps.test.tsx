import { render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { STYLE_EDITOR_DEMO_CONTENT_ITEM_ID } from "../saved-maps";
import Maps, { MapViewerSurface } from "./Maps";

const initMapViewerMock = vi.hoisted(() =>
  vi.fn(() => ({
    ready: Promise.resolve(),
    dispose: vi.fn(),
  })),
);

vi.mock("../viewer/init", () => ({
  initMapViewer: initMapViewerMock,
}));

vi.mock("../auth/SessionContext", () => ({
  useSession: () => ({
    session: {
      status: "authenticated",
      user: { id: "u-member", displayName: "Mira Chen" },
      workspace: { id: "w-acme", name: "Acme Geospatial" },
    },
  }),
}));

describe("MapViewerSurface embed route params", () => {
  beforeEach(() => {
    initMapViewerMock.mockClear();
  });

  it("threads parsed embed params into viewer init", async () => {
    render(
      <MemoryRouter
        initialEntries={["/embed/maps/map-style-demo?chrome=none&legend=off&zoom=off&extent=-157.9,21.2,-157.7,21.4"]}
      >
        <Routes>
          <Route path="/embed/maps/:mapId" element={<MapViewerSurface mode="embed" />} />
        </Routes>
      </MemoryRouter>,
    );

    await waitFor(() => expect(initMapViewerMock).toHaveBeenCalled());

    expect(screen.getByTestId("embed-map-viewer-root")).toHaveAttribute("data-chrome", "none");
    expect(screen.getByTestId("embed-map-viewer-root")).toHaveAttribute("data-legend", "off");
    expect(screen.getByTestId("embed-map-viewer-root")).toHaveAttribute("data-zoom", "off");
    expect(screen.getByTestId("annotation-panel")).toBeInTheDocument();
    expect(initMapViewerMock).toHaveBeenCalledWith(
      expect.any(HTMLElement),
      expect.objectContaining({
        savedMapId: "map-style-demo",
        mode: "embed",
        embedParams: {
          chrome: "none",
          legend: false,
          zoom: false,
          extent: { west: -157.9, south: 21.2, east: -157.7, north: 21.4 },
        },
      }),
    );
  });
});

describe("Maps workspace", () => {
  beforeEach(() => {
    initMapViewerMock.mockClear();
  });

  it("lists saved maps at /maps without mounting the viewer", () => {
    render(
      <MemoryRouter initialEntries={["/maps"]}>
        <Routes>
          <Route path="/maps" element={<Maps />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(screen.getByTestId("maps-workspace")).toBeInTheDocument();
    expect(screen.getByText("Honolulu style demo")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open map" })).toHaveAttribute(
      "href",
      `/maps/${STYLE_EDITOR_DEMO_CONTENT_ITEM_ID}`,
    );
    // The app-builder "Build app proof" affordance belongs to honua-console#5.
    expect(initMapViewerMock).not.toHaveBeenCalled();
  });

  it("opens catalog items through the /maps/new source route", async () => {
    render(
      <MemoryRouter initialEntries={["/maps/new?from=01HXY3ZK7N1J2Q9V8M0FQ2PWAB"]}>
        <Routes>
          <Route path="/maps/:mapId" element={<Maps />} />
        </Routes>
      </MemoryRouter>,
    );

    await waitFor(() => expect(initMapViewerMock).toHaveBeenCalled());
    expect(screen.getByTestId("annotation-panel")).toBeInTheDocument();
    expect(initMapViewerMock).toHaveBeenCalledWith(
      expect.any(HTMLElement),
      expect.objectContaining({
        itemId: "01HXY3ZK7N1J2Q9V8M0FQ2PWAB",
        savedMapId: undefined,
        mode: "viewer",
      }),
    );
  });

  it("opens catalog map items through the /maps/new source route", async () => {
    render(
      <MemoryRouter initialEntries={["/maps/new?from=01HXY3ZK7N1J2Q9V8M0FQ2PWAD"]}>
        <Routes>
          <Route path="/maps/:mapId" element={<Maps />} />
        </Routes>
      </MemoryRouter>,
    );

    await waitFor(() => expect(initMapViewerMock).toHaveBeenCalled());
    expect(initMapViewerMock).toHaveBeenCalledWith(
      expect.any(HTMLElement),
      expect.objectContaining({
        itemId: "01HXY3ZK7N1J2Q9V8M0FQ2PWAD",
        savedMapId: undefined,
        mode: "viewer",
      }),
    );
  });
});
