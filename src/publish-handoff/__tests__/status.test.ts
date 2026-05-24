import { describe, expect, it } from "vitest";
import { buildServiceContentItem } from "../mapping.js";
import { adminDiagnosticLink, mapAdminStatus, sanitizeStatusReason, statusBadge } from "../status.js";
import { makePublishEvent } from "./fixtures.js";

const SERVICE_ITEM_ID = "01HXY3ZK7N1J2Q9V8M0FQ2PWS1";

describe("mapAdminStatus", () => {
  it("collapses healthy operator states to 'available'", () => {
    expect(mapAdminStatus("ok")).toBe("available");
    expect(mapAdminStatus("ready")).toBe("available");
    expect(mapAdminStatus("running")).toBe("available");
  });

  it("collapses warming/partial/throttled states to 'limited'", () => {
    expect(mapAdminStatus("publishing")).toBe("limited");
    expect(mapAdminStatus("warming")).toBe("limited");
    expect(mapAdminStatus("partial")).toBe("limited");
    expect(mapAdminStatus("degraded")).toBe("limited");
    expect(mapAdminStatus("throttled")).toBe("limited");
  });

  it("collapses failed/errored/broken to 'unavailable'", () => {
    expect(mapAdminStatus("failed")).toBe("unavailable");
    expect(mapAdminStatus("errored")).toBe("unavailable");
    expect(mapAdminStatus("broken")).toBe("unavailable");
  });

  it("collapses draft/unpublished to 'draft'", () => {
    expect(mapAdminStatus("draft")).toBe("draft");
    expect(mapAdminStatus("unpublished")).toBe("draft");
  });

  it("defaults unknown operator strings to 'unavailable' (never 'available')", () => {
    // Defaulting to 'available' on an unknown value would let an admin-side
    // rename silently mislabel a broken service as healthy in portal.
    expect(mapAdminStatus("totally-new-status")).toBe("unavailable");
    expect(mapAdminStatus("")).toBe("unavailable");
  });

  it("maps the canonical honua-server PublishedServiceStatus enum correctly", () => {
    // honua-server's canonical PublishedServiceStatus enum:
    //   Provisioning | Active | Suspended | RefreshFailed | Decommissioned.
    // The admin upstream serializes these PascalCase by default
    // (System.Text.Json's JsonStringEnumConverter, no naming policy).
    // Without these explicit mappings, "Active" would default to
    // 'unavailable' and a healthy published service would render as
    // broken — see the codex_local finding on status.ts.
    expect(mapAdminStatus("Active")).toBe("available");
    expect(mapAdminStatus("Provisioning")).toBe("limited");
    expect(mapAdminStatus("RefreshFailed")).toBe("limited");
    expect(mapAdminStatus("Suspended")).toBe("unavailable");
    expect(mapAdminStatus("Decommissioned")).toBe("unavailable");
  });

  it("matches admin status case-insensitively", () => {
    // Admin adapters may normalize to lowercase before sending; both
    // shapes must project to the same portal status.
    expect(mapAdminStatus("active")).toBe("available");
    expect(mapAdminStatus("ACTIVE")).toBe("available");
    expect(mapAdminStatus("provisioning")).toBe("limited");
    expect(mapAdminStatus("OK")).toBe("available");
    expect(mapAdminStatus("Failed")).toBe("unavailable");
    expect(mapAdminStatus("DRAFT")).toBe("draft");
  });
});

describe("statusBadge", () => {
  it("returns a stable, end-user-safe label per status", () => {
    expect(statusBadge("available").label).toBe("Available");
    expect(statusBadge("limited").label).toBe("Limited availability");
    expect(statusBadge("unavailable").label).toBe("Currently unavailable");
    expect(statusBadge("draft").label).toBe("Draft");
  });

  it("never leaks operator vocabulary in the description", () => {
    for (const status of ["available", "limited", "unavailable", "draft"] as const) {
      const badge = statusBadge(status);
      expect(badge.description.toLowerCase()).not.toMatch(/(stack|trace|exception|http\s*\d{3}|job[-_]?id|deploy)/);
    }
  });
});

