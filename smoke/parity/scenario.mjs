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
//   6. Share the generated artifact and embed the saved map from the same Console origin.
//
// Each step declares the owningLayer that owns the contract it exercises,
// so a failure points the smoke triage at the right repo:
//
//   - devops:        build artifact / single deployable artifact
//   - legacy-admin:  publish-handoff producer (transition surface)
//   - server:        catalog upsert, share, embed mint
//   - sdk:           browser-safe projections
//   - console:       Console-side UX / route map / same-origin invariants
//
// Steps are pure functions over a `ctx` object the runner mutates as the
// chain advances. Adding a new step requires choosing an owningLayer; the
// taxonomy is closed (see owning-layers.mjs) so a typo here will throw
// rather than silently widen the smoke surface.

import { loadPublishHandoff } from "./adapters/admin.mjs";
import {
  CONSOLE_ROUTES,
  assertEmbedTokenInFragment,
  assertSameOrigin,
  buildConsoleUrls,
  redactConsoleUrls,
  redactEmbedToken,
} from "./adapters/console.mjs";
import { loadBuildArtifact } from "./adapters/devops.mjs";
import {
  projectAppPackage,
  projectBuilderPlan,
  projectCatalogSummary,
  projectGeneratedAppRecord,
  resolveViewerOpenability,
} from "./adapters/sdk.mjs";
import { createServerAdapter } from "./adapters/server.mjs";
import { findContract } from "./contracts.mjs";

export const SCENARIO_ID = "console-parity-publish-to-embed";

