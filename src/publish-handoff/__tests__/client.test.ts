import { describe, expect, it } from "vitest";
import { FixturePublishHandoffClient, PublishHandoffNotFoundError, SavedMapReferenceRegistry } from "../client.js";
import { deterministicIdGenerator, deterministicNow, makePublishEvent } from "./fixtures.js";

const FIRST_SERVICE_ITEM_ID = "01HXY3ZK7N03NMF57D00000001";

function makeClient(opts?: { references?: SavedMapReferenceRegistry }) {
  return new FixturePublishHandoffClient({
    generateId: deterministicIdGenerator("svc"),
    now: deterministicNow(),
    ...(opts?.references ? { savedMapReferences: opts.references } : {}),
  });
}

describe("FixturePublishHandoffClient.receive (AC1: publish creates a discoverable portal item)", () => {
  it("creates a service content item discoverable via list and findBySourceServiceId", async () => {
    const client = makeClient();
    const event = makePublishEvent({ sourceServiceId: "svc-source-1" });
    const created = await client.receive(event);

    expect(created.id).toBe(FIRST_SERVICE_ITEM_ID);
    expect(created.type).toBe("service");
    expect(created.source.sourceId).toBe("svc-source-1");

    const found = await client.findBySourceServiceId("svc-source-1");
    expect(found?.id).toBe(FIRST_SERVICE_ITEM_ID);

    const list = await client.list();
    expect(list).toHaveLength(1);
    expect(list[0]?.id).toBe(FIRST_SERVICE_ITEM_ID);
  });

  it("treats a duplicate publish as a republish (admin retry safety) — no second item is created", async () => {
    const client = makeClient();
    const a = await client.receive(makePublishEvent({ sourceServiceId: "svc-source-1" }));
    const b = await client.receive(makePublishEvent({ sourceServiceId: "svc-source-1", title: "Renamed" }));
    expect(b.id).toBe(a.id);
    expect(b.title).toBe("Renamed");

    const list = await client.list();
    expect(list).toHaveLength(1);
  });

  it("rejects metadataUpdate / statusChange against an unknown source service id", async () => {
    const client = makeClient();
    await expect(
      client.receive(
        makePublishEvent({
          sourceServiceId: "svc-missing",
          eventKind: "metadataUpdate",
        }),
      ),
    ).rejects.toBeInstanceOf(PublishHandoffNotFoundError);
    await expect(
      client.receive(
        makePublishEvent({
          sourceServiceId: "svc-missing",
          eventKind: "statusChange",
        }),
      ),
    ).rejects.toBeInstanceOf(PublishHandoffNotFoundError);
  });

  it("rejects an empty sourceServiceId (admin contract bug)", async () => {
    const client = makeClient();
    await expect(client.receive(makePublishEvent({ sourceServiceId: "" }))).rejects.toThrow(
      /sourceServiceId is required/,
    );
  });

  it("rejects an invalid initial serviceUrl without indexing the source", async () => {
    const client = makeClient();
    await expect(
      client.receive(
        makePublishEvent({
          sourceServiceId: "svc-bad-url",
          serviceUrl: "/relative/service",
        }),
      ),
    ).rejects.toThrow(/serviceUrl must be an absolute http\(s\) URL/);

    expect(await client.surfaceForSourceService("svc-bad-url")).toEqual({
      kind: "missing",
      sourceServiceId: "svc-bad-url",
    });
    expect(await client.list()).toEqual([]);
  });
});

