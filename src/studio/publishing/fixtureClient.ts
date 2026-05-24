import {
  DEFAULT_SHARE_SETTINGS,
  STUDIO_PUBLISH_DRAFTS
} from "./fixtures.js";
import { publishedItemRoutes } from "./routes.js";
import type {
  PublishedContentItem,
  ReopenedStudioArtifact,
  ShareEmbedSettings,
  ShareVisibility,
  StudioPublishDraft,
  StudioPublishingClient,
  StudioPublishReviewInput
} from "./types.js";
import { StudioPublishingError } from "./types.js";

const WORKSPACE_ID = "workspace-honua-ops";
const PUBLISHED_STORAGE_KEY = "honua-console:studio-published-items";

const ITEM_IDS: Record<StudioPublishDraft["draftId"], string> = {
  "draft-map-operations": "console-map-operations",
  "draft-dashboard-operations": "console-dashboard-operations",
  "draft-report-operations": "console-report-operations",
  "draft-app-operations": "console-app-operations",
  "draft-map-conflict": "console-map-conflict"
};

const VISIBILITY_RANK: Record<ShareVisibility, number> = {
  private: 0,
  workspace: 1,
  group: 2,
  "public-link": 3,
  public: 4
};

export class FixtureStudioPublishingClient implements StudioPublishingClient {
  private readonly drafts = new Map(STUDIO_PUBLISH_DRAFTS.map((draft) => [draft.draftId, draft]));
  private readonly published = new Map<string, PublishedContentItem>();
  private generationCalls = 0;

  constructor() {
    this.restorePublishedItems();
  }

  async listDrafts(): Promise<readonly StudioPublishDraft[]> {
    return [...this.drafts.values()];
  }

  async getDraft(draftId: string): Promise<StudioPublishDraft> {
    const draft = this.drafts.get(draftId);
    if (!draft) {
      throw new StudioPublishingError("missing", `Studio draft ${draftId} was not found.`);
    }
    return draft;
  }

  async publishDraft(input: StudioPublishReviewInput): Promise<PublishedContentItem> {
    const draft = await this.getDraft(input.draftId);
    this.assertCanPublish(draft, input);

    const itemId = ITEM_IDS[draft.draftId];
    const existing = this.published.get(itemId);
    const versionNumber = existing ? existing.version.versionNumber + 1 : 1;
    const publishedAt = "2026-05-23T22:15:00.000Z";
    const item: PublishedContentItem = {
      itemId,
      workspaceId: WORKSPACE_ID,
      type: draft.target,
      title: input.title.trim(),
      summary: input.summary.trim(),
      tags: input.tags.map((tag) => tag.trim()).filter(Boolean),
      publicationState: "published",
      version: {
        versionId: `${itemId}-v${versionNumber}`,
        versionNumber,
        packageRef: draft.packageRef,
        packageSchemaVersion: draft.packageRef.schemaVersion,
        createdBy: draft.provenance.actor,
        createdAt: publishedAt,
        changeNote: input.versionNote.trim(),
        rollbackFromVersionId: draft.rollbackTargetVersionId
      },
      provenance: {
        ...draft.provenance,
        createdAt: publishedAt
      },
      share: normalizeShareSettings(input.share),
      routes: publishedItemRoutes(draft.target, itemId)
    };
    this.published.set(itemId, item);
    this.persistPublishedItems();
    return item;
  }

  async getPublishedItem(itemId: string): Promise<PublishedContentItem> {
    const item = this.published.get(itemId);
    if (!item) {
      throw new StudioPublishingError("missing", `Published item ${itemId} was not found.`);
    }
    return item;
  }

  async reopenPublishedItem(itemId: string): Promise<ReopenedStudioArtifact> {
    const item = await this.getPublishedItem(itemId);
    const draft = [...this.drafts.values()].find((candidate) => candidate.packageRef.packageId === item.version.packageRef.packageId);
    if (!draft) {
      throw new StudioPublishingError("missing", `Package ${item.version.packageRef.packageId} was not found.`);
    }

    return {
      item,
      draftPackage: draft.draftPackage,
      editContext: {
        draftId: draft.draftId,
        sourceVersionId: item.version.versionId,
        promptRef: item.provenance.promptRef,
        planRef: item.provenance.planRef,
        packageRef: item.version.packageRef,
        loadedWithoutGeneration: true
      }
    };
  }

  getGenerationCallCount(): number {
    return this.generationCalls;
  }

  reset(): void {
    this.published.clear();
    this.generationCalls = 0;
    this.clearPersistedPublishedItems();
  }

  private assertCanPublish(draft: StudioPublishDraft, input: StudioPublishReviewInput): void {
    if (!input.title.trim()) {
      throw new StudioPublishingError("invalid", "A title is required before publishing.");
    }

    if (input.share.visibility === "group" && normalizedGroupIds(input.share.groupIds).length === 0) {
      throw new StudioPublishingError("invalid", "Choose at least one group before publishing with group visibility.");
    }

    const blockingWarning = draft.warnings.find((warning) => warning.severity === "blocking");
    if (blockingWarning) {
      throw new StudioPublishingError("conflict", blockingWarning.message);
    }

    const requestedRank = VISIBILITY_RANK[input.share.visibility];
    const narrowDependency = draft.dependencies.find(
      (dependency) => requestedRank > VISIBILITY_RANK[dependency.requiredVisibility]
    );
    if (narrowDependency) {
      throw new StudioPublishingError(
        "conflict",
        `${narrowDependency.title} is ${narrowDependency.requiredVisibility}; publish with narrower access or update the source item.`
      );
    }
  }

  private restorePublishedItems(): void {
    const storage = browserSessionStorage();
    if (!storage) return;

    const raw = storage.getItem(PUBLISHED_STORAGE_KEY);
    if (!raw) return;

    try {
      const items = JSON.parse(raw) as readonly PublishedContentItem[];
      for (const item of items) {
        if (item?.itemId) {
          this.published.set(item.itemId, item);
        }
      }
    } catch {
      storage.removeItem(PUBLISHED_STORAGE_KEY);
    }
  }

  private persistPublishedItems(): void {
    const storage = browserSessionStorage();
    if (!storage) return;
    storage.setItem(PUBLISHED_STORAGE_KEY, JSON.stringify([...this.published.values()]));
  }

  private clearPersistedPublishedItems(): void {
    const storage = browserSessionStorage();
    if (!storage) return;
    storage.removeItem(PUBLISHED_STORAGE_KEY);
  }
}

function browserSessionStorage(): Storage | undefined {
  if (typeof window === "undefined") return undefined;

  try {
    return window.sessionStorage;
  } catch {
    return undefined;
  }
}

function normalizeShareSettings(share: ShareEmbedSettings): ShareEmbedSettings {
  if (share.visibility === "private") {
    return {
      ...DEFAULT_SHARE_SETTINGS,
      groupIds: []
    };
  }

  return {
    visibility: share.visibility,
    groupIds: share.visibility === "group" ? normalizedGroupIds(share.groupIds) : [],
    publicLinkEnabled: share.visibility === "public-link" || share.publicLinkEnabled,
    embedEnabled: share.embedEnabled,
    embedPolicy: share.embedEnabled ? (share.visibility === "public" || share.visibility === "public-link" ? "public" : "same-origin") : "disabled"
  };
}

function normalizedGroupIds(groupIds: readonly string[]): readonly string[] {
  return groupIds.map((groupId) => groupId.trim()).filter(Boolean);
}

export const studioPublishingClient = new FixtureStudioPublishingClient();
