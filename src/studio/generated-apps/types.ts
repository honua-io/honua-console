import type { AppPackage, ArtifactRef, BuilderPlan, ProvenanceRecord } from "@honua/sdk-js/operator";

import type { ContentItem, ContentItemSummary, ItemType, Owner, Ulid } from "../../transitional/content-item.js";

export const GENERATED_APP_EXTENSION = "honua-generated-app" as const;
export const GENERATED_APP_EXTENSION_SCHEMA = "honua-generated-app-lifecycle/v1" as const;

export type GeneratedAppLifecycleState = "draft" | "published" | "unsupported";
export type GeneratedAppSourceKind = "saved-map" | "catalog-item";

export interface GeneratedAppSourceRef {
  readonly kind: GeneratedAppSourceKind;
  readonly itemId: Ulid;
  readonly itemType: ItemType;
  readonly title: string;
}

export interface GeneratedAppArtifactRef extends ArtifactRef {
  readonly url?: string;
}

export interface GeneratedAppPlanRef {
  readonly id: string;
  readonly artifact?: GeneratedAppArtifactRef | null;
  readonly warnings: readonly string[];
}

export interface GeneratedAppServerJobRef {
  readonly id: string;
  readonly status: "queued" | "running" | "succeeded" | "failed" | "cancelled";
  readonly url?: string;
}

export interface GeneratedAppRevision {
  readonly id: string;
  readonly sequence: number;
  readonly label: string;
  readonly createdAt: string;
  readonly actor: string;
  readonly manifestVersion: string;
  readonly buildSpecRef: GeneratedAppArtifactRef;
  readonly planRef: GeneratedAppPlanRef;
  readonly appPackageRef: GeneratedAppArtifactRef;
  readonly manifestArtifact: GeneratedAppArtifactRef;
  readonly serverJob: GeneratedAppServerJobRef | null;
  readonly provenance: readonly ProvenanceRecord[];
  readonly previewUrl: string;
  readonly rollbackOf: string | null;
}

export interface GeneratedAppLifecycleExtension {
  readonly schema: typeof GENERATED_APP_EXTENSION_SCHEMA;
  readonly state: GeneratedAppLifecycleState;
  readonly source: GeneratedAppSourceRef;
  readonly activeRevisionId: string;
  readonly revisions: readonly GeneratedAppRevision[];
  readonly unsupportedReason: string | null;
}

export interface GeneratedAppSourceInput {
  readonly kind: GeneratedAppSourceKind;
  readonly item: Pick<ContentItem, "id" | "type" | "title" | "extent" | "license" | "attribution" | "preview">;
}

export interface GeneratedAppRevisionInput {
  readonly actor: string;
  readonly label?: string;
  readonly manifestVersion: string;
  readonly buildSpecRef: GeneratedAppArtifactRef;
  readonly plan: Pick<BuilderPlan, "id"> & { readonly warnings?: readonly string[] };
  readonly planArtifact?: GeneratedAppArtifactRef | null;
  readonly appPackage: AppPackage;
  readonly manifestArtifact: GeneratedAppArtifactRef;
  readonly serverJob?: GeneratedAppServerJobRef | null;
  readonly provenance?: readonly ProvenanceRecord[];
}

export interface SaveGeneratedAppDraftInput extends GeneratedAppRevisionInput {
  readonly id?: Ulid;
  readonly slug?: string | null;
  readonly title: string;
  readonly summary: string;
  readonly description: string;
  readonly tags?: readonly string[];
  readonly owner: Owner;
  readonly source: GeneratedAppSourceInput;
  readonly unsupportedReason?: string | null;
}

export interface GeneratedAppLifecycleRecord {
  readonly item: ContentItem;
  readonly summary: ContentItemSummary;
  readonly lifecycle: GeneratedAppLifecycleExtension;
  readonly activeRevision: GeneratedAppRevision;
}

export interface GeneratedAppPreviewDescriptor extends GeneratedAppLifecycleRecord {
  readonly previewUrl: string;
}