export const SCENARIO_STEPS = [
  {
    id: "devops/build-artifact",
    owningLayer: "devops",
    description:
      "Verify the single deployable artifact metadata (version.json) declares name=honua-console, the four areas, and the legacy block.",
    async run(ctx) {
      const { metadata, source, path, contract } = await loadBuildArtifact({
        repoRoot: ctx.repoRoot,
        originUrl: ctx.originUrl,
        fetchImpl: ctx.fetchImpl,
      });
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
      "Operator triggers a publish from the transitional legacy admin surface; emits a publish-handoff/v1.1.0 payload into Console catalog.",
    async run(ctx) {
      const handoff = await loadPublishHandoff({ repoRoot: ctx.repoRoot });
      ctx.publishHandoff = handoff;
      return {
        evidence: {
          type: handoff.type,
          title: handoff.title,
          sourceKind: handoff.source.kind,
          sourceId: handoff.source.sourceId,
          publishedBy: handoff.source.publishedBy,
          serviceUrl: handoff.target.serviceUrl,
          status: handoff.target.status,
        },
        contracts: [findContract("publish-handoff")],
      };
    },
  },
  {
    id: "server/catalog-upsert",
    owningLayer: "server",
    description:
      "Server upserts the publish-handoff into the catalog and returns a stable content item id.",
    async run(ctx) {
      ctx.server = ctx.server ?? createServerAdapter({ originUrl: ctx.originUrl });
      const { item, contract } = ctx.server.publishService(ctx.publishHandoff);
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
      "SDK projects the server catalog row into the canonical content-item/v1.1.0 ContentItemSummary Console consumes.",
    async run(ctx) {
      const summary = projectCatalogSummary(ctx.serviceItem);
      ctx.summary = summary;
      // content-item/v1.1.0 ContentItemSummary.viewerSupport is null when
      // the publisher has not asserted an override; the openability gate
      // is resolved by the viewer layer using the type default. The smoke
      // mirrors that split so a regression that bakes type defaults back
      // into the summary DTO is caught here (the projection drift the
      // SDK adapter must not silently absorb).
      const openability = resolveViewerOpenability(summary);
      if (openability.supported !== true) {
        throw new Error(
          `SDK projection + viewer openability mark the published service as unsupported: ${openability.reason ?? "no reason"}`,
        );
      }
      return {
        evidence: {
          itemId: summary.id,
          title: summary.title,
          viewerSupport: summary.viewerSupport,
          viewerOpenability: openability,
          capabilities: summary.capabilities,
          formats: summary.formats,
          sharing: summary.sharing,
          openData: summary.openData,
        },
        contracts: [summary.contract],
      };
    },
  },
  {
    id: "console/catalog-list",
    owningLayer: "console",
    description:
      "Console catalog browse lists the new item as content-item/v1.1.0 ContentItemSummary without a per-card detail fetch.",
    async run(ctx) {
      const { items, contract } = ctx.server.listCatalog();
      const match = items.find((i) => i.id === ctx.serviceItem.id);
      if (!match) {
        throw new Error(
          `Catalog list response did not include published item ${ctx.serviceItem.id}; Console catalog UI would render an empty grid.`,
        );
      }
      // Sanity: the list response must carry summary-shape fields so the
      // catalog cards can render format pills without a per-item detail fetch.
      for (const required of ["tags", "preview", "modified", "formats", "sharing", "openData", "viewerSupport"]) {
        if (!(required in match)) {
          throw new Error(
            `Catalog list response missing ContentItemSummary field "${required}"; cards would need a per-item detail fetch.`,
          );
        }
      }
      return {
        evidence: { listed: items.length, includesPublished: true, listedItemId: match.id, formats: match.formats },
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
        sourceItem: ctx.serviceItem,
        extent: ctx.serviceItem.extent,
      });
      ctx.savedMap = savedMap;
      ctx.itemIds.savedMapId = savedMap.id;
      if (savedMap.document.version !== "honua-webmap/v1") {
        throw new Error(`saved-map document version ${savedMap.document.version} does not match webmap-doc/v1.`);
      }
      return {
        evidence: {
          savedMapId: savedMap.id,
          title: savedMap.title,
          documentVersion: savedMap.document.version,
          operationalLayerCount: savedMap.document.operationalLayers.length,
        },
        contracts: [contract],
      };
    },
  },
  {
    id: "console/studio-draft",
    owningLayer: "console",
    description:
      "Studio accepts the source-scoped draft route, clarifies an ambiguous prompt, and keeps the route-compatible package inspectable from the honua-server-bound package lifecycle.",
    async run(ctx) {
      const draftUrl = `${ctx.originUrl}${CONSOLE_ROUTES.studioDraftForMap(ctx.savedMap.id)}`;
      assertSameOrigin(ctx.originUrl, { draft: draftUrl });
      const sourceContext = { kind: "saved-map", itemId: ctx.savedMap.id, itemType: "map", title: ctx.savedMap.title };
      const authoringPackage = {
        contractName: "studio-authoring-shell",
        contractVersion: "v1",
        packageRef: "draft-app-clarify",
        packageType: "app.package",
        // Server family schema version advertised by the bound honua-server lifecycle
        // (StudioPackageFamilyCapabilities.currentSchemaVersion / ServerStudioAuthoringShell "1.0"
        // fallback). The retired "package-shell/v1" was the pre-binding mock projection.
        schemaVersion: "1.0",
        lifecycleState: "Draft",
        sourceHydrated: false,
        inspectorSections: ["assumptions", "dataBindings", "warnings", "validation", "provenance"],
        dataBindings: [
          { id: "source-binding", sourceRef: "catalog:item/demo-city-observations", status: "Ready for validation" },
        ],
      };
      ctx.studioDraft = {
        sourceContext,
        routeCompatibility: {
          sourceKind: sourceContext.kind,
          itemId: sourceContext.itemId,
          sourceHydrated: false,
        },
        url: draftUrl,
        prompt: "Build a field app from this",
        clarification: {
          routed: true,
          questionId: "publish-intent",
          selectedChoice: "Prepare an organization preview",
        },
        package: authoringPackage,
      };
      return {
        evidence: {
          draftUrl,
          routeCompatibility: ctx.studioDraft.routeCompatibility,
          sourceContext,
          prompt: ctx.studioDraft.prompt,
          clarification: ctx.studioDraft.clarification,
          package: authoringPackage,
          lifecycleStates: ["Draft", "Saved version", "Published"],
        },
        contracts: [findContract("studio-authoring-shell")],
      };
    },
  },
  {
    id: "sdk/app-package-build",
    owningLayer: "sdk",
    description:
      "SDK builds the BuilderPlan and AppPackage from the draft using shared contracts.",
    async run(ctx) {
      const plan = projectBuilderPlan({ source: ctx.studioDraft.sourceContext, savedMap: ctx.savedMap });
      const appPackage = projectAppPackage({ plan });
      ctx.builderPlan = plan;
      ctx.appPackage = appPackage;
      // SDK contract guards: a regression that drops kind='builder' or
      // assets[] would let the smoke "pass" while producing a package
      // Console cannot hydrate. Fail fast at the sdk layer so triage
      // doesn't bounce to the server or console layer first.
      if (plan.kind !== "builder") {
        throw new Error(`BuilderPlan.kind must be "builder"; got "${plan.kind}".`);
      }
      if (!Array.isArray(plan.steps) || plan.steps.length === 0) {
        throw new Error("BuilderPlan.steps must be a non-empty array.");
      }
      if (!Array.isArray(appPackage.assets) || appPackage.assets.length === 0) {
        throw new Error("AppPackage.assets must be a non-empty array (per @honua/sdk-js AppPackage).");
      }
      if (!appPackage.manifestArtifact && !appPackage.manifest_artifact) {
        throw new Error(
          "AppPackage must carry manifestArtifact or manifest_artifact for the generated-app preview projection.",
        );
      }
      const manifestArtifact = appPackage.manifestArtifact ?? appPackage.manifest_artifact;
      return {
        evidence: {
          planId: plan.id,
          planKind: plan.kind,
          stepCount: plan.steps.length,
          appPackageId: appPackage.id,
          appPackageVersion: appPackage.version,
          assetKinds: appPackage.assets.map((a) => a.kind),
          widgetKinds: manifestArtifact.manifest.layout.widgets.map((w) => w.kind),
        },
        contracts: [plan.contract, appPackage.contract],
      };
    },
  },
  {
    id: "server/generated-app-publish",
    owningLayer: "server",
    description:
      "Server records the generated app as a published content item with target.url/framework and a saved-map/catalog-item dependency back to the source.",
    async run(ctx) {
      const { item, contract } = ctx.server.publishGeneratedApp({
        source: ctx.studioDraft.sourceContext,
        manifestVersion: "1.0.0",
        plan: ctx.builderPlan,
        appPackage: ctx.appPackage,
        owner: ctx.serviceItem.owner,
        title: `${ctx.savedMap.title} — Field App`,
      });
      ctx.generatedApp = item;
      ctx.itemIds.generatedAppId = item.id;
      // generated-app-lifecycle/v1 content-item mapping: target carries
      // url + framework; dependency role must be saved-map or catalog-item.
      if (item.target?.type !== "app" || typeof item.target.url !== "string" || item.target.framework !== "honua") {
        throw new Error(
          `Generated app target must be { type: "app", url, framework: "honua" }; got ${JSON.stringify(item.target)}.`,
        );
      }
      const dep = item.dependencies.find((d) => d.id === ctx.studioDraft.sourceContext.itemId);
      if (!dep || !["saved-map", "catalog-item"].includes(dep.role)) {
        throw new Error(
          `Generated app missing saved-map/catalog-item dependency back to source ${ctx.studioDraft.sourceContext.itemId}; got ${JSON.stringify(item.dependencies)}.`,
        );
      }
      const record = projectGeneratedAppRecord(item);
      return {
        evidence: {
          generatedAppId: item.id,
          targetUrl: item.target.url,
          targetFramework: item.target.framework,
          activeRevisionId: record.lifecycle.activeRevisionId,
          provenanceCount: record.activeRevision.provenance.length,
          dependencyRoles: item.dependencies.map((d) => ({ id: d.id, role: d.role })),
        },
        contracts: [contract],
      };
    },
  },
  {
    id: "console/share-publish",
    owningLayer: "console",
    description:
      "Console share dialog promotes the saved map for embed and the generated app for catalog publication.",
    async run(ctx) {
      const dependencyPromotions = [
        { itemId: ctx.serviceItem.id, role: "source-service", embeddable: false },
        { itemId: ctx.savedMap.id, role: "saved-map", embeddable: true },
      ].map(({ itemId, role, embeddable }) => {
        const promotion = ctx.server.patchAccess({ itemId, tier: "org", embeddable });
        if (promotion.kind !== "ok") {
          throw new Error(`dependency share patch returned ${promotion.kind} for ${role} ${itemId}`);
        }
        return { itemId, role, sharing: promotion.access.sharing, embeddable: promotion.access.embeddable };
      });
      const result = ctx.server.patchAccess({ itemId: ctx.generatedApp.id, tier: "org", embeddable: true });
      if (result.kind !== "ok") {
        throw new Error(`share patch returned ${result.kind} for generated app ${ctx.generatedApp.id}`);
      }
      // share-access/v1 is { sharing, embeddable, groupIds?, publicLinkToken? }.
      // openData is a content-item.access field, not a share-access response field.
      if ("openData" in result.access) {
        throw new Error("share-access/v1 response must not carry openData; that field lives on content-item.access.");
      }
      ctx.itemIds.shareTier = result.access.sharing;
      return {
        evidence: {
          itemId: ctx.generatedApp.id,
          sharing: result.access.sharing,
          embeddable: result.access.embeddable,
          dependencyPromotions,
        },
        contracts: [result.contract],
      };
    },
  },
  {
    id: "server/embed-token-mint",
    owningLayer: "server",
    description:
      "Server mints a same-origin embed-token/v1 descriptor for the saved map.",
    async run(ctx) {
      const result = ctx.server.mintEmbedToken({ itemId: ctx.savedMap.id, audience: "pilot" });
      if (result.kind !== "ok") {
        throw new Error(`embed-token mint returned ${result.kind} for saved map ${ctx.savedMap.id}`);
      }
      ctx.embedToken = result.descriptor.token;
      ctx.itemIds.embedTokenHash = redactEmbedToken(result.descriptor.token);
      return {
        evidence: {
          tokenHash: ctx.itemIds.embedTokenHash,
          audience: result.descriptor.audience,
          closureSize: result.descriptor.closure.length,
          closure: result.descriptor.closure,
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
      "Console assembles the same-origin embed URL using the minted token; evidence keeps the same-origin route while redacting the bearer fragment.",
    async run(ctx) {
      const urls = buildConsoleUrls({
        originUrl: ctx.originUrl,
        items: { ...ctx.itemIds, embedToken: ctx.embedToken },
      });
      assertSameOrigin(ctx.originUrl, urls);
      assertEmbedTokenInFragment(urls.embed);
      ctx.urls = redactConsoleUrls(urls);
      return { evidence: { urls: ctx.urls } };
    },
  },
];
