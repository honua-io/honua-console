// Contract-shape tests for the smoke fixtures and adapter output. These
// tests are the smoke's own guardrail against the "harness reports a
// contract version while exercising a drifted shape" class of bug. They
// do not stand up the full JSON Schema validator (the canonical schemas
// live in honua-portal/schemas/) — they assert the required field set
// and key cross-field invariants the smoke depends on. When the porting
// tickets (#3/#4/#5/#7) replace the in-memory adapters with imports
// from @honua/sdk-js and the real server transport, these tests follow
// to validate the live envelopes against the canonical schema.

import { strict as assert } from "node:assert";
import { describe, test } from "node:test";

import { loadPublishHandoff, PUBLISH_HANDOFF_REQUIRED_FIELDS, validatePublishHandoff } from "../adapters/admin.mjs";
import { createServerAdapter } from "../adapters/server.mjs";
import {
  projectAppPackage,
  projectBuilderPlan,
  projectCatalogSummary,
  projectGeneratedAppRecord,
  summarizeContentItem,
} from "../adapters/sdk.mjs";
import { CONSOLE_ROUTES } from "../adapters/console.mjs";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(HERE, "../../..");

// Canonical content-item/v1.1.0 ContentItemSummary required fields, copied
// verbatim from honua-portal/schemas/content-item-v1.json so a contract
// bump there is caught here on the next test run.
const CONTENT_ITEM_SUMMARY_REQUIRED = [
  "id",
  "slug",
  "type",
  "title",
  "summary",
  "owner",
  "tags",
  "extent",
  "preview",
  "modified",
  "capabilities",
  "formats",
  "sharing",
  "openData",
  "viewerSupport",
];

const CONTENT_ITEM_ULID_REGEX = /^[0-9A-HJKMNP-TV-Z]{26}$/;

describe("publish-handoff fixture", () => {
  test("ships every top-level field required by publish-handoff/v1.1.0", async () => {
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    for (const field of PUBLISH_HANDOFF_REQUIRED_FIELDS) {
      assert.ok(field in handoff, `publish-handoff fixture missing required field "${field}"`);
    }
  });

  test("source provides kind, sourceId, jobId, publishedBy", async () => {
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    for (const field of ["kind", "sourceId", "jobId", "publishedBy"]) {
      assert.ok(field in handoff.source, `publish-handoff.source missing "${field}"`);
    }
    assert.ok(["import", "publish", "admin-job", "external"].includes(handoff.source.kind));
  });

  test("target.type matches the item type (publish-handoff/v1 cross-field invariant)", async () => {
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    assert.equal(handoff.target.type, handoff.type, "target.type must equal item type");
  });

  test("endpoints declares all four protocol slots", async () => {
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    for (const slot of ["geoservices", "ogcFeatures", "stac", "tiles"]) {
      assert.ok(slot in handoff.endpoints, `publish-handoff.endpoints missing slot "${slot}"`);
    }
  });

  test("populated ServiceLinks carry the v1.1 required keys", async () => {
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    for (const slot of ["geoservices", "ogcFeatures", "stac", "tiles"]) {
      const link = handoff.endpoints[slot];
      if (!link) continue;
      for (const key of ["accessURL", "format", "mediaType", "describedBy", "describedByType", "conformsTo"]) {
        assert.ok(key in link, `endpoint ${slot} missing ServiceLink key "${key}"`);
      }
    }
  });

  test("access.openData=true implies sharing=public", async () => {
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    if (handoff.access.openData) {
      assert.equal(handoff.access.sharing, "public");
    }
  });

  test("Honolulu fixture uses the Oahu State Plane native CRS", async () => {
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    assert.deepEqual(handoff.extent.bbox, [-158.3, 21.2, -157.6, 21.7]);
    assert.equal(handoff.extent.crs, "EPSG:4326");
    assert.equal(handoff.nativeCrs, "EPSG:2784");
  });
});

