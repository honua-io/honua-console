import { FixtureStudioPublishingClient } from "./fixtureClient.js";
import { previewRoute } from "./routes.js";
import { StudioPublishingError } from "./types.js";
import type { ShareEmbedSettings, StudioPublishTarget } from "./types.js";

const PUBLISH_CASES: readonly {
  readonly draftId: string;
  readonly itemId: string;
  readonly target: StudioPublishTarget;
}[] = [
  { draftId: "draft-map-operations", itemId: "console-map-operations", target: "map" },
  { draftId: "draft-dashboard-operations", itemId: "console-dashboard-operations", target: "dashboard" },
  { draftId: "draft-report-operations", itemId: "console-report-operations", target: "report" },
  { draftId: "draft-app-operations", itemId: "console-app-operations", target: "app" }
];

const WORKSPACE_EMBED_SHARE: ShareEmbedSettings = {
  visibility: "workspace",
  groupIds: [],
  publicLinkEnabled: false,
  embedEnabled: true,
  embedPolicy: "same-origin"
};

function publishInput(draftId: string) {
  return {
    draftId,
    title: `Published ${draftId}`,
    summary: `Summary for ${draftId}`,
    tags: ["operations", "studio"],
    targetAudience: "Console builders",
    versionNote: "Initial publish from test",
    share: WORKSPACE_EMBED_SHARE
  };
}

describe("FixtureStudioPublishingClient", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
  });

  it.each(PUBLISH_CASES)(
    "publishes $target drafts as versioned Console content items with stable routes and provenance",
    async ({ draftId, itemId, target }) => {
      const client = new FixtureStudioPublishingClient();

      const item = await client.publishDraft(publishInput(draftId));

      expect(item).toMatchObject({
        itemId,
        type: target,
        publicationState: "published",
        routes: {
          canonical: `/catalog/${itemId}`,
          catalog: `/catalog/${itemId}`,
          preview: previewRoute(target, itemId),
          share: `/share/${itemId}`,
          embed: `/embed/${itemId}`,
          editInStudio: `/studio/items/${itemId}/edit`
        },
        share: {
          visibility: "workspace",
          embedEnabled: true,
          embedPolicy: "same-origin"
        }
      });
      expect(item.version).toMatchObject({
        versionId: `${itemId}-v1`,
        versionNumber: 1,
        packageSchemaVersion: "1.0.0",
        changeNote: "Initial publish from test"
      });
      expect(item.version.packageRef.packageType).toBe(`${target}.package`);
      expect(item.provenance).toMatchObject({
        promptRef: `prompt://${target}/operations-dashboard`,
        specRef: `spec://${target}/operations-dashboard`,
        planRef: `plan://${target}/operations-dashboard`,
        applyJobRef: `job://apply-${target}-operations`,
        actor: "builder@honua.test"
      });
      expect(item.provenance.packageArtifactRefs).toHaveLength(1);
      expect(item.provenance.sourceItemDependencyRefs).toHaveLength(2);
    }
  );

  it("reopens the active package revision in Studio without invoking generation", async () => {
    const client = new FixtureStudioPublishingClient();
    const item = await client.publishDraft(publishInput("draft-app-operations"));

    const reopened = await client.reopenPublishedItem(item.itemId);

    expect(reopened.item.itemId).toBe(item.itemId);
    expect(reopened.draftPackage.target).toBe("app");
    expect(reopened.editContext).toMatchObject({
      draftId: "draft-app-operations",
      sourceVersionId: "console-app-operations-v1",
      promptRef: item.provenance.promptRef,
      planRef: item.provenance.planRef,
      packageRef: item.version.packageRef,
      loadedWithoutGeneration: true
    });
    expect(client.getGenerationCallCount()).toBe(0);
  });

  it("appends versions on republish and preserves rollback provenance", async () => {
    const client = new FixtureStudioPublishingClient();

    await client.publishDraft(publishInput("draft-app-operations"));
    const updated = await client.publishDraft({
      ...publishInput("draft-app-operations"),
      versionNote: "Updated app publication"
    });

    expect(updated.version).toMatchObject({
      versionId: "console-app-operations-v2",
      versionNumber: 2,
      changeNote: "Updated app publication",
      rollbackFromVersionId: "app-package-operations-v0"
    });
  });

  it("blocks dependency closure conflicts through the shared problem taxonomy", async () => {
    const client = new FixtureStudioPublishingClient();

    let conflict: unknown;
    try {
      await client.publishDraft({
        ...publishInput("draft-map-operations"),
        share: {
          visibility: "public-link",
          groupIds: [],
          publicLinkEnabled: true,
          embedEnabled: true,
          embedPolicy: "public"
        }
      });
    } catch (error) {
      conflict = error;
    }

    expect(conflict).toBeInstanceOf(StudioPublishingError);
    expect((conflict as StudioPublishingError).kind).toBe("conflict");
    expect((conflict as Error).message).toContain("publish with narrower access");
  });

  it("requires at least one group id for group visibility", async () => {
    const client = new FixtureStudioPublishingClient();

    let invalid: unknown;
    try {
      await client.publishDraft({
        ...publishInput("draft-map-operations"),
        share: {
          visibility: "group",
          groupIds: ["   "],
          publicLinkEnabled: false,
          embedEnabled: false,
          embedPolicy: "disabled"
        }
      });
    } catch (error) {
      invalid = error;
    }

    expect(invalid).toBeInstanceOf(StudioPublishingError);
    expect((invalid as StudioPublishingError).kind).toBe("invalid");
    expect((invalid as Error).message).toContain("Choose at least one group");
  });

  it("treats group sharing as wider than workspace dependencies", async () => {
    const client = new FixtureStudioPublishingClient();

    let conflict: unknown;
    try {
      await client.publishDraft({
        ...publishInput("draft-map-operations"),
        share: {
          visibility: "group",
          groupIds: ["group-emergency-ops"],
          publicLinkEnabled: false,
          embedEnabled: false,
          embedPolicy: "disabled"
        }
      });
    } catch (error) {
      conflict = error;
    }

    expect(conflict).toBeInstanceOf(StudioPublishingError);
    expect((conflict as StudioPublishingError).kind).toBe("conflict");
    expect((conflict as Error).message).toContain("Incident response layer is workspace");
  });
});
