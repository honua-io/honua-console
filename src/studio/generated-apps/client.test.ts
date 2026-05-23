import { describe, expect, it } from "vitest";

import { lifecycleCodeFromStatus } from "./client.js";

describe("lifecycleCodeFromStatus", () => {
  // Auth failures: a 401 (expired/missing session) must surface as
  // `unauthorized` so the GeneratedAppPreviewPage renders the Forbidden
  // surface — same posture as the session probe in `whoamiDriver`, which
  // collapses 401 and 403 to "unauthenticated".
  it("maps 401 and 403 to unauthorized", () => {
    expect(lifecycleCodeFromStatus(401)).toBe("unauthorized");
    expect(lifecycleCodeFromStatus(403)).toBe("unauthorized");
  });

  it("maps 404 to missing", () => {
    expect(lifecycleCodeFromStatus(404)).toBe("missing");
  });

  it("maps 409 to conflict", () => {
    expect(lifecycleCodeFromStatus(409)).toBe("conflict");
  });

  it("maps 422 to unsupported", () => {
    expect(lifecycleCodeFromStatus(422)).toBe("unsupported");
  });

  it("maps 5xx to server", () => {
    expect(lifecycleCodeFromStatus(500)).toBe("server");
    expect(lifecycleCodeFromStatus(502)).toBe("server");
    expect(lifecycleCodeFromStatus(599)).toBe("server");
  });

  it("falls back to invalid for other 4xx codes", () => {
    expect(lifecycleCodeFromStatus(400)).toBe("invalid");
    expect(lifecycleCodeFromStatus(429)).toBe("invalid");
  });
});
