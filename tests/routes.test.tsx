import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";

import { App } from "../src/App";
import { AREA_DESCRIPTORS, CONSOLE_AREAS } from "../src/areas";

function MemoryRouterWith(initialEntries: string[]) {
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return <MemoryRouter initialEntries={initialEntries}>{children}</MemoryRouter>;
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
});