describe("validatePublishHandoff access object", () => {
  // Negative tests for the content-item/v1.1.0 Access object: the canonical
  // schema requires sharing (enum), embeddable (boolean), openData (boolean),
  // and enforces openData=true => sharing=public. The smoke validator now
  // matches that surface so a drifted admin producer cannot publish a
  // schema-invalid access object.
  async function baseHandoff() {
    return await loadPublishHandoff({ repoRoot: REPO_ROOT });
  }

  test("rejects missing access.openData", async () => {
    const handoff = await baseHandoff();
    delete handoff.access.openData;
    assert.throws(
      () => validatePublishHandoff(handoff, "test"),
      /access\.openData/,
    );
  });

  test("rejects non-boolean access.openData", async () => {
    const handoff = await baseHandoff();
    handoff.access.openData = "false";
    assert.throws(
      () => validatePublishHandoff(handoff, "test"),
      /access\.openData/,
    );
  });

  test("rejects non-boolean access.embeddable", async () => {
    const handoff = await baseHandoff();
    handoff.access.embeddable = "true";
    assert.throws(
      () => validatePublishHandoff(handoff, "test"),
      /access\.embeddable/,
    );
  });

  test("rejects access.sharing outside the schema enum", async () => {
    const handoff = await baseHandoff();
    handoff.access.sharing = "world-readable";
    assert.throws(
      () => validatePublishHandoff(handoff, "test"),
      /invalid access\.sharing/,
    );
  });

  test("rejects openData=true with non-public sharing", async () => {
    const handoff = await baseHandoff();
    handoff.access = { sharing: "private", embeddable: false, openData: true };
    assert.throws(
      () => validatePublishHandoff(handoff, "test"),
      /open-data items must be shared as public/,
    );
  });

  test("accepts openData=true with sharing=public", async () => {
    const handoff = await baseHandoff();
    handoff.access = { sharing: "public", embeddable: true, openData: true };
    assert.doesNotThrow(() => validatePublishHandoff(handoff, "test"));
  });
});

