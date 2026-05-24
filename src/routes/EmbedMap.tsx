import { useEffect, useState } from "react";
import { useLocation, useParams } from "react-router-dom";

import { useSession } from "../auth/SessionContext";
import { contentItemToClosureItem } from "../catalog/client";
import { getDefaultCatalogClient, getDefaultCatalogFixtureItems } from "../catalog/default-client";
import { CatalogError, type ContentItem } from "../contracts/content-item";
import { type EmbedAuthPosture, type RedeemEmbedToken, prepareEmbedAuth } from "../embed/auth";
import { resolveEmbedAuthorization } from "../embed/permissions";
import { type SavedMapItem, loadFixtureSavedMapForViewer } from "../saved-maps";
import { defaultEmbedToken } from "../share/tokens";
import type { ClosureItem, EmbedTokenDescriptor, ShareAccess, SharingTier } from "../share/types";
import { EmptyState, type EmptyStateKind } from "../ui/EmptyState";
import { MapViewerSurface } from "./Maps";

type EmbedTarget =
  | { kind: "saved-map"; routeId: string; item: SavedMapItem }
  | { kind: "catalog-map"; routeId: string; item: ContentItem };

type EmbedGateState =
  | { kind: "loading" }
  | { kind: "ready"; target: EmbedTarget }
  | { kind: "fallback"; emptyKind: EmptyStateKind; title?: string; message?: string };

export default function EmbedMap(): JSX.Element {
  const { mapId } = useParams<{ mapId: string }>();
  const location = useLocation();
  const { session } = useSession();
  const hasSession = session.status === "authenticated";
  const actorId = hasSession ? session.user.id : null;
  const [state, setState] = useState<EmbedGateState>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;

    async function resolve(): Promise<void> {
      if (!mapId) {
        setState({
          kind: "fallback",
          emptyKind: "missing",
          message: "The embed URL is missing a map id.",
        });
        return;
      }

      if (session.status === "loading") {
        setState({ kind: "loading" });
        return;
      }

      setState({ kind: "loading" });
      const target = await loadEmbedTarget(mapId);
      if (cancelled) return;

      if (target.kind === "fallback") {
        setState(target);
        return;
      }

      const auth = await prepareEmbedAuth({
        fragment: location.hash,
        hasSession,
        redeemEmbedToken: redeemFixtureEmbedToken(target.target),
      });
      if (cancelled) return;

      if (auth.fallback) {
        setState(embedAuthFallback(auth.fallback));
        return;
      }

      if (auth.descriptor && !descriptorMatchesTarget(auth.descriptor, target.target, mapId)) {
        setState({
          kind: "fallback",
          emptyKind: "unauthorized",
          title: "Embed token is scoped to a different map",
          message: "Request a fresh embed snippet for this map.",
        });
        return;
      }

      const authorization = authorizeEmbedTarget({
        target: target.target,
        posture: auth.posture,
        hasSession,
        actorId,
      });
      if (!authorization.rootReadable) {
        setState(rootBlockedFallback(authorization.rootBlockedBy));
        return;
      }

      setState({ kind: "ready", target: target.target });
    }

    void resolve();

    return () => {
      cancelled = true;
    };
  }, [actorId, hasSession, location.hash, mapId, session.status]);

  if (state.kind === "loading") {
    return (
      <main className="hc-embed-empty" data-testid="embed-empty-state">
        <EmptyState kind="loading" title="Loading embed..." />
      </main>
    );
  }

  if (state.kind === "fallback") {
    return (
      <main className="hc-embed-empty" data-testid="embed-empty-state">
        <EmptyState kind={state.emptyKind} title={state.title} message={state.message} />
      </main>
    );
  }

  return state.target.kind === "catalog-map" ? (
    <MapViewerSurface mode="embed" itemId={state.target.item.id} />
  ) : (
    <MapViewerSurface mode="embed" savedMapId={state.target.routeId} />
  );
}

async function loadEmbedTarget(
  routeId: string,
): Promise<{ kind: "ok"; target: EmbedTarget } | Extract<EmbedGateState, { kind: "fallback" }>> {
  const savedMap = loadFixtureSavedMapForViewer(routeId);
  if (savedMap.status === "ok") {
    return { kind: "ok", target: { kind: "saved-map", routeId, item: savedMap.item } };
  }
  if (savedMap.status === "unsupported") {
    return {
      kind: "fallback",
      emptyKind: "unsupported",
      message: savedMap.reason,
    };
  }

  try {
    const item = await getDefaultCatalogClient().getItem(routeId);
    if (item.type !== "map") {
      return {
        kind: "fallback",
        emptyKind: "unsupported",
        title: "Only maps can be embedded",
        message: "Open this item from the catalog instead.",
      };
    }
    return { kind: "ok", target: { kind: "catalog-map", routeId, item } };
  } catch (error: unknown) {
    if (error instanceof CatalogError) {
      return {
        kind: "fallback",
        emptyKind: error.code === "unauthorized" ? "unauthorized" : error.code === "missing" ? "missing" : "error",
        message: error.message,
      };
    }
    return {
      kind: "fallback",
      emptyKind: "error",
      message: error instanceof Error ? error.message : String(error),
    };
  }
}

