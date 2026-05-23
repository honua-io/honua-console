import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it } from "vitest";

import { App } from "./App";

function testRouter(initialEntries: string[]): React.ComponentType<{ children: ReactNode }> {
  return function TestRouter({ children }: { children: ReactNode }): JSX.Element {
    return <MemoryRouter initialEntries={initialEntries}>{children}</MemoryRouter>;
  };
}

describe("fixture sign-in", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
  });

  afterEach(() => {
    cleanup();
    window.sessionStorage.clear();
  });

  it("lets a local user choose a fixture preset and returns to the protected route", async () => {
    const user = userEvent.setup();

    render(<App Router={testRouter(["/studio"])} />);

    expect(await screen.findByRole("heading", { name: "Sign in to Honua Console" })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Continue as builder" }));

    expect(await screen.findByRole("heading", { name: "Studio" })).toBeInTheDocument();
    expect(screen.getByTestId("usermenu-trigger")).toHaveTextContent("Mira Chen");
  });
});