describe("server.publishService output", () => {
  test("emits a content item that survives the SDK summarize() projection", async () => {
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    const server = createServerAdapter({ originUrl: "https://console.smoke.example" });
    const { item } = server.publishService(handoff);

    // Server fills in id, endpoints.self, and source.history.
    assert.ok(item.id, "server must assign an id");
    assert.match(item.id, CONTENT_ITEM_ULID_REGEX);
    assert.equal(item.endpoints.self.format, "Honua:Portal:v1");
    assert.ok(item.source.history.length >= 1, "server must seed source.history");

    const summary = summarizeContentItem(item);
    for (const field of CONTENT_ITEM_SUMMARY_REQUIRED) {
      assert.ok(field in summary, `summary missing required field "${field}"`);
    }
    // Formats are derived from non-self endpoints; the fixture's geoservices
    // + ogcFeatures slots must show up so catalog cards can render pills.
    assert.ok(summary.formats.includes("GeoServices:FeatureService"));
    assert.ok(summary.formats.includes("OGC:API:Features"));
  });

  test("listCatalog returns ContentItemSummary[] (not raw rows)", async () => {
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    const server = createServerAdapter({ originUrl: "https://console.smoke.example" });
    server.publishService(handoff);
    const { items } = server.listCatalog();
    assert.equal(items.length, 1);
    const [first] = items;
    for (const field of CONTENT_ITEM_SUMMARY_REQUIRED) {
      assert.ok(field in first, `catalog list item missing summary field "${field}"`);
    }
  });

  test("upsert identity keys on (source.kind, source.sourceId), not sourceId alone", async () => {
    // publish-handoff/v1.1.0 idempotency: re-publishing the same sourceId under
    // the same kind merges history; under a different kind, the second publish
    // is a distinct provenance chain and must mint a new item.
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    const server = createServerAdapter({ originUrl: "https://console.smoke.example" });

    const { item: first } = server.publishService(handoff);
    const { item: republish } = server.publishService(handoff);
    assert.equal(republish.id, first.id, "same (kind, sourceId) must merge into the same item");
    assert.ok(
      republish.source.history.length >= 2,
      "republish must append to source.history rather than reset it",
    );

    const externalHandoff = structuredClone(handoff);
    externalHandoff.source = { ...handoff.source, kind: "external" };
    const { item: external } = server.publishService(externalHandoff);
    assert.notEqual(
      external.id,
      first.id,
      "same sourceId under a different source.kind must mint a distinct item",
    );
    assert.equal(external.source.kind, "external");
    assert.equal(external.source.history.length, 1, "external publish gets its own history chain");
  });

  test("re-publish preserves portal-owned access state (sharing/embed/openData survive)", async () => {
    // Canonical portal mapping: re-publishing the same service via legacy
    // admin must not reset Console-managed access state. A regression here
    // would silently drop org/public sharing on every re-publish, which is
    // not what an operator triggering a metadata refresh expects.
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    const server = createServerAdapter({ originUrl: "https://console.smoke.example" });
    const { item: first } = server.publishService(handoff);
    // Promote to org-tier embeddable via the share surface.
    server.patchAccess({ itemId: first.id, tier: "org", embeddable: true });
    const { item: republish } = server.publishService(handoff);
    assert.equal(republish.id, first.id);
    assert.equal(republish.access.sharing, "org", "re-publish must not reset sharing tier");
    assert.equal(republish.access.embeddable, true, "re-publish must not reset embeddable");
    assert.equal(republish.access.openData, false, "re-publish must not touch openData");
  });

  test("re-publish preserves portal-owned preview, dependencies, extensions, endpoints.self", async () => {
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    const server = createServerAdapter({ originUrl: "https://console.smoke.example" });
    const { item: first } = server.publishService(handoff);

    // Simulate Console-side mutations of portal-owned fields after publish.
    first.preview = { thumbnail: "https://console.smoke.example/cdn/edited.png", image: null };
    first.dependencies = [{ id: "01ARZ3NDEKTSV4RRFFQ69G5FAV", type: "layer", role: "datasource" }];
    first.extensions = { ...first.extensions, "honua-portal-viewer": { supported: false, reason: "tiles" } };
    const editedSelf = first.endpoints.self;

    const { item: republish } = server.publishService(handoff);
    assert.deepEqual(republish.preview, first.preview, "re-publish must not reset preview");
    assert.deepEqual(republish.dependencies, first.dependencies, "re-publish must not reset dependencies");
    assert.deepEqual(republish.extensions, first.extensions, "re-publish must not reset extensions");
    assert.equal(republish.endpoints.self, editedSelf, "re-publish must not reset endpoints.self");

    // Handoff-owned fields still flow through on re-publish.
    assert.equal(republish.title, handoff.title);
    assert.equal(republish.summary, handoff.summary);
    assert.deepEqual(republish.endpoints.geoservices, handoff.endpoints.geoservices);
    // timestamps.created comes from the first publish; timestamps.modified
    // is bumped to "now" on every publish. We assert created is stable rather
    // than modified-is-new because two publishes can land in the same ms.
    assert.equal(republish.timestamps.created, first.timestamps.created);
    assert.ok(republish.source.history.length >= 2, "source.history must append on re-publish");
  });
});

describe("projectCatalogSummary", () => {
  test("returns the canonical ContentItemSummary plus a contract tag", async () => {
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    const server = createServerAdapter({ originUrl: "https://console.smoke.example" });
    const { item } = server.publishService(handoff);
    const projected = projectCatalogSummary(item);
    for (const field of CONTENT_ITEM_SUMMARY_REQUIRED) {
      assert.ok(field in projected, `projection missing summary field "${field}"`);
    }
    assert.equal(projected.contract.name, "content-item");
    assert.equal(projected.contract.version, "v1.1.0");
    // ViewerSupport schema is `null` when the publisher has no override,
    // otherwise `{ supported, reason }`. The fixture sets no extensions,
    // so the summary MUST carry null here.
    assert.equal(projected.viewerSupport, null);
  });

  test("projects publisher viewerSupport override when extensions assert one", async () => {
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    const server = createServerAdapter({ originUrl: "https://console.smoke.example" });
    const { item } = server.publishService(handoff);
    item.extensions = {
      ...item.extensions,
      "honua-portal-viewer": { supported: false, reason: "Service uses an unsupported tile scheme." },
    };
    const projected = projectCatalogSummary(item);
    assert.deepEqual(projected.viewerSupport, {
      supported: false,
      reason: "Service uses an unsupported tile scheme.",
    });
  });

  test("resolveViewerOpenability falls back to type defaults when summary.viewerSupport is null", async () => {
    const { resolveViewerOpenability } = await import("../adapters/sdk.mjs");
    // Service: openable by default.
    assert.equal(
      resolveViewerOpenability({ type: "service", viewerSupport: null }).supported,
      true,
    );
    // App: type default says not openable in the viewer.
    assert.equal(
      resolveViewerOpenability({ type: "app", viewerSupport: null }).supported,
      false,
    );
    // Publisher override wins.
    assert.deepEqual(
      resolveViewerOpenability({
        type: "service",
        viewerSupport: { supported: false, reason: "tiles" },
      }),
      { supported: false, reason: "tiles" },
    );
  });
});

