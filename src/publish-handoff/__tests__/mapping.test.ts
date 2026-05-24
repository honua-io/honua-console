import { describe, expect, it } from "vitest";
import { applyHandoffEventToItem, buildServiceContentItem } from "../mapping.js";
import type { AdminServiceStatus } from "../types.js";
import { makePublishEvent } from "./fixtures.js";

const NOW = "2026-05-06T12:00:00.000Z";
const LATER = "2026-05-06T12:34:56.000Z";
const SERVICE_ITEM_ID = "01HXY3ZK7N1J2Q9V8M0FQ2PWS1";
const DEPENDENCY_LAYER_ID = "01HXY3ZK7N1J2Q9V8M0FQ2PWAC";

describe("buildServiceContentItem (initial publish)", () => {
  it("projects an admin publish event onto a portal service item with default access", () => {
    const event = makePublishEvent({
      title: "Census Tracts 2026",
      summary: "Tract polygons.",
      tags: ["census", "polygons"],
      status: "ok",
    });
    const item = buildServiceContentItem(event, { id: SERVICE_ITEM_ID, now: NOW });

    expect(item.id).toBe(SERVICE_ITEM_ID);
    expect(item.type).toBe("service");
    expect(item.title).toBe("Census Tracts 2026");
    expect(item.summary).toBe("Tract polygons.");
    expect(item.tags).toEqual(["census", "polygons"]);
    expect(item.endpoints.self.accessURL).toBe(`https://console.honua.example/catalog/${SERVICE_ITEM_ID}`);

    // Default access is private and not embeddable / not open-data —
    // promotion is owned by the share/embed/open-data flows in #15/#17.
    expect(item.access).toEqual({
      sharing: "private",
      embeddable: false,
      openData: false,
    });

    // source.kind is publish; sourceId is the admin's stable service id.
    expect(item.source.kind).toBe("publish");
    expect(item.source.sourceId).toBe(event.sourceServiceId);
    expect(item.source.history).toEqual([{ at: NOW, kind: "publish", actor: event.actor }]);

    // status mapped through to user-safe vocabulary
    expect(item.target.status).toBe("available");
    expect(item.target.statusDetail).toBeNull();
    expect(item.target.adminDiagnosticsRef).toBe("diag-svc-source-1");
  });

  it("never publishes an operator-only adminDiagnosticsRef on the surface", () => {
    // The ref is stored but it is the consumer's job (status.ts) to gate
    // the rendered URL behind admin permissions. The mapping does not
    // expose any operator-only field other than `adminDiagnosticsRef`,
    // and it MUST be absent from any user-facing read path.
    const event = makePublishEvent({ adminDiagnosticsRef: "diag-9" });
    const item = buildServiceContentItem(event, { id: SERVICE_ITEM_ID, now: NOW });
    expect(item.target.adminDiagnosticsRef).toBe("diag-9");
    // No raw operator URL stored — only the opaque ref.
    expect(JSON.stringify(item)).not.toMatch(/admin\.honua\.io/);
  });

  it("sanitizes operator-only status reasons before storing", () => {
    const event = makePublishEvent({
      status: "failed",
      statusReason: "HTTP 503 from origin server",
    });
    const item = buildServiceContentItem(event, { id: SERVICE_ITEM_ID, now: NOW });
    expect(item.target.status).toBe("unavailable");
    expect(item.target.statusDetail).toBeNull();
  });

  it("preserves a free-text reason when status is non-OK", () => {
    const event = makePublishEvent({
      status: "degraded",
      statusReason: "Slower than usual response times.",
    });
    const item = buildServiceContentItem(event, { id: SERVICE_ITEM_ID, now: NOW });
    expect(item.target.status).toBe("limited");
    expect(item.target.statusDetail).toBe("Slower than usual response times.");
  });

  it("clears statusDetail when status is available, even if a reason was sent", () => {
    const event = makePublishEvent({
      status: "ok",
      statusReason: "previously degraded",
    });
    const item = buildServiceContentItem(event, { id: SERVICE_ITEM_ID, now: NOW });
    expect(item.target.statusDetail).toBeNull();
  });

  it("treats unknown operator status as 'unavailable'", () => {
    const event = makePublishEvent({
      status: "totally-new-status" as unknown as AdminServiceStatus,
    });
    const item = buildServiceContentItem(event, { id: SERVICE_ITEM_ID, now: NOW });
    expect(item.target.status).toBe("unavailable");
  });

  it("validates title and rejects empty/oversize", () => {
    expect(() =>
      buildServiceContentItem(makePublishEvent({ title: "" }), {
        id: SERVICE_ITEM_ID,
        now: NOW,
      }),
    ).toThrow(/title is required/);
    expect(() =>
      buildServiceContentItem(makePublishEvent({ title: "x".repeat(281) }), {
        id: SERVICE_ITEM_ID,
        now: NOW,
      }),
    ).toThrow(/title exceeds 280/);
    expect(() =>
      buildServiceContentItem(makePublishEvent({ title: "x".repeat(280) }), {
        id: SERVICE_ITEM_ID,
        now: NOW,
      }),
    ).not.toThrow();
  });

  it("collapses summary whitespace to a contract-safe default and de-duplicates tags", () => {
    const item = buildServiceContentItem(
      makePublishEvent({
        summary: "   ",
        tags: ["a", "a", "b", "", "c"],
      }),
      { id: SERVICE_ITEM_ID, now: NOW },
    );
    expect(item.summary).toBe("No summary provided.");
    expect(item.tags).toEqual(["a", "b", "c"]);
  });

  it("rejects non-http service URLs before emitting a catalog item", () => {
    for (const serviceUrl of ["javascript:alert(1)", "/relative/service"]) {
      expect(() =>
        buildServiceContentItem(makePublishEvent({ serviceUrl }), {
          id: SERVICE_ITEM_ID,
          now: NOW,
        }),
      ).toThrow(/serviceUrl must be an absolute http\(s\) URL/);
    }
  });

  it("stamps lastCheckedAt only for explicit statusChange events or operator-supplied values", () => {
    const publish = buildServiceContentItem(makePublishEvent({ eventKind: "publish" }), {
      id: SERVICE_ITEM_ID,
      now: NOW,
    });
    expect(publish.target.lastCheckedAt).toBeNull();

    const statusChange = buildServiceContentItem(makePublishEvent({ eventKind: "statusChange" }), {
      id: SERVICE_ITEM_ID,
      now: NOW,
    });
    expect(statusChange.target.lastCheckedAt).toBe(NOW);

    const explicit = buildServiceContentItem(
      makePublishEvent({
        eventKind: "publish",
        lastCheckedAt: "2026-05-06T11:00:00.000Z",
      }),
      { id: SERVICE_ITEM_ID, now: NOW },
    );
    expect(explicit.target.lastCheckedAt).toBe("2026-05-06T11:00:00.000Z");
  });
});

