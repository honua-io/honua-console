// Console parity scenario.
//
// The scenario is the cross-surface chain called out in the
// honua-console#9 acceptance criteria:
//
//   1. Operator publishes a service through the operate path
//      (currently the legacy admin transition surface).
//   2. Service appears as a Console catalog item with metadata/provenance.
//   3. Open the item in the map viewer and save a map.
//   4. Studio creates a dashboard/app/report draft from the same source.
//   5. Publish/reopen the generated artifact as a content item.
//   6. Share/embed the generated artifact from the same Console origin.
//
// Each step declares the owningLayer that owns the contract it exercises,
// so a failure points the smoke triage at the right repo:
//
//   - devops:        build artifact / single deployable artifact
//   - legacy-admin:  publish-handoff event producer (transition surface)
//   - server:        catalog upsert, share, embed mint
//   - sdk:           browser-safe projections
//   - console:       Console-side UX / route map / same-origin invariants
//
// Steps are pure functions over a `ctx` object the runner mutates as the
// chain advances. Adding a new step requires choosing an owningLayer; the
// taxonomy is closed (see owning-layers.mjs) so a typo here will throw
// rather than silently widen the smoke surface.

import { loadPublishEvent } from "./adapters/admin.mjs";
import { buildConsoleUrls, assertSameOrigin } from "./adapters/console.mjs";
import { loadBuildArtifact } from "./adapters/devops.mjs";
import { projectAppPackage, projectBuilderPlan, projectCatalogSummary, projectGeneratedAppRecord } from "./adapters/sdk.mjs";
import { createServerAdapter } from "./adapters/server.mjs";
import { findContract } from "./contracts.mjs";

export const SCENARIO_ID = "console-parity-publish-to-embed";

