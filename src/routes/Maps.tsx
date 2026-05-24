import { useEffect, useMemo, useRef } from "react";
import { Link, useLocation, useParams } from "react-router-dom";

import { useSession } from "../auth/SessionContext";
import { canSeeOperatorLinks } from "../auth/permissions";
import { formatDate } from "../catalog/components/format";
import { parseEmbedParams } from "../embed/route";
import { STYLE_EDITOR_DEMO_MAP_ID, type SavedMapItem, loadFixtureSavedMapForViewer } from "../saved-maps";
import { EmptyState } from "../shell/EmptyState";
import { TypePill } from "../ui/TypePill";
import { VisibilityPill } from "../ui/VisibilityPill";
import { initMapViewer } from "../viewer/init";
import { createPortalViewerSdkFeatureLoader } from "../viewer/sdk-feature-loader";
import { createFixturePortalViewerSdkFetch } from "../viewer/sdk-fixtures";
import "./maps.css";

interface MapViewerSurfaceProps {
  mode?: "viewer" | "embed";
  itemId?: string;
  savedMapId?: string;
}

export function MapViewerSurface({
  mode = "viewer",
  itemId,
  savedMapId: savedMapIdOverride,
}: MapViewerSurfaceProps): JSX.Element {
  const rootRef = useRef<HTMLDivElement>(null);
  const params = useParams();
  const location = useLocation();
  const { session } = useSession();
  const routeMapId = params["mapId"];
  const sourceItemId = useMemo(() => {
    if (itemId) return itemId;
    if (routeMapId !== "new") return undefined;
    const from = new URLSearchParams(location.search).get("from")?.trim();
    return from || undefined;
  }, [itemId, location.search, routeMapId]);
  const savedMapId = savedMapIdOverride ?? (sourceItemId ? undefined : routeMapId === "new" ? undefined : routeMapId);
  const embedParams = useMemo(
    () => (mode === "embed" ? parseEmbedParams(location.search) : null),
    [location.search, mode],
  );
  const canModerateAnnotations = useMemo(() => {
    if (mode === "embed" || session.status !== "authenticated" || !savedMapId) return false;
    const loaded = loadFixtureSavedMapForViewer(savedMapId);
    return loaded.status === "ok" && (loaded.item.owner.id === session.user.id || canSeeOperatorLinks(session));
  }, [mode, savedMapId, session]);
  const sdkFeatureLoader = useMemo(() => {
    const browserFetch = typeof globalThis.fetch === "function" ? globalThis.fetch.bind(globalThis) : undefined;
    return createPortalViewerSdkFeatureLoader({
      session,
      fetchFn: createFixturePortalViewerSdkFetch(browserFetch),
    });
  }, [session]);

  useEffect(() => {
    const root = rootRef.current;
    if (!root) return;
    const handle = initMapViewer(root, {
      savedMapId,
      itemId: sourceItemId,
      mode,
      actorId: session.status === "authenticated" ? session.user.id : undefined,
      actorName: session.status === "authenticated" ? session.user.displayName : undefined,
      canModerateAnnotations,
      embedParams,
      sdkFeatureLoader,
    });
    return () => {
      handle.dispose();
    };
  }, [savedMapId, sourceItemId, mode, session, canModerateAnnotations, embedParams, sdkFeatureLoader]);

  return (
    <div
      ref={rootRef}
      className={mode === "embed" ? "hc-mv hc-mv--embed" : "hc-mv"}
      data-testid={mode === "embed" ? "embed-map-viewer-root" : "map-viewer-root"}
      data-chrome={embedParams?.chrome ?? "full"}
      data-legend={embedParams?.legend === false ? "off" : "on"}
      data-zoom={embedParams?.zoom === false ? "off" : "on"}
    >
      <h1 className="hc-visually-hidden">Maps</h1>
      <header className="hc-mv__header" data-testid="viewer-header">
        <div className="hc-mv__title" data-portal-item-title>
          Loading portal item…
        </div>
        {mode === "viewer" ? (
          <>
            <button
              type="button"
              className="hc-mv__action"
              data-style-editor-button
              title="Open the self-hosted Maputnik style editor"
              hidden
            >
              Edit style
            </button>
            <button
              type="button"
              className="hc-mv__action"
              data-share-url-button
              title="Copy a sharable URL of the current view"
            >
              Copy view URL
            </button>
          </>
        ) : (
          <button type="button" className="hc-mv__action" data-share-url-button hidden>
            Copy view URL
          </button>
        )}
      </header>
      <div className="hc-mv__main">
        <aside className="hc-mv__sidebar" data-testid="viewer-sidebar">
          <section className="hc-mv__section" data-testid="metadata-panel">
            <h2 className="hc-mv__section-title">Layer metadata</h2>
            <div className="metadata-grid" data-metadata-grid />
          </section>
          <section className="hc-mv__section" data-testid="layer-list">
            <h2 className="hc-mv__section-title">Layers</h2>
            <ul className="layer-list" data-layer-list />
          </section>
          <section className="hc-mv__section" data-testid="feature-detail">
            <h2 className="hc-mv__section-title">Feature detail</h2>
            <div className="feature-detail" data-feature-detail>
              <p className="empty-copy">Click a feature on the map to inspect its attributes.</p>
            </div>
          </section>
          <section className="hc-mv__section" data-testid="annotation-panel">
            <h2 className="hc-mv__section-title">Annotations</h2>
            <div className="annotation-panel" data-annotation-panel />
          </section>
          {mode === "viewer" ? (
            <section className="hc-mv__section" data-testid="collaboration-panel">
              <h2 className="hc-mv__section-title">Collaboration</h2>
              <div className="collaboration-panel" data-collaboration-panel />
            </section>
          ) : null}
        </aside>
        <section className="hc-mv__map-shell">
          <div className="hc-mv__map" data-map-container data-testid="map-container" />
          <output className="hc-mv__status" data-map-status aria-live="polite">
            Loading map…
          </output>
        </section>
      </div>
      <section className="hc-mv__table" data-testid="feature-table">
        <header className="hc-mv__table-header">
          <h2 className="hc-mv__section-title">Tabular detail</h2>
          <div className="hc-mv__table-meta">
            <span data-table-layer-label>Select a layer</span>
            <span data-table-row-count />
          </div>
        </header>
        <div className="hc-mv__table-scroll">
          <table className="feature-table">
            <thead data-feature-table-head />
            <tbody data-feature-table-body />
          </table>
        </div>
      </section>
      {mode === "viewer" ? (
        <section className="hc-mv__style-editor" data-style-editor-panel hidden aria-label="Style editor">
          <header className="hc-mv__style-editor-header">
            <div>
              <h2 className="hc-mv__style-editor-title">Maputnik style editor</h2>
              <div className="hc-mv__style-editor-meta">
                <label>
                  <span className="hc-visually-hidden">Style target</span>
                  <select data-style-target-select aria-label="Style target" />
                </label>
                <span className="hc-mv__style-origin" data-style-origin />
              </div>
            </div>
            <button
              type="button"
              className="hc-mv__style-close"
              data-style-close-button
              aria-label="Close style editor"
            >
              x
            </button>
          </header>
          <iframe
            className="hc-mv__maputnik-frame"
            data-maputnik-frame
            title="Self-hosted Maputnik editor"
            sandbox="allow-scripts allow-same-origin"
          />
          <footer className="hc-mv__style-editor-footer">
            <output className="hc-mv__style-editor-status" data-style-editor-status aria-live="polite" />
            <button type="button" className="hc-mv__style-save" data-style-save-button>
              Save style
            </button>
          </footer>
        </section>
      ) : null}
    </div>
  );
}

