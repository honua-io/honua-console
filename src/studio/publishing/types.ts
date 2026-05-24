import type { HonuaShareRequest } from "@honua/sdk-js/control-plane";
import type { AppPackage, ArtifactRef } from "@honua/sdk-js/operator";
import type { HonuaMapPackage } from "@honua/sdk-js/runtime";
import type { TopLevelSpec } from "vega-lite";

export type StudioPublishTarget = "map" | "dashboard" | "report" | "app";

export type ShareVisibility = Extract<HonuaShareRequest["visibility"], "private" | "workspace" | "public"> | "group" | "public-link";

export type PublishProblemKind = "missing" | "unauthorized" | "unsupported" | "invalid" | "conflict" | "server";

export interface StudioPackageRef {
  readonly packageId: string;
  readonly packageType: `${StudioPublishTarget}.package`;
  readonly schemaVersion: string;
  readonly artifactRef: string;
}

export interface StudioDependencyRef {
  readonly itemId: string;
  readonly title: string;
  readonly versionId: string;
  readonly requiredVisibility: ShareVisibility;
}

export interface StudioPackageWarning {
  readonly code: string;
  readonly message: string;
  readonly severity: "info" | "warning" | "blocking";
}

export interface StudioProvenanceRefs {
  readonly promptRef: string;
  readonly specRef: string;
  readonly planRef: string;
  readonly applyJobRef: string;
  readonly packageArtifactRefs: readonly ArtifactRef[];
  readonly sourceItemDependencyRefs: readonly StudioDependencyRef[];
  readonly modelRunRefs: readonly string[];
  readonly actor: string;
  readonly createdAt: string;
}

export interface StudioDashboardPackage {
  readonly id: string;
  readonly schemaVersion: string;
  readonly packageType: "dashboard.package";
  readonly title: string;
  readonly charts: readonly {
    readonly id: string;
    readonly title: string;
    readonly spec: TopLevelSpec;
  }[];
  readonly dataBindings: readonly string[];
}

export interface StudioReportPackage {
  readonly id: string;
  readonly schemaVersion: string;
  readonly packageType: "report.package";
  readonly title: string;
  readonly sections: readonly {
    readonly id: string;
    readonly title: string;
    readonly body: string;
  }[];
  readonly chartRefs: readonly string[];
}

export type StudioDraftPackage =
  | { readonly target: "map"; readonly package: HonuaMapPackage }
  | { readonly target: "dashboard"; readonly package: StudioDashboardPackage }
  | { readonly target: "report"; readonly package: StudioReportPackage }
  | { readonly target: "app"; readonly package: AppPackage };

export interface StudioPublishDraft {
  readonly draftId: string;
  readonly target: StudioPublishTarget;
  readonly title: string;
  readonly summary: string;
  readonly tags: readonly string[];
  readonly targetAudience: string;
  readonly packageRef: StudioPackageRef;
  readonly draftPackage: StudioDraftPackage;
  readonly dependencies: readonly StudioDependencyRef[];
  readonly warnings: readonly StudioPackageWarning[];
  readonly provenance: StudioProvenanceRefs;
  readonly rollbackTargetVersionId?: string;
}

export interface ShareEmbedSettings {
  readonly visibility: ShareVisibility;
  readonly groupIds: readonly string[];
  readonly publicLinkEnabled: boolean;
  readonly embedEnabled: boolean;
  readonly embedPolicy: "disabled" | "same-origin" | "public";
}

export interface StudioPublishReviewInput {
  readonly draftId: string;
  readonly title: string;
  readonly summary: string;
  readonly tags: readonly string[];
  readonly targetAudience: string;
  readonly versionNote: string;
  readonly share: ShareEmbedSettings;
}

export interface PublishedVersionMetadata {
  readonly versionId: string;
  readonly versionNumber: number;
  readonly packageRef: StudioPackageRef;
  readonly packageSchemaVersion: string;
  readonly createdBy: string;
  readonly createdAt: string;
  readonly changeNote: string;
  readonly rollbackFromVersionId?: string;
}

export interface PublishedItemRoutes {
  readonly canonical: string;
  readonly catalog: string;
  readonly preview: string;
  readonly share: string;
  readonly embed: string;
  readonly editInStudio: string;
}

export interface PublishedContentItem {
  readonly itemId: string;
  readonly workspaceId: string;
  readonly type: StudioPublishTarget;
  readonly title: string;
  readonly summary: string;
  readonly tags: readonly string[];
  readonly publicationState: "published";
  readonly version: PublishedVersionMetadata;
  readonly provenance: StudioProvenanceRefs;
  readonly share: ShareEmbedSettings;
  readonly routes: PublishedItemRoutes;
}

export interface ReopenedStudioArtifact {
  readonly item: PublishedContentItem;
  readonly draftPackage: StudioDraftPackage;
  readonly editContext: {
    readonly draftId: string;
    readonly sourceVersionId: string;
    readonly promptRef: string;
    readonly planRef: string;
    readonly packageRef: StudioPackageRef;
    readonly loadedWithoutGeneration: true;
  };
}

export interface StudioPublishingClient {
  listDrafts(): Promise<readonly StudioPublishDraft[]>;
  getDraft(draftId: string): Promise<StudioPublishDraft>;
  publishDraft(input: StudioPublishReviewInput): Promise<PublishedContentItem>;
  getPublishedItem(itemId: string): Promise<PublishedContentItem>;
  reopenPublishedItem(itemId: string): Promise<ReopenedStudioArtifact>;
}

export class StudioPublishingError extends Error {
  readonly kind: PublishProblemKind;

  constructor(kind: PublishProblemKind, message: string) {
    super(message);
    this.name = "StudioPublishingError";
    this.kind = kind;
  }
}

export function isStudioPublishingError(error: unknown): error is StudioPublishingError {
  return error instanceof StudioPublishingError;
}
