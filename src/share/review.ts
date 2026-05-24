import type { Dependency, DependencyNode, GetDependenciesResponse, Sharing } from "../contracts/content-item.js";
import { compareTier, evaluateShareEscalation } from "./policy.js";
import type { ClosureNode, DependencyRole, DependencyType, ShareAccess, SharingTier } from "./types.js";

export type ShareReviewBlockerReason = "narrower-tier" | "unauthorized" | "missing" | "truncated";
export type ShareReviewNoticeReason = "unsupported";

export interface ShareReviewBlocker {
  readonly id: string;
  readonly type: DependencyType;
  readonly role: DependencyRole;
  readonly title?: string;
  readonly sharing?: SharingTier;
  readonly reason: ShareReviewBlockerReason;
}

export interface ShareReviewNotice {
  readonly id: string;
  readonly type: DependencyType;
  readonly role: DependencyRole;
  readonly reason: ShareReviewNoticeReason;
}

export type ShareReviewOutcome =
  | { readonly kind: "ok"; readonly widening: boolean; readonly notices: readonly ShareReviewNotice[] }
  | {
      readonly kind: "blocked";
      readonly widening: true;
      readonly blockers: readonly ShareReviewBlocker[];
      readonly notices: readonly ShareReviewNotice[];
    };

export function reviewShareChange(input: {
  readonly current: SharingTier;
  readonly proposed: SharingTier;
  readonly closure: GetDependenciesResponse;
}): ShareReviewOutcome {
  const notices = input.closure.unsupported.map((dependency) => ({
    ...dependencyToBlockerBase(dependency),
    reason: "unsupported" as const,
  }));
  const widening = compareTier(input.proposed, input.current) > 0;
  if (!widening) return { kind: "ok", widening: false, notices };

  const closureNodes = input.closure.nodes.map(dependencyNodeToClosureNode);
  const outcome = evaluateShareEscalation({
    current: input.current,
    proposed: input.proposed,
    closure: closureNodes,
  });

  const blockers: ShareReviewBlocker[] =
    outcome.kind === "blocked"
      ? outcome.blockers.map((node) => ({
          id: node.id,
          type: node.type,
          role: node.role,
          title: node.title,
          sharing: node.access && node.access !== "unsupported" ? node.access.sharing : undefined,
          reason: "narrower-tier" as const,
        }))
      : [];

  for (const dependency of input.closure.unauthorized) {
    blockers.push({ ...dependencyToBlockerBase(dependency), reason: "unauthorized" });
  }
  for (const dependency of input.closure.missing) {
    blockers.push({ ...dependencyToBlockerBase(dependency), reason: "missing" });
  }
  if (input.closure.truncated) {
    blockers.push({
      id: "dependency-closure",
      type: "map",
      role: "dependencyClosure",
      title: "Dependency closure",
      reason: "truncated",
    });
  }

  if (blockers.length > 0) {
    return { kind: "blocked", widening: true, blockers, notices };
  }
  return { kind: "ok", widening: true, notices };
}

export function dependencyNodeToClosureNode(node: DependencyNode): ClosureNode {
  return {
    id: node.id,
    type: toDependencyType(node.type),
    role: toDependencyRole(node.role),
    title: node.summary?.title,
    access: node.summary ? accessFromSharing(node.summary.sharing) : "unsupported",
  };
}

export function accessFromSharing(sharing: Sharing): ShareAccess {
  return { sharing, embeddable: false };
}

function dependencyToBlockerBase(dependency: Dependency): Omit<ShareReviewBlocker, "reason"> {
  return {
    id: dependency.id,
    type: toDependencyType(dependency.type),
    role: toDependencyRole(dependency.role),
  };
}

function toDependencyType(type: string): DependencyType {
  switch (type) {
    case "service":
    case "layer":
    case "map":
    case "scene":
    case "app":
    case "document":
    case "external-url":
    case "style":
    case "popup":
      return type;
    default:
      return "service";
  }
}

function toDependencyRole(role: string): DependencyRole {
  return role as DependencyRole;
}