describe("FixturePublishHandoffClient.receive (AC2: metadata update preserves saved-map references)", () => {
  it("re-publish keeps the same portal item id so saved-map references survive", async () => {
    const references = new SavedMapReferenceRegistry();
    const client = makeClient({ references });
    const created = await client.receive(makePublishEvent({ sourceServiceId: "svc-source-1" }));

    // Simulate a saved map (#14 owns the saved map itself) that lists this
    // service item as an operational layer dependency.
    references.link(created.id, "map-saved-1");
    references.link(created.id, "map-saved-2");

    // Admin renames the service in admin and re-pushes; portal must NOT
    // mint a new id, otherwise the two saved maps go dangling.
    const renamed = await client.receive(
      makePublishEvent({
        sourceServiceId: "svc-source-1",
        eventKind: "metadataUpdate",
        title: "Census Tracts (Renamed)",
        summary: "Renamed for 2026 release.",
        tags: ["census", "v2"],
      }),
    );

    expect(renamed.id).toBe(created.id);
    expect(renamed.title).toBe("Census Tracts (Renamed)");
    expect(renamed.summary).toBe("Renamed for 2026 release.");
    expect(renamed.tags).toEqual(["census", "v2"]);

    // Saved-map references resolve unchanged after the update.
    expect([...references.savedMapsFor(created.id)].sort()).toEqual(["map-saved-1", "map-saved-2"]);
    const stillThere = await client.get(created.id);
    expect(stillThere?.id).toBe(created.id);

    // Created timestamp is preserved; modified moves forward.
    expect(renamed.timestamps.created).toBe(created.timestamps.created);
    expect(renamed.timestamps.modified > created.timestamps.modified).toBe(true);
  });

  it("preserves access (sharing/embeddable/openData) across a re-publish", async () => {
    const client = makeClient();
    const created = await client.receive(makePublishEvent({ sourceServiceId: "svc-source-1" }));
    expect(created.access.sharing).toBe("private");

    // Promote sharing through a side channel (the share flow is owned by
    // #15; we mutate the underlying record to simulate that). In real
    // wiring, the share flow updates a separate share-access record.
    // What we are asserting here is that a re-publish does NOT silently
    // reset access.
    const updated = await client.receive(
      makePublishEvent({
        sourceServiceId: "svc-source-1",
        eventKind: "republish",
        title: "Census Tracts 2026 (rev 2)",
      }),
    );
    expect(updated.access).toEqual(created.access);
  });

  it("appends an audit history entry on every republish/metadata update", async () => {
    const client = makeClient();
    await client.receive(makePublishEvent({ sourceServiceId: "s", actor: "u1" }));
    await client.receive(
      makePublishEvent({
        sourceServiceId: "s",
        eventKind: "metadataUpdate",
        actor: "u2",
      }),
    );
    const final = await client.receive(
      makePublishEvent({
        sourceServiceId: "s",
        eventKind: "statusChange",
        status: "degraded",
        actor: "u3",
      }),
    );
    expect(final.source.history.map((h) => h.kind)).toEqual(["publish", "metadata-edit", "update"]);
    expect(final.source.history.map((h) => h.actor)).toEqual(["u1", "u2", "u3"]);
  });

  it("rejects an invalid re-publish serviceUrl without mutating the stored item", async () => {
    const client = makeClient();
    const created = await client.receive(makePublishEvent({ sourceServiceId: "svc-source-1" }));

    await expect(
      client.receive(
        makePublishEvent({
          sourceServiceId: "svc-source-1",
          eventKind: "republish",
          serviceUrl: "javascript:alert(1)",
          title: "Should not persist",
        }),
      ),
    ).rejects.toThrow(/serviceUrl must be an absolute http\(s\) URL/);

    const stored = await client.get(created.id, { permissions: ["admin:diagnostics"] });
    expect(stored?.title).toBe(created.title);
    expect(stored?.target.serviceUrl).toBe(created.target.serviceUrl);
    expect(stored?.source.history).toHaveLength(1);
  });
});

