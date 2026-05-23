import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";

import { App } from "../src/App";
import { AREA_DESCRIPTORS, CONSOLE_AREAS } from "../src/areas";

function MemoryRouterWith(initialEntries: string[]) {
  return function Wrapper({ basename, children }: { basename?: string; children: React.ReactNode }) {
    return (
      <MemoryRouter basename={basename} initialEntries={initialEntries}>
        {children}
      </MemoryRouter>
    );
  };
}

describe("Console area routes", () => {
  for (const id of CONSOLE_AREAS) {
    const area = AREA_DESCRIPTORS[id];
    it(`renders ${area.path} from the same origin`, () => {
      render(<App Router={MemoryRouterWith([area.path])} />);
      const heading = screen.getByRole("heading", { level: 1, name: area.label });
      expect(heading).toBeInTheDocument();
    });
  }

  it("home links to each area", () => {
    render(<App Router={MemoryRouterWith(["/"])} />);
    for (const id of CONSOLE_AREAS) {
      const area = AREA_DESCRIPTORS[id];
      const link = screen.getByRole("link", { name: area.label });
      expect(link).toHaveAttribute("href", area.path);
    }
  });

  it("falls back to NotFound for unknown routes", () => {
    render(<App Router={MemoryRouterWith(["/no-such-route"])} />);
    expect(screen.getByRole("heading", { level: 1, name: "Not found" })).toBeInTheDocument();
  });

  it("routes area paths under a subpath basename (HONUA_CONSOLE_BASE_PATH)", () => {
    // Mirrors the production wiring documented in BUILD_ARTIFACT.md: Vite emits
    // assets under HONUA_CONSOLE_BASE_PATH and React Router strips that
    // basename before matching the /studio route.
    render(<App basename="/console" Router={MemoryRouterWith(["/console/studio"])} />);
    expect(screen.getByRole("heading", { level: 1, name: "Studio" })).toBeInTheDocument();
  });
});
