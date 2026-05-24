import { describe, expect, it } from "vitest";

import { adaptControlPlaneResult, adaptSdkThrown } from "./adapt";

describe("adaptControlPlaneResult", () => {
  it("ok wraps the value", () => {
    const s = adaptControlPlaneResult({ supported: true, value: 42 });
    expect(s).toEqual({ status: "ok", value: 42 });
  });

  it("404 maps to missing", () => {
    const s = adaptControlPlaneResult({
      supported: false,
      capability: "map-packages",
      statusCode: 404,
      reason: "not found",
    });
    expect(s.status).toBe("missing");
  });

  it("501 maps to unsupported with the SDK code", () => {
    const s = adaptControlPlaneResult({
      supported: false,
      capability: "map-packages",
      statusCode: 501,
      reason: "deployment does not expose map-packages",
    });
    expect(s.status).toBe("unsupported");
    if (s.status === "unsupported") {
      expect(s.code).toBe("501");
      expect(s.reason).toContain("map-packages");
    }
  });
});

describe("adaptSdkThrown", () => {
  it("401 maps to unauthorized", () => {
    const error = Object.assign(new Error("nope"), { statusCode: 401 });
    expect(adaptSdkThrown<string>(error).status).toBe("unauthorized");
  });

  it("404 maps to missing", () => {
    const error = Object.assign(new Error("nope"), { statusCode: 404 });
    expect(adaptSdkThrown<string>(error).status).toBe("missing");
  });

  it("unknown errors map to unsupported with message", () => {
    const s = adaptSdkThrown<string>(new Error("boom"));
    expect(s.status).toBe("unsupported");
    if (s.status === "unsupported") expect(s.reason).toBe("boom");
  });
});
