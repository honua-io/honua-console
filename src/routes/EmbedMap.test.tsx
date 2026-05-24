import { render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { staticDriver, unauthenticatedSession } from "../../tests/fixtures";
import { SessionProvider } from "../auth/SessionContext";
import { defaultEmbedToken } from "../share/tokens";
import EmbedMap from "./EmbedMap";

const initMapViewerMock = vi.hoisted(() =>
  vi.fn(() => ({
    ready: Promise.resolve(),
    dispose: vi.fn(),
  })),
);

vi.mock("../viewer/init", () => ({
  initMapViewer: initMapViewerMock,
}));

function renderEmbed(path: string): void {
  render(
    <SessionProvider driver={staticDriver(unauthenticatedSession)}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/embed/maps/:mapId" element={<EmbedMap />} />
        </Routes>
      </MemoryRouter>
    </SessionProvider>,
  );
}

describe("EmbedMap route authorization", () => {
  beforeEach(() => {
    initMapViewerMock.mockClear();
  });

  it("loads the public-link saved-map fixture without requiring a token", async () => {
    renderEmbed("/embed/maps/map-style-demo");

    await waitFor(() => expect(initMapViewerMock).toHaveBeenCalled());
    expect(screen.getByTestId("embed-map-viewer-root")).toBeInTheDocument();
    expect(initMapViewerMock).toHaveBeenCalledWith(
      expect.any(HTMLElement),
      expect.objectContaining({
        savedMapId: "map-style-demo",
        itemId: undefined,
        mode: "embed",
      }),
    );
  });

  it("refuses an expired token before mounting the viewer", async () => {
    renderEmbed("/embed/maps/map-style-demo#embedToken=fixture-expired");

    expect(await screen.findByRole("heading", { name: "Embed token expired or invalid" })).toBeInTheDocument();
    expect(initMapViewerMock).not.toHaveBeenCalled();
  });

  it("blocks maps that are not embeddable before mounting the viewer", async () => {
    renderEmbed("/embed/maps/01HXY3ZK7N1J2Q9V8M0FQ2PWAK");

    expect(await screen.findByRole("heading", { name: "Embeds are disabled for this map" })).toBeInTheDocument();
    expect(initMapViewerMock).not.toHaveBeenCalled();
  });

  it("loads catalog map ids as source items after a valid token", async () => {
    const mapId = "01HXY3ZK7N1J2Q9V8M0FQ2PWAD";
    renderEmbed(`/embed/maps/${mapId}#embedToken=${defaultEmbedToken(mapId)}`);

    await waitFor(() => expect(initMapViewerMock).toHaveBeenCalled());
    expect(initMapViewerMock).toHaveBeenCalledWith(
      expect.any(HTMLElement),
      expect.objectContaining({
        savedMapId: undefined,
        itemId: mapId,
        mode: "embed",
      }),
    );
  });
});