describe("sanitizeStatusReason", () => {
  it("returns null for null/empty/whitespace", () => {
    expect(sanitizeStatusReason(null)).toBeNull();
    expect(sanitizeStatusReason("")).toBeNull();
    expect(sanitizeStatusReason("   ")).toBeNull();
    expect(sanitizeStatusReason(undefined)).toBeNull();
  });

  it("drops operator-only tokens (URLs, file paths, GUIDs, http codes, stack frames)", () => {
    expect(sanitizeStatusReason("Error fetching https://internal.honua.io/admin/jobs/123")).toBeNull();
    expect(sanitizeStatusReason("/var/log/honua/server.log: connection reset")).toBeNull();
    expect(sanitizeStatusReason("HTTP 503 from origin server")).toBeNull();
    expect(sanitizeStatusReason("job-id: 12345 failed")).toBeNull();
    expect(sanitizeStatusReason("at Honua.Server.Publish.PublishService.Run (/src/Publish.cs:42:11)")).toBeNull();
    expect(sanitizeStatusReason("Failed: NullReferenceException in pipeline")).toBeNull();
    expect(sanitizeStatusReason("StackTrace: ...")).toBeNull();
    expect(sanitizeStatusReason("uuid 12345678-1234-1234-1234-123456789012 failed")).toBeNull();
  });

  it("trims and caps free-text reasons at 160 chars", () => {
    expect(sanitizeStatusReason("  Slow response from upstream.  ")).toBe("Slow response from upstream.");
    const long = "A".repeat(200);
    const out = sanitizeStatusReason(long);
    expect(out).not.toBeNull();
    expect((out as string).length).toBe(160);
    expect((out as string).endsWith("…")).toBe(true);
  });
});

describe("adminDiagnosticLink", () => {
  it("returns null when actor lacks admin:diagnostics permission", () => {
    const item = buildServiceContentItem(makePublishEvent({ adminDiagnosticsRef: "diag-1" }), {
      id: SERVICE_ITEM_ID,
      now: "2026-05-06T12:00:00.000Z",
    });
    expect(
      adminDiagnosticLink(item, {
        adminBaseUrl: "https://admin.honua.io",
        permissions: ["catalog:read"],
      }),
    ).toBeNull();
  });

  it("returns null when item has no adminDiagnosticsRef", () => {
    const item = buildServiceContentItem(makePublishEvent({ adminDiagnosticsRef: null }), {
      id: SERVICE_ITEM_ID,
      now: "2026-05-06T12:00:00.000Z",
    });
    expect(
      adminDiagnosticLink(item, {
        adminBaseUrl: "https://admin.honua.io",
        permissions: ["admin:diagnostics"],
      }),
    ).toBeNull();
  });

  it("composes admin URL only when permission AND ref are present", () => {
    const item = buildServiceContentItem(makePublishEvent({ adminDiagnosticsRef: "diag-9" }), {
      id: SERVICE_ITEM_ID,
      now: "2026-05-06T12:00:00.000Z",
    });
    expect(
      adminDiagnosticLink(item, {
        adminBaseUrl: "https://admin.honua.io",
        permissions: ["admin:diagnostics"],
      }),
    ).toBe("https://admin.honua.io/services/diag-9/diagnostics");
  });

  it("strips trailing slashes on the admin base URL and url-encodes the ref", () => {
    const item = buildServiceContentItem(makePublishEvent({ adminDiagnosticsRef: "diag/with spaces" }), {
      id: SERVICE_ITEM_ID,
      now: "2026-05-06T12:00:00.000Z",
    });
    expect(
      adminDiagnosticLink(item, {
        adminBaseUrl: "https://admin.honua.io///",
        permissions: new Set(["admin:diagnostics"]),
      }),
    ).toBe("https://admin.honua.io/services/diag%2Fwith%20spaces/diagnostics");
  });
});