describe("applyHandoffEventToItem (re-publish / metadata update)", () => {
  it("preserves id, created timestamp, endpoint, access, preview, and dependencies", () => {
    const initial = buildServiceContentItem(makePublishEvent({ title: "Original" }), { id: SERVICE_ITEM_ID, now: NOW });
    // Force-add a saved-map-like dependency + preview to assert preservation.
    const seed = {
      ...initial,
      dependencies: [{ id: DEPENDENCY_LAYER_ID, type: "layer" as const, role: "operationalLayer" as const }],
      preview: { thumbnail: "/cached.png", image: null },
      access: { sharing: "org" as const, embeddable: true, openData: false },
    };
    const event = makePublishEvent({
      eventKind: "metadataUpdate",
      title: "Renamed",
      summary: "Updated summary.",
      tags: ["new"],
    });
    const next = applyHandoffEventToItem(seed, event, { now: LATER });

    // AC2 invariant: id is preserved across updates so saved-map references
    // that point at this portal item id continue to resolve.
    expect(next.id).toBe(initial.id);
    expect(next.endpoints.self).toBe(initial.endpoints.self);
    expect(next.timestamps.created).toBe(NOW);
    expect(next.timestamps.modified).toBe(LATER);

    // Metadata applied
    expect(next.title).toBe("Renamed");
    expect(next.summary).toBe("Updated summary.");
    expect(next.tags).toEqual(["new"]);

    // Preserved by the upsert (handoff is not allowed to silently change them)
    expect(next.access).toEqual({
      sharing: "org",
      embeddable: true,
      openData: false,
    });
    expect(next.preview).toEqual({ thumbnail: "/cached.png", image: null });
    expect(next.dependencies).toEqual([{ id: DEPENDENCY_LAYER_ID, type: "layer", role: "operationalLayer" }]);
  });

  it("appends to source.history rather than replacing it", () => {
    const initial = buildServiceContentItem(makePublishEvent({ actor: "u1" }), { id: SERVICE_ITEM_ID, now: NOW });
    const next = applyHandoffEventToItem(
      initial,
      makePublishEvent({
        eventKind: "republish",
        actor: "u2",
      }),
      { now: LATER },
    );
    expect(next.source.history).toEqual([
      { at: NOW, kind: "publish", actor: "u1" },
      { at: LATER, kind: "publish", actor: "u2" },
    ]);
  });

  it("re-projects status (operator status can change between publishes)", () => {
    const initial = buildServiceContentItem(makePublishEvent({ status: "ok" }), { id: SERVICE_ITEM_ID, now: NOW });
    expect(initial.target.status).toBe("available");

    const next = applyHandoffEventToItem(
      initial,
      makePublishEvent({
        eventKind: "statusChange",
        status: "failed",
        statusReason: "Temporary upstream outage.",
      }),
      { now: LATER },
    );
    expect(next.target.status).toBe("unavailable");
    expect(next.target.statusDetail).toBe("Temporary upstream outage.");
    expect(next.target.lastCheckedAt).toBe(LATER);
  });

  it("rejects non-http service URLs on re-publish without producing a partial item", () => {
    const initial = buildServiceContentItem(makePublishEvent({ status: "ok" }), { id: SERVICE_ITEM_ID, now: NOW });

    expect(() =>
      applyHandoffEventToItem(
        initial,
        makePublishEvent({
          eventKind: "republish",
          serviceUrl: "javascript:alert(1)",
        }),
        { now: LATER },
      ),
    ).toThrow(/serviceUrl must be an absolute http\(s\) URL/);

    expect(initial.target.serviceUrl).toBe("https://maps.honua.io/services/census/v1");
    expect(initial.source.history).toHaveLength(1);
  });

  it("preserves adminDiagnosticsRef when admin omits it on a metadata update", () => {
    const initial = buildServiceContentItem(makePublishEvent({ adminDiagnosticsRef: "diag-original" }), {
      id: SERVICE_ITEM_ID,
      now: NOW,
    });
    const event = makePublishEvent({
      eventKind: "metadataUpdate",
      title: "Renamed",
    });
    // Drop the field entirely on the event to simulate admin not resending it.
    event.adminDiagnosticsRef = undefined;
    const next = applyHandoffEventToItem(initial, event, { now: LATER });
    expect(next.target.adminDiagnosticsRef).toBe("diag-original");
  });

  it("respects an explicit null adminDiagnosticsRef (admin clearing the ref)", () => {
    const initial = buildServiceContentItem(makePublishEvent({ adminDiagnosticsRef: "diag-original" }), {
      id: SERVICE_ITEM_ID,
      now: NOW,
    });
    const event = makePublishEvent({
      eventKind: "republish",
      adminDiagnosticsRef: null,
    });
    const next = applyHandoffEventToItem(initial, event, { now: LATER });
    expect(next.target.adminDiagnosticsRef).toBeNull();
  });
});