const WORKSPACE_SAVED_MAP_IDS = [STYLE_EDITOR_DEMO_MAP_ID] as const;

function SavedMapsWorkspace(): JSX.Element {
  const { session } = useSession();
  const maps = useMemo(() => loadWorkspaceSavedMaps(), []);
  const workspaceName = session.status === "authenticated" ? session.workspace.name : "this workspace";

  return (
    <main className="maps-workspace hc-page" data-testid="maps-workspace">
      <header className="hc-page__header">
        <h1 className="hc-page__title">Maps</h1>
        <p className="hc-page__subtitle">Saved maps in {workspaceName}'s My Content workspace.</p>
      </header>

      <section className="maps-workspace__summary" aria-label="Saved map summary">
        <div className="maps-workspace__metric">
          <span className="maps-workspace__metric-value">{maps.length}</span>
          <span className="maps-workspace__metric-label">Saved map{maps.length === 1 ? "" : "s"}</span>
        </div>
        <Link to="/catalog?type=map" className="maps-workspace__secondary-action">
          Browse catalog maps
        </Link>
      </section>

      {maps.length === 0 ? (
        <EmptyState
          title="No saved maps yet"
          description="Open a catalog item in the map viewer, save the view, then return here to manage it from My Content."
          primaryAction={
            <Link to="/catalog" className="hc-btn hc-btn--primary">
              Browse catalog
            </Link>
          }
        />
      ) : (
        <ul className="maps-workspace__list" data-testid="saved-map-list">
          {maps.map((map) => (
            <li key={map.id}>
              <SavedMapCard map={map} />
            </li>
          ))}
        </ul>
      )}
    </main>
  );
}

function SavedMapCard({ map }: { map: SavedMapItem }): JSX.Element {
  const routePath = savedMapRoutePath(map);
  return (
    <article className="maps-workspace__card" data-testid={`saved-map-card-${map.id}`}>
      <div className="maps-workspace__card-main">
        <div className="maps-workspace__pills">
          <TypePill type={map.type} />
          <VisibilityPill sharing={map.access.sharing} />
        </div>
        <h2 className="maps-workspace__card-title">
          <Link to={routePath}>{map.title}</Link>
        </h2>
        <p className="maps-workspace__card-summary">{map.summary}</p>
        <dl className="maps-workspace__meta">
          <div>
            <dt>Owner</dt>
            <dd>{map.owner.name}</dd>
          </div>
          <div>
            <dt>Modified</dt>
            <dd>
              <time dateTime={map.timestamps.modified}>{formatDate(map.timestamps.modified)}</time>
            </dd>
          </div>
          <div>
            <dt>Layers</dt>
            <dd>{map.target.operationalLayerCount}</dd>
          </div>
        </dl>
      </div>
      <div className="maps-workspace__actions" aria-label={`Actions for ${map.title}`}>
        <Link to={routePath} className="maps-workspace__primary-action">
          Open map
        </Link>
      </div>
    </article>
  );
}

function savedMapRoutePath(map: SavedMapItem): string {
  try {
    const url = new URL(map.endpoints.self.accessURL, "https://console.honua.example");
    return `${url.pathname}${url.search}${url.hash}`;
  } catch {
    return `/maps/${encodeURIComponent(map.id)}`;
  }
}

function loadWorkspaceSavedMaps(): SavedMapItem[] {
  return WORKSPACE_SAVED_MAP_IDS.flatMap((id) => {
    const loaded = loadFixtureSavedMapForViewer(id);
    return loaded.status === "ok" ? [loaded.item] : [];
  });
}

export default function Maps(): JSX.Element {
  const params = useParams();
  return params["mapId"] ? <MapViewerSurface /> : <SavedMapsWorkspace />;
}
