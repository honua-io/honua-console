import { describe, expect, it } from "vitest";

import { buildAuthRedirectUrl } from "./whoamiDriver";

describe("buildAuthRedirectUrl", () => {
  it("targets a server-owned endpoint instead of the SPA sign-in route", () => {
    expect(buildAuthRedirectUrl("/api/auth/signin", "/studio?tab=maps#draft", "https://console.example")).toBe(
      "https://console.example/api/auth/signin?returnTo=%2Fstudio%3Ftab%3Dmaps%23draft",
    );
  });

  it("sanitizes unsafe return targets before redirecting", () => {
    expect(buildAuthRedirectUrl("/api/auth/signin", "https://evil.example", "https://console.example")).toBe(
      "https://console.example/api/auth/signin?returnTo=%2F",
    );
  });
});
