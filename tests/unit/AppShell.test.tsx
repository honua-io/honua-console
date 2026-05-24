import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { SessionProvider } from "../../src/auth/SessionContext";
import { AppRoutes } from "../../src/router";
import { memberSession, operatorSession, staticDriver, unauthenticatedSession } from "../fixtures";

import "../../src/styles/global.css";

function renderWith(driver: ReturnType<typeof staticDriver>, initialEntries: string[]): void {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <SessionProvider driver={driver}>
        <MemoryRouter initialEntries={initialEntries}>
          <AppRoutes />
        </MemoryRouter>
      </SessionProvider>
    </QueryClientProvider>,
  );
}

afterEach(() => {
  vi.unstubAllEnvs();
});

describe("App shell", () => {
  it("renders the workspace home with the canonical nav for an authenticated member (AC1)", async () => {
    renderWith(staticDriver(memberSession), ["/"]);

    await waitFor(() => {
      expect(screen.getByText(/Welcome back, Mira/i)).toBeInTheDocument();
    });

    for (const id of ["home", "catalog", "maps", "data", "groups", "public"]) {
      expect(screen.getByTestId(`nav-${id}`)).toBeInTheDocument();
    }
    expect(screen.getByTestId("usermenu-trigger")).toHaveTextContent("Mira Chen");
  });

  it("hides operator controls from non-operators (AC2)", async () => {
    vi.stubEnv("VITE_ADMIN_BASE_URL", "https://admin.example");
    renderWith(staticDriver(memberSession), ["/"]);
    await waitFor(() => {
      expect(screen.getByText(/Welcome back/i)).toBeInTheDocument();
    });
    await userEvent.click(screen.getByTestId("usermenu-trigger"));
    expect(screen.queryByTestId("usermenu-admin-link")).not.toBeInTheDocument();
  });

  it("exposes the admin link-back to operators when configured (AC3)", async () => {
    vi.stubEnv("VITE_ADMIN_BASE_URL", "https://admin.example");
    renderWith(staticDriver(operatorSession), ["/"]);
    await waitFor(() => {
      expect(screen.getByText(/Welcome back/i)).toBeInTheDocument();
    });
    await userEvent.click(screen.getByTestId("usermenu-trigger"));
    const link = await screen.findByTestId("usermenu-admin-link");
    expect(link).toHaveAttribute("href", "https://admin.example");
  });

  it("omits the operator group entirely when no admin URL is configured", async () => {
    vi.stubEnv("VITE_ADMIN_BASE_URL", "");
    renderWith(staticDriver(operatorSession), ["/"]);
    await waitFor(() => {
      expect(screen.getByText(/Welcome back/i)).toBeInTheDocument();
    });
    await userEvent.click(screen.getByTestId("usermenu-trigger"));
    expect(screen.queryByTestId("usermenu-admin-link")).not.toBeInTheDocument();
    expect(screen.queryByText(/^Operator$/)).not.toBeInTheDocument();
  });

  it("redirects unauthenticated visitors away from a protected route", async () => {
    renderWith(staticDriver(unauthenticatedSession), ["/maps"]);
    await waitFor(() => {
      expect(screen.getByText(/Sign in to Honua Console/i)).toBeInTheDocument();
    });
  });

  it("renders the public route without a session", async () => {
    renderWith(staticDriver(unauthenticatedSession), ["/public"]);
    await waitFor(() => {
      expect(screen.getByText(/Open datasets, services, layers/i)).toBeInTheDocument();
    });
    expect(screen.getByRole("link", { name: "City Parcels 2026" })).toHaveAttribute(
      "href",
      "/public/items/city-parcels-2026",
    );
    expect(screen.getByTestId("signin-trigger")).toBeInTheDocument();
  });

  it("renders a public open-data item detail without a session", async () => {
    renderWith(staticDriver(unauthenticatedSession), ["/public/items/city-parcels-2026"]);
    await waitFor(() => {
      expect(screen.getByRole("heading", { name: "City Parcels 2026" })).toBeInTheDocument();
    });
    expect(screen.getByRole("heading", { name: "Downloads and APIs" })).toBeInTheDocument();
    expect(screen.getByTestId("signin-trigger")).toBeInTheDocument();
  });
});
