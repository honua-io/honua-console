import { describe, expect, it } from "vitest";

import {
  isOk,
  missing,
  ok,
  pendingBinding,
  unauthorized,
  unsupported,
} from "./LoadSurface";

describe("LoadSurface helpers", () => {
  it("ok carries the value", () => {
    const s = ok({ id: "abc" });
    expect(s.status).toBe("ok");
    expect(isOk(s)).toBe(true);
    if (s.status === "ok") expect(s.value).toEqual({ id: "abc" });
  });

  it("missing/unauthorized produce typed sentinels", () => {
    expect(missing<string>().status).toBe("missing");
    expect(unauthorized<string>().status).toBe("unauthorized");
  });

  it("unsupported preserves reason and optional code", () => {
    const s = unsupported<string>("boom", "E_BIND");
    expect(s.status).toBe("unsupported");
    if (s.status === "unsupported") {
      expect(s.reason).toBe("boom");
      expect(s.code).toBe("E_BIND");
    }
  });

  it("pending-binding lists the contracts still in flight", () => {
    const s = pendingBinding<string>(["honua-sdk-js#225"]);
    expect(s.status).toBe("pending-binding");
    if (s.status === "pending-binding") {
      expect(s.waitingFor).toEqual(["honua-sdk-js#225"]);
    }
  });
});
