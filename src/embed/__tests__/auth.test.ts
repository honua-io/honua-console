import { describe, expect, it } from "vitest";
import type { EmbedTokenDescriptor } from "../../share/types.js";
import { prepareEmbedAuth, resolveEmbedAuth } from "../auth.js";

describe("resolveEmbedAuth", () => {
  it("returns anonymous when there is no fragment and no session", () => {
    expect(resolveEmbedAuth({ fragment: null, hasSession: false })).toEqual({
      kind: "anonymous",
    });
  });

  it("returns session when a session is present and no token", () => {
    expect(resolveEmbedAuth({ fragment: null, hasSession: true })).toEqual({
      kind: "session",
    });
  });

  it("token in fragment takes precedence over session", () => {
    expect(resolveEmbedAuth({ fragment: "#embedToken=abc", hasSession: true })).toEqual({
      kind: "token",
      token: "abc",
    });
  });
});

const descriptor: EmbedTokenDescriptor = {
  token: "abc",
  itemId: "map-1",
  audience: "pilot",
  expiresAt: "2026-05-13T00:00:00Z",
  closure: ["layer-a"],
};

describe("prepareEmbedAuth", () => {
  it("anonymous embed needs no token redemption", async () => {
    const state = await prepareEmbedAuth({
      fragment: null,
      hasSession: false,
    });
    expect(state).toEqual({
      posture: { kind: "anonymous" },
      descriptor: null,
      fallback: null,
    });
  });

  it("redeems a valid token", async () => {
    const state = await prepareEmbedAuth({
      fragment: "#embedToken=abc",
      hasSession: false,
      redeemEmbedToken: async () => ({ kind: "ok", descriptor }),
    });
    expect(state.posture).toEqual({ kind: "token", token: "abc" });
    expect(state.descriptor).toEqual(descriptor);
    expect(state.fallback).toBeNull();
  });

  it("expired token surfaces as `unauthorized`", async () => {
    const state = await prepareEmbedAuth({
      fragment: "#embedToken=abc",
      hasSession: false,
      redeemEmbedToken: async () => ({ kind: "expired" }),
    });
    expect(state.fallback).toBe("unauthorized");
    expect(state.descriptor).toBeNull();
  });

  it("invalid token surfaces as `unauthorized`", async () => {
    const state = await prepareEmbedAuth({
      fragment: "#embedToken=abc",
      hasSession: false,
      redeemEmbedToken: async () => ({ kind: "invalid" }),
    });
    expect(state.fallback).toBe("unauthorized");
  });

  it("network error surfaces as `error`", async () => {
    const state = await prepareEmbedAuth({
      fragment: "#embedToken=abc",
      hasSession: false,
      redeemEmbedToken: async () => ({ kind: "error", message: "boom" }),
    });
    expect(state.fallback).toBe("error");
  });

  it("token without an injected redemption client is `unsupported`", async () => {
    const state = await prepareEmbedAuth({
      fragment: "#embedToken=abc",
      hasSession: false,
    });
    expect(state.fallback).toBe("unsupported");
  });
});