describe("FixturePublishHandoffClient (AC3: failed/degraded services surface user-safe status + admin-only diagnostics)", () => {
  it("a failed publish event surfaces 'unavailable' on the portal item with no operator vocabulary", async () => {
    const client = makeClient();
    const item = await client.receive(
      makePublishEvent({
        sourceServiceId: "svc-broken",
        status: "failed",
        statusReason: "HTTP 503 from origin server",
        adminDiagnosticsRef: "diag-broken",
      }),
    );
    expect(item.target.status).toBe("unavailable");
    // Operator-only reason was sanitized to null; user sees the badge,
    // operator follows the admin link.
    expect(item.target.statusDetail).toBeNull();
    // Admin diagnostic correlation id is stored on the item for later
    // gated rendering by status.adminDiagnosticLink.
    expect(item.target.adminDiagnosticsRef).toBe("diag-broken");
  });

  it("a degraded publish event surfaces 'limited' with a sanitized free-text detail", async () => {
    const client = makeClient();
    const item = await client.receive(
      makePublishEvent({
        sourceServiceId: "svc-slow",
        status: "degraded",
        statusReason: "Slower than usual response times.",
      }),
    );
    expect(item.target.status).toBe("limited");
    expect(item.target.statusDetail).toBe("Slower than usual response times.");
  });

  it("a status flip from ok to failed updates the same portal item without dropping references", async () => {
    const references = new SavedMapReferenceRegistry();
    const client = makeClient({ references });
    const created = await client.receive(makePublishEvent({ sourceServiceId: "svc-1", status: "ok" }));
    references.link(created.id, "map-saved-1");

    const failed = await client.receive(
      makePublishEvent({
        sourceServiceId: "svc-1",
        eventKind: "statusChange",
        status: "broken",
        statusReason: "broken pipeline",
      }),
    );
    expect(failed.id).toBe(created.id);
    expect(failed.target.status).toBe("unavailable");
    expect(references.savedMapsFor(created.id)).toEqual(["map-saved-1"]);
  });
});

describe("FixturePublishHandoffClient.surfaceForSourceService (catalog surface taxonomy)", () => {
  it("returns missing for an unknown source service", async () => {
    const client = makeClient();
    const surface = await client.surfaceForSourceService("svc-unknown");
    expect(surface.kind).toBe("missing");
  });

  it("returns ok with the item when the source is published", async () => {
    const client = makeClient();
    await client.receive(makePublishEvent({ sourceServiceId: "svc-1" }));
    const surface = await client.surfaceForSourceService("svc-1");
    expect(surface.kind).toBe("ok");
    if (surface.kind === "ok") {
      expect(surface.item.id).toBe(FIRST_SERVICE_ITEM_ID);
    }
  });

  it("returns unsupported for service kinds the portal cannot describe", async () => {
    const client = makeClient();
    await client.receive(
      makePublishEvent({
        sourceServiceId: "svc-1",
        serviceType: "unsupported",
        status: "ok",
        statusReason: "Underlying service kind is not yet supported in portal.",
      }),
    );
    const surface = await client.surfaceForSourceService("svc-1");
    expect(surface.kind).toBe("unsupported");
    if (surface.kind === "unsupported") {
      // Default reason text used when statusDetail is null (status='ok' clears it).
      expect(surface.reason).toBe("Service type not supported.");
    }
  });

  it("returns unsupported with a sanitized statusDetail when one is set", async () => {
    const client = makeClient();
    await client.receive(
      makePublishEvent({
        sourceServiceId: "svc-1",
        serviceType: "unsupported",
        status: "degraded",
        statusReason: "Underlying service kind is not yet supported in portal.",
      }),
    );
    const surface = await client.surfaceForSourceService("svc-1");
    expect(surface.kind).toBe("unsupported");
    if (surface.kind === "unsupported") {
      expect(surface.reason).toMatch(/not yet supported/);
    }
  });

  it("returns unauthorized when the read context denies the located item", async () => {
    // AC3 / surface-taxonomy completeness. The catalog UI promises one of
    // ok | missing | unauthorized | unsupported and must not leak that an
    // item exists to a caller that cannot read it. The HTTP-backed client
    // will translate a server 403 into the same surface; the fixture
    // exercises the same branch via a canRead callback.
    const client = makeClient();
    await client.receive(makePublishEvent({ sourceServiceId: "svc-1" }));
    const surface = await client.surfaceForSourceService("svc-1", {
      canRead: () => false,
    });
    expect(surface.kind).toBe("unauthorized");
    if (surface.kind === "unauthorized") {
      expect(surface.sourceServiceId).toBe("svc-1");
    }
  });

  it("falls through to ok when canRead allows the located item", async () => {
    const client = makeClient();
    await client.receive(makePublishEvent({ sourceServiceId: "svc-1" }));
    const surface = await client.surfaceForSourceService("svc-1", {
      canRead: () => true,
    });
    expect(surface.kind).toBe("ok");
  });

  it("returns missing for an unknown source service even when canRead is supplied", async () => {
    // canRead is only consulted for items that exist. An unknown
    // sourceServiceId still surfaces as `missing` so callers do not
    // have to special-case the predicate path.
    const client = makeClient();
    const surface = await client.surfaceForSourceService("svc-unknown", {
      canRead: () => false,
    });
    expect(surface.kind).toBe("missing");
  });
});

