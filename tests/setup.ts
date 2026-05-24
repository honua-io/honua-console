import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

afterEach(() => {
  cleanup();
  try {
    window.sessionStorage.clear();
  } catch {
    // jsdom storage may be unavailable in some Node versions; tests don't depend on it being clean.
  }
});