function authorizeEmbedTarget(input: {
  target: EmbedTarget;
  posture: EmbedAuthPosture;
  hasSession: boolean;
  actorId: string | null;
}): ReturnType<typeof resolveEmbedAuthorization> {
  const rootAccess = toShareAccess(input.target.item);
  if (input.posture.kind === "token") {
    return {
      rootReadable: rootAccess.embeddable,
      rootBlockedBy: rootAccess.embeddable ? null : "embeddable",
      cells: [],
      hasUnauthorizedDeps: false,
    };
  }

  const viewerTier = viewerTierFor(rootAccess.sharing, input.hasSession);
  const rootAccessForViewer =
    input.hasSession && input.actorId === input.target.item.owner.id && rootAccess.sharing === "private"
      ? { ...rootAccess, sharing: "org" as const }
      : rootAccess;

  return resolveEmbedAuthorization({
    rootId: input.target.item.id,
    rootAccess: rootAccessForViewer,
    viewerTier,
    closure: closureItemsFor(input.target),
  });
}

function closureItemsFor(target: EmbedTarget): ClosureItem[] {
  const fixtureItems = getDefaultCatalogFixtureItems().map(contentItemToClosureItem);
  return [...fixtureItems, contentItemToClosureItem(target.item as ContentItem)];
}

function toShareAccess(item: ContentItem | SavedMapItem): ShareAccess {
  return {
    sharing: item.access.sharing,
    embeddable: item.access.embeddable,
  };
}

function viewerTierFor(rootTier: SharingTier, hasSession: boolean): SharingTier {
  if (hasSession) return "org";
  return rootTier === "public-link" ? "public-link" : "public";
}

function redeemFixtureEmbedToken(target: EmbedTarget): RedeemEmbedToken {
  return async (token) => {
    if (token === "fixture-expired" || token === "expired") return { kind: "expired" };
    if (token === "fixture-error") return { kind: "error", message: "fixture token verification failed" };
    if (token !== defaultEmbedToken(target.item.id) && token !== defaultEmbedToken(target.routeId)) {
      return { kind: "invalid" };
    }
    return {
      kind: "ok",
      descriptor: {
        token,
        itemId: target.item.id,
        audience: "pilot",
        expiresAt: "2099-01-01T00:00:00.000Z",
        closure: [target.item.id, ...target.item.dependencies.map((dependency) => dependency.id)],
      },
    };
  };
}

function descriptorMatchesTarget(descriptor: EmbedTokenDescriptor, target: EmbedTarget, routeId: string): boolean {
  return descriptor.itemId === target.item.id || descriptor.itemId === routeId;
}

function embedAuthFallback(kind: "empty" | "unauthorized" | "unsupported" | "missing" | "error"): EmbedGateState {
  if (kind === "unauthorized") {
    return {
      kind: "fallback",
      emptyKind: "unauthorized",
      title: "Embed token expired or invalid",
      message: "Request a fresh embed snippet from the item owner.",
    };
  }
  if (kind === "unsupported") {
    return {
      kind: "fallback",
      emptyKind: "unsupported",
      title: "Embed token verification is unavailable",
      message: "This environment cannot verify the embed token yet.",
    };
  }
  return { kind: "fallback", emptyKind: kind };
}

function rootBlockedFallback(
  blockedBy: ReturnType<typeof resolveEmbedAuthorization>["rootBlockedBy"],
): Extract<EmbedGateState, { kind: "fallback" }> {
  if (blockedBy === "embeddable") {
    return {
      kind: "fallback",
      emptyKind: "unsupported",
      title: "Embeds are disabled for this map",
      message: "The owner can enable embeds from the sharing panel.",
    };
  }
  if (blockedBy === "unsupported") {
    return {
      kind: "fallback",
      emptyKind: "unsupported",
      title: "This map cannot be embedded yet",
      message: "One or more required sharing contracts are unavailable for this item.",
    };
  }
  return {
    kind: "fallback",
    emptyKind: "unauthorized",
    title: "This map is not shared with the embed audience",
    message: "Ask the owner for a public link, public share, or valid embed token.",
  };
}
