import { describe, expect, it } from "vitest";
import { safeHttpUrl } from "./url.js";

describe("safeHttpUrl", () => {
  it("allows http and https URLs", () => {
    expect(safeHttpUrl("https://portal.honua.example/items/1")).toBe("https://portal.honua.example/items/1");
    expect(safeHttpUrl("http://localhost:4173/items/1")).toBe("http://localhost:4173/items/1");
  });

  it("rejects non-http schemes and malformed values", () => {
    expect(safeHttpUrl("javascript:alert(1)")).toBeNull();
    expect(safeHttpUrl("data:text/html,<script>alert(1)</script>")).toBeNull();
    expect(safeHttpUrl("/relative/path")).toBeNull();
  });
});