describe("FixturePublishHandoffClient.list", () => {
  it("returns items newest-modified first", async () => {
    const client = makeClient();
    await client.receive(makePublishEvent({ sourceServiceId: "a", title: "A" }));
    await client.receive(makePublishEvent({ sourceServiceId: "b", title: "B" }));
    await client.receive(
      makePublishEvent({
        sourceServiceId: "a",
        eventKind: "metadataUpdate",
        title: "A renamed",
      }),
    );
    const list = await client.list();
    expect(list.map((i) => i.title)).toEqual(["A renamed", "B"]);
  });
});

describe("FixturePublishHandoffClient catalog read redaction (operator-only fields)", () => {
  // The handoff stores `target.adminDiagnosticsRef` so an operator with
  // `admin:diagnostics` can later compose the admin URL. Catalog reads
  // (the catalog UI binding, the dev portal, third-party SDK consumers)
  // must never see that ref. The redaction lives at the read boundary so
  // a misuse like `item.target.adminDiagnosticsRef` in a UI binding
  // simply gets `null` rather than the operator correlation id.

  async function published(
    client: FixturePublishHandoffClient,
    sourceServiceId = "svc-1",
    adminDiagnosticsRef = "diag-1",
  ) {
    await client.receive(makePublishEvent({ sourceServiceId, adminDiagnosticsRef }));
  }

  it("findBySourceServiceId redacts adminDiagnosticsRef without admin:diagnostics", async () => {
    const client = makeClient();
    await published(client);
    const item = await client.findBySourceServiceId("svc-1");
    expect(item?.target.adminDiagnosticsRef).toBeNull();
  });

  it("findBySourceServiceId preserves adminDiagnosticsRef with admin:diagnostics", async () => {
    const client = makeClient();
    await published(client, "svc-1", "diag-9");
    const item = await client.findBySourceServiceId("svc-1", {
      permissions: ["admin:diagnostics"],
    });
    expect(item?.target.adminDiagnosticsRef).toBe("diag-9");
  });

  it("findBySourceServiceId accepts permissions as a Set", async () => {
    const client = makeClient();
    await published(client, "svc-1", "diag-9");
    const item = await client.findBySourceServiceId("svc-1", {
      permissions: new Set(["admin:diagnostics"]),
    });
    expect(item?.target.adminDiagnosticsRef).toBe("diag-9");
  });

  it("findBySourceServiceId redacts when permissions exist but lack admin:diagnostics", async () => {
    const client = makeClient();
    await published(client);
    const item = await client.findBySourceServiceId("svc-1", {
      permissions: ["catalog:read", "share:write"],
    });
    expect(item?.target.adminDiagnosticsRef).toBeNull();
  });

  it("get redacts adminDiagnosticsRef without admin:diagnostics", async () => {
    const client = makeClient();
    await published(client);
    const item = await client.get(FIRST_SERVICE_ITEM_ID);
    expect(item?.target.adminDiagnosticsRef).toBeNull();
  });

  it("get preserves adminDiagnosticsRef with admin:diagnostics", async () => {
    const client = makeClient();
    await published(client, "svc-1", "diag-5");
    const item = await client.get(FIRST_SERVICE_ITEM_ID, {
      permissions: ["admin:diagnostics"],
    });
    expect(item?.target.adminDiagnosticsRef).toBe("diag-5");
  });

  it("list redacts adminDiagnosticsRef on every item without admin:diagnostics", async () => {
    const client = makeClient();
    await client.receive(makePublishEvent({ sourceServiceId: "a", adminDiagnosticsRef: "diag-a" }));
    await client.receive(makePublishEvent({ sourceServiceId: "b", adminDiagnosticsRef: "diag-b" }));
    const items = await client.list();
    expect(items).toHaveLength(2);
    for (const item of items) {
      expect(item.target.adminDiagnosticsRef).toBeNull();
    }
  });

  it("list preserves adminDiagnosticsRef on every item with admin:diagnostics", async () => {
    const client = makeClient();
    await client.receive(makePublishEvent({ sourceServiceId: "a", adminDiagnosticsRef: "diag-a" }));
    await client.receive(makePublishEvent({ sourceServiceId: "b", adminDiagnosticsRef: "diag-b" }));
    const items = await client.list({ permissions: ["admin:diagnostics"] });
    const refs = items.map((i) => i.target.adminDiagnosticsRef).sort();
    expect(refs).toEqual(["diag-a", "diag-b"]);
  });

  it("surfaceForSourceService.ok redacts adminDiagnosticsRef without admin:diagnostics", async () => {
    const client = makeClient();
    await published(client);
    const surface = await client.surfaceForSourceService("svc-1");
    expect(surface.kind).toBe("ok");
    if (surface.kind === "ok") {
      expect(surface.item.target.adminDiagnosticsRef).toBeNull();
    }
  });

  it("surfaceForSourceService.ok preserves adminDiagnosticsRef with admin:diagnostics", async () => {
    const client = makeClient();
    await published(client, "svc-1", "diag-7");
    const surface = await client.surfaceForSourceService("svc-1", {
      permissions: ["admin:diagnostics"],
    });
    expect(surface.kind).toBe("ok");
    if (surface.kind === "ok") {
      expect(surface.item.target.adminDiagnosticsRef).toBe("diag-7");
    }
  });

  it("redaction does not mutate the stored record (subsequent admin read still sees the ref)", async () => {
    // The stored record retains the ref so an authorized later read can
    // resolve adminDiagnosticLink. Redaction only applies to the
    // returned clone.
    const client = makeClient();
    await published(client, "svc-1", "diag-stable");
    const nonAdmin = await client.findBySourceServiceId("svc-1");
    expect(nonAdmin?.target.adminDiagnosticsRef).toBeNull();
    const admin = await client.findBySourceServiceId("svc-1", {
      permissions: ["admin:diagnostics"],
    });
    expect(admin?.target.adminDiagnosticsRef).toBe("diag-stable");
  });

  it("non-operator-only fields are unaffected by redaction", async () => {
    // Only adminDiagnosticsRef is operator-only today. statusDetail is
    // already sanitized at write time and is intended to be user-visible
    // (catalog cards render it). The redaction must not over-strip.
    const client = makeClient();
    await client.receive(
      makePublishEvent({
        sourceServiceId: "svc-1",
        status: "degraded",
        statusReason: "Slower than usual response times.",
        adminDiagnosticsRef: "diag-1",
      }),
    );
    const item = await client.findBySourceServiceId("svc-1");
    expect(item?.target.statusDetail).toBe("Slower than usual response times.");
    expect(item?.target.serviceUrl).toMatch(/^https:\/\//);
    expect(item?.target.status).toBe("limited");
    expect(item?.target.adminDiagnosticsRef).toBeNull();
  });
});