describe("generated-app content-item mapping", () => {
  test("emits schema-valid ULIDs for content items and dependency refs", async () => {
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    const server = createServerAdapter({ originUrl: "https://console.smoke.example" });
    const { item: svc } = server.publishService(handoff);
    const { savedMap } = server.saveMap({
      title: "x",
      owner: svc.owner,
      sourceItem: svc,
      extent: svc.extent,
    });
    const { item: app } = server.publishGeneratedApp({
      source: { kind: "saved-map", itemId: savedMap.id, itemType: "map", title: savedMap.title },
      manifestVersion: "1.0.0",
      plan: { id: "plan-1", warnings: [] },
      appPackage: { id: "pkg-1", version: "1" },
      owner: svc.owner,
      title: "field app",
    });

    const lifecycle = app.extensions["honua-generated-app"];
    const active = lifecycle.revisions.find((r) => r.id === lifecycle.activeRevisionId);
    for (const id of [svc.id, savedMap.id, app.id, lifecycle.activeRevisionId]) {
      assert.match(id, CONTENT_ITEM_ULID_REGEX);
    }
    for (const dep of app.dependencies) {
      assert.match(dep.id, CONTENT_ITEM_ULID_REGEX);
    }
    for (const provenance of active.provenance) {
      assert.match(provenance.itemId, CONTENT_ITEM_ULID_REGEX);
    }
  });

  test("publishGeneratedApp emits target.url/framework and saved-map dependency", async () => {
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    const server = createServerAdapter({ originUrl: "https://console.smoke.example" });
    const { item: svc } = server.publishService(handoff);
    const { savedMap } = server.saveMap({
      title: "x",
      owner: svc.owner,
      sourceItem: svc,
      extent: svc.extent,
    });
    const { item: app } = server.publishGeneratedApp({
      source: { kind: "saved-map", itemId: savedMap.id, itemType: "map", title: savedMap.title },
      manifestVersion: "1.0.0",
      plan: { id: "plan-1", warnings: [] },
      appPackage: { id: "pkg-1", version: "1" },
      owner: svc.owner,
      title: "field app",
    });

    assert.equal(app.target.type, "app");
    assert.equal(app.target.framework, "honua");
    assert.match(app.target.url, /^https?:\/\//);
    assert.equal(app.source.kind, "manual");
    const dep = app.dependencies.find((d) => d.id === savedMap.id);
    assert.ok(dep);
    assert.equal(dep.role, "saved-map");

    // projectGeneratedAppRecord must accept the new shape and project a
    // canonical summary so generated-app cards render the same as service cards.
    const record = projectGeneratedAppRecord(app);
    for (const field of CONTENT_ITEM_SUMMARY_REQUIRED) {
      assert.ok(field in record.summary, `generated-app summary missing "${field}"`);
    }
  });

  test("rejects an unknown source.kind so taxonomy cannot silently widen", async () => {
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    const server = createServerAdapter({ originUrl: "https://console.smoke.example" });
    const { item: svc } = server.publishService(handoff);
    assert.throws(
      () =>
        server.publishGeneratedApp({
          source: { kind: "datasource", itemId: svc.id, itemType: "service", title: svc.title },
          manifestVersion: "1.0.0",
          plan: { id: "plan-1", warnings: [] },
          appPackage: { id: "pkg-1", version: "1" },
          owner: svc.owner,
          title: "field app",
        }),
      /saved-map.+catalog-item/,
    );
  });
});

describe("BuilderPlan / AppPackage SDK contracts", () => {
  // The smoke must exercise the structural shape @honua/sdk-js consumers
  // (BuilderWorkspaceController, generated-app preview projection) require:
  // BuilderPlan { id, intentId, kind="builder", steps[] }; AppPackage
  // { id, version, assets[] } with manifest_artifact OR manifestArtifact
  // for the generated-app preview projection. A drifted projection that
  // omits these fields would let the smoke pass while shipping a package
  // Console cannot hydrate — so a contract regression here is recorded as
  // an SDK-layer failure, not a downstream Console mystery.
  function fixturePlanInputs() {
    return {
      source: { kind: "saved-map", itemId: "svc-001", itemType: "service", title: "Source" },
      savedMap: { id: "map-001" },
    };
  }

  test("projectBuilderPlan ships every required BuilderPlan field", () => {
    const plan = projectBuilderPlan(fixturePlanInputs());
    for (const field of ["id", "intentId", "kind", "steps"]) {
      assert.ok(field in plan, `BuilderPlan missing required field "${field}"`);
    }
    assert.equal(plan.kind, "builder", "BuilderPlan.kind must be the literal 'builder'");
    assert.ok(Array.isArray(plan.steps) && plan.steps.length > 0, "BuilderPlan.steps must be non-empty");
    for (const step of plan.steps) {
      for (const field of ["id", "kind", "label"]) {
        assert.ok(field in step, `PlanStep missing required field "${field}"`);
      }
    }
  });

  test("projectAppPackage ships AppPackage assets[] plus a manifest artifact", () => {
    const plan = projectBuilderPlan(fixturePlanInputs());
    const pkg = projectAppPackage({ plan });
    for (const field of ["id", "version", "assets"]) {
      assert.ok(field in pkg, `AppPackage missing required field "${field}"`);
    }
    assert.ok(Array.isArray(pkg.assets) && pkg.assets.length > 0, "AppPackage.assets must be non-empty");
    for (const asset of pkg.assets) {
      assert.ok("id" in asset && "kind" in asset, "ArtifactRef must carry id and kind");
    }
    // Generated-app preview projection requires manifest_artifact OR manifestArtifact.
    const manifestArtifact = pkg.manifestArtifact ?? pkg.manifest_artifact;
    assert.ok(manifestArtifact, "AppPackage must carry manifest_artifact or manifestArtifact");
    assert.equal(manifestArtifact.artifactKind, "honua.generated-app.manifest");
    assert.equal(manifestArtifact.artifactVersion, 1);
    // Manifest layout uses an operations-dashboard profile with concrete widgets.
    assert.equal(manifestArtifact.manifest.profile, "operations-dashboard.v1");
    assert.equal(manifestArtifact.manifest.layout.kind, "operations-dashboard");
    const widgetKinds = manifestArtifact.manifest.layout.widgets.map((w) => w.kind);
    for (const kind of widgetKinds) {
      assert.ok(
        ["map", "table", "list", "count", "chart", "filter"].includes(kind),
        `widget kind "${kind}" not in HonuaGeneratedAppWidgetKind union`,
      );
    }
    // The smoke pins both casings so a single-casing regression is caught.
    assert.equal(pkg.manifestArtifact, pkg.manifest_artifact);
  });
});

describe("embed URL placement", () => {
  test("embed route puts the token in the URL fragment, not the query string", () => {
    const url = `https://console.smoke.example${CONSOLE_ROUTES.embed("app-1", "tok-1")}`;
    const parsed = new URL(url);
    assert.equal(parsed.hash, "#embedToken=tok-1");
    assert.equal(parsed.search, "", "embed URL must not carry a query string");
  });

  test("encodes special characters in the token", () => {
    const url = `https://console.smoke.example${CONSOLE_ROUTES.embed("app-1", "tok with space=&")}`;
    const parsed = new URL(url);
    assert.equal(decodeURIComponent(parsed.hash.replace("#embedToken=", "")), "tok with space=&");
  });
});

describe("share-access response shape", () => {
  test("patchAccess returns share-access/v1 (no openData leak)", async () => {
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    const server = createServerAdapter({ originUrl: "https://console.smoke.example" });
    const { item } = server.publishService(handoff);
    const result = server.patchAccess({ itemId: item.id, tier: "org", embeddable: true });
    assert.equal(result.kind, "ok");
    assert.deepEqual(Object.keys(result.access).sort(), ["embeddable", "sharing"]);
  });

  test("group tier carries the groupIds field", async () => {
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    const server = createServerAdapter({ originUrl: "https://console.smoke.example" });
    const { item } = server.publishService(handoff);
    const result = server.patchAccess({
      itemId: item.id,
      tier: "group",
      embeddable: false,
      groupIds: ["grp-1", "grp-2"],
    });
    assert.equal(result.kind, "ok");
    assert.deepEqual(result.access.groupIds, ["grp-1", "grp-2"]);
  });

  test("rejects an invalid sharing tier with kind=invalid-tier", async () => {
    // A tier outside the canonical enum (private|org|group|public-link|public)
    // must be rejected rather than written through. Accepting "world-readable"
    // would emit a share-access/v1 envelope with a schema-invalid sharing
    // string, exactly the harness-claims-conformance-while-exercising-drift
    // class of bug the contract-shapes tests pin against.
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    const server = createServerAdapter({ originUrl: "https://console.smoke.example" });
    const { item } = server.publishService(handoff);
    const result = server.patchAccess({ itemId: item.id, tier: "world-readable", embeddable: false });
    assert.equal(result.kind, "invalid-tier");
    assert.equal(result.tier, "world-readable");
    assert.deepEqual(result.allowed, ["private", "org", "group", "public-link", "public"]);
    // The item's sharing tier must remain unchanged.
    const { item: re } = server.getItem(item.id);
    assert.equal(re.access.sharing, "private");
  });

  test("public sharing does NOT auto-enable openData (invariant runs one way only)", async () => {
    // The canonical invariant is `openData=true => sharing="public"`, never
    // the reverse. A patch to public sharing must leave openData alone; the
    // open-data toggle is a separate content-item mutation.
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    const server = createServerAdapter({ originUrl: "https://console.smoke.example" });
    const { item } = server.publishService(handoff);
    assert.equal(item.access.openData, false, "fixture seeds openData=false");
    const result = server.patchAccess({ itemId: item.id, tier: "public", embeddable: true });
    assert.equal(result.kind, "ok");
    assert.equal(result.access.sharing, "public");
    // Response stays inside share-access/v1 (no openData echo).
    assert.ok(!("openData" in result.access));
    // openData on the content-item is preserved across the share patch.
    const { item: re } = server.getItem(item.id);
    assert.equal(re.access.openData, false, "openData ownership belongs to the open-data toggle, not the share patch");
  });

  test("an open-data item cannot be narrowed below public via the share patch", async () => {
    // Inverse of the invariant: if openData=true, the share patch must not
    // leave the item in an openData=true with non-public state. The smoke
    // surfaces this as `open-data-locked` so a Console reviewer can tell
    // the rejection from a missing-item or invalid-tier outcome.
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    const server = createServerAdapter({ originUrl: "https://console.smoke.example" });
    const { item } = server.publishService(handoff);
    // Promote to open-data via the canonical path (publish patch to public
    // first, then the open-data toggle would flip openData; for the smoke
    // we mutate directly because there is no open-data adapter today).
    item.access = { sharing: "public", embeddable: true, openData: true };
    const blocked = server.patchAccess({ itemId: item.id, tier: "org", embeddable: true });
    assert.equal(blocked.kind, "open-data-locked");
    // The item must remain in its prior public/open-data state.
    const { item: re } = server.getItem(item.id);
    assert.equal(re.access.sharing, "public");
    assert.equal(re.access.openData, true);
  });
});

describe("webmap-doc shape", () => {
  test("saveMap document declares version honua-webmap/v1 with operationalLayers, baseMap, initialState", async () => {
    const handoff = await loadPublishHandoff({ repoRoot: REPO_ROOT });
    const server = createServerAdapter({ originUrl: "https://console.smoke.example" });
    const { item } = server.publishService(handoff);
    const { savedMap } = server.saveMap({
      title: "x",
      owner: item.owner,
      sourceItem: item,
      extent: item.extent,
    });
    assert.equal(savedMap.document.version, "honua-webmap/v1");
    assert.ok(Array.isArray(savedMap.document.operationalLayers));
    assert.ok(savedMap.document.operationalLayers.length >= 1);
    assert.ok(savedMap.document.baseMap);
    assert.ok(savedMap.document.initialState?.viewpoint?.extent);
  });
});