export const SCENARIO_STEPS = [
  {
    id: "devops/build-artifact",
    owningLayer: "devops",
    description:
      "Verify the single deployable artifact metadata (dist/version.json) declares name=honua-console, the four areas, and the legacy block.",
    async run(ctx) {
      const { metadata, source, path, contract } = await loadBuildArtifact({ repoRoot: ctx.repoRoot });
      ctx.buildArtifact = { metadata, source, path };
      return {
        evidence: {
          source,
          path,
          version: metadata.version,
          commit: metadata.shortCommit,
          ref: metadata.ref,
          legacy: metadata.legacy,
          areas: metadata.areas,
        },
        contracts: [contract],
      };
    },
  },
  {
    id: "legacy-admin/operator-publish",
    owningLayer: "legacy-admin",
    description:
      "Operator triggers a publish from the transitional legacy admin surface; emits a publish-handoff event into Console catalog.",
    async run(ctx) {
      const event = await loadPublishEvent({ repoRoot: ctx.repoRoot });
      ctx.publishEvent = event;
      return {
        evidence: {
          sourceServiceId: event.sourceServiceId,
          eventKind: event.eventKind,
          serviceUrl: event.serviceUrl,
          actor: event.actor,
          status: event.status,
        },
        contracts: [findContract("publish-handoff")],
      };
    },
  },
  {
    id: "server/catalog-upsert",
    owningLayer: "server",
    description:
      "Server upserts the publish event into the catalog and returns a stable content item id.",
    async run(ctx) {
      ctx.server = ctx.server ?? createServerAdapter({ originUrl: ctx.originUrl });
      const { item, contract } = ctx.server.publishService(ctx.publishEvent);
      ctx.serviceItem = item;
      ctx.itemIds.serviceItemId = item.id;
      return {
        evidence: {
          itemId: item.id,
          serviceUrl: item.target.serviceUrl,
          status: item.target.status,
          sourceKind: item.source.kind,
          sourceId: item.source.sourceId,
        },
        contracts: [contract],
      };
    },
  },
  {
    id: "sdk/catalog-projection",
    owningLayer: "sdk",
    description:
      "SDK projects the server catalog row into the browser-safe ContentItemSummary Console consumes.",
    async run(ctx) {
      const summary = projectCatalogSummary(ctx.serviceItem);
      ctx.summary = summary;
      if (!summary.viewerSupport.supported) {
        throw new Error(`SDK projection marks the published service as unsupported by the viewer: ${summary.viewerSupport.reason}`);
      }
      return {
        evidence: {
          itemId: summary.id,
          title: summary.title,
          viewerSupported: summary.viewerSupport.supported,
          capabilities: summary.capabilities,
        },
        contracts: [summary.contract],
      };
    },
  },
  {
    id: "console/catalog-list",
    owningLayer: "console",
    description:
      "Console catalog browse lists the new item without a per-card detail fetch.",
    async run(ctx) {
      const { items, contract } = ctx.server.listCatalog();
      const match = items.find((i) => i.id === ctx.serviceItem.id);
      if (!match) {
        throw new Error(
          `Catalog list response did not include published item ${ctx.serviceItem.id}; Console catalog UI would render an empty grid.`,
        );
      }
      return {
        evidence: { listed: items.length, includesPublished: true, listedItemId: match.id },
        contracts: [contract],
      };
    },
  },
  {
    id: "console/viewer-open",
    owningLayer: "console",
    description:
      "Console viewer route opens the catalog item via the same-origin /maps/new?from=... hydration URL.",
    async run(ctx) {
      const hydrationUrl = `${ctx.originUrl}/maps/new?from=${ctx.serviceItem.id}`;
      assertSameOrigin(ctx.originUrl, { hydration: hydrationUrl });
      return { evidence: { hydrationUrl } };
    },
  },
  {
    id: "console/saved-map-save",
    owningLayer: "console",
    description:
      "Save a map that references the published service; saved-map document conforms to webmap-doc/v1.",
    async run(ctx) {
      const { savedMap, contract } = ctx.server.saveMap({
        title: `${ctx.serviceItem.title} — overview`,
        owner: ctx.serviceItem.owner,
        operationalLayers: [
          { id: ctx.serviceItem.id, type: "service", role: "operationalLayer", serviceUrl: ctx.serviceItem.target.serviceUrl },
        ],
        extent: ctx.serviceItem.extent,
      });
      ctx.savedMap = savedMap;
      ctx.itemIds.savedMapId = savedMap.id;
      return {
        evidence: { savedMapId: savedMap.id, title: savedMap.title, operationalLayerCount: savedMap.document.operationalLayers.length },
        contracts: [contract],
      };
    },
  },
  {
    id: "console/studio-draft",
    owningLayer: "console",
    description:
      "Studio creates a dashboard/app/report draft from the saved map; the draft route is same-origin.",
    async run(ctx) {
      const draftUrl = `${ctx.originUrl}/studio/drafts?source=saved-map&id=${ctx.savedMap.id}`;
      assertSameOrigin(ctx.originUrl, { draft: draftUrl });
      ctx.studioDraft = {
        source: { kind: "saved-map", itemId: ctx.savedMap.id, itemType: "map", title: ctx.savedMap.title },
        url: draftUrl,
      };
      return { evidence: { draftUrl, source: ctx.studioDraft.source } };
    },
  },
  {
    id: "sdk/app-package-build",
    owningLayer: "sdk",
    description:
      "SDK builds the BuilderPlan and AppPackage from the draft using shared contracts.",
    async run(ctx) {
      const plan = projectBuilderPlan({ source: ctx.studioDraft.source, savedMap: ctx.savedMap });
      const appPackage = projectAppPackage({ plan });
      ctx.builderPlan = plan;
      ctx.appPackage = appPackage;
      return {
        evidence: {
          planId: plan.id,
          appPackageId: appPackage.id,
          widgetKinds: appPackage.widgets.map((w) => w.kind),
        },
        contracts: [plan.contract, appPackage.contract],
      };
    },
  },
  {
    id: "server/generated-app-publish",
    owningLayer: "server",
    description:
      "Server records the generated app as a published content item with provenance back to the source service.",
    async run(ctx) {
      const { item, contract } = ctx.server.publishGeneratedApp({
        source: ctx.studioDraft.source,
        manifestVersion: "1.0.0",
        plan: ctx.builderPlan,
        appPackage: ctx.appPackage,
        owner: ctx.serviceItem.owner,
        title: `${ctx.savedMap.title} — Field App`,
      });
      ctx.generatedApp = item;
      ctx.itemIds.generatedAppId = item.id;
      const record = projectGeneratedAppRecord(item);
      return {
        evidence: {
          generatedAppId: item.id,
          activeRevisionId: record.lifecycle.activeRevisionId,
          provenanceCount: record.activeRevision.provenance.length,
          dependencyIds: item.dependencies.map((d) => d.id),
        },
        contracts: [contract],
      };
    },
  },
  {
    id: "console/share-publish",
    owningLayer: "console",
    description:
      "Console share dialog promotes the generated app to org-tier and marks it embeddable.",
    async run(ctx) {
      const result = ctx.server.patchAccess({ itemId: ctx.generatedApp.id, tier: "org", embeddable: true });
      if (result.kind !== "ok") {
        throw new Error(`share patch returned ${result.kind} for generated app ${ctx.generatedApp.id}`);
      }
      ctx.itemIds.shareTier = result.access.sharing;
      return {
        evidence: { itemId: ctx.generatedApp.id, sharing: result.access.sharing, embeddable: result.access.embeddable },
        contracts: [result.contract],
      };
    },
  },
  {
    id: "server/embed-token-mint",
    owningLayer: "server",
    description:
      "Server mints a same-origin embed token descriptor for the generated app.",
    async run(ctx) {
      const result = ctx.server.mintEmbedToken({ itemId: ctx.generatedApp.id, audience: "pilot" });
      if (result.kind !== "ok") {
        throw new Error(`embed-token mint returned ${result.kind} for generated app ${ctx.generatedApp.id}`);
      }
      ctx.embedToken = result.descriptor.token;
      ctx.itemIds.embedToken = result.descriptor.token;
      return {
        evidence: {
          token: result.descriptor.token,
          audience: result.descriptor.audience,
          closureSize: result.descriptor.closure.length,
          expiresAt: result.descriptor.expiresAt,
        },
        contracts: [result.contract],
      };
    },
  },
  {
    id: "console/embed-render",
    owningLayer: "console",
    description:
      "Console assembles the same-origin embed URL using the minted token; URL is reachable from the deployable artifact origin.",
    async run(ctx) {
      const urls = buildConsoleUrls({ originUrl: ctx.originUrl, items: ctx.itemIds });
      assertSameOrigin(ctx.originUrl, urls);
      ctx.urls = urls;
      return { evidence: { urls } };
    },
  },
];
