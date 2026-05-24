import { useCallback, useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";

import { useSession } from "../auth/SessionContext.js";
import {
  CatalogError,
  type ContentItem,
  type GetDependenciesResponse,
  type ServiceLink,
} from "../contracts/content-item.js";
import { buildDatasetJsonLd, serializeJsonLd } from "../open-data/schema-org.js";
import { safeHttpUrl } from "../security/url.js";
import { SharePanel } from "../share/SharePanel.js";
import type { ShareAccess } from "../share/types.js";
import { CopyRow } from "../ui/CopyRow.js";
import { EmptyCell } from "../ui/EmptyCell.js";
import { EmptyState } from "../ui/EmptyState.js";
import { Pill } from "../ui/Pill.js";
import { Thumbnail } from "../ui/Thumbnail.js";
import { TypePill } from "../ui/TypePill.js";
import { VisibilityPill } from "../ui/VisibilityPill.js";
import { useCatalogClient } from "./CatalogContext.js";
import { RelatedItemsSection } from "./RelatedItemsSection.js";
import { RevisionContextSection } from "./RevisionContextSection.js";
import { ExtentPreview } from "./components/ExtentPreview.js";
import { formatDate, paragraphs } from "./components/format.js";
import { type OpenAction, getOpenAction } from "./openability.js";

type DetailState =
  | { kind: "loading" }
  | { kind: "ready"; item: ContentItem }
  | { kind: "error"; error: CatalogError | Error };

export function ItemDetailPage() {
  const { idOrSlug } = useParams<{ idOrSlug: string }>();
  const client = useCatalogClient();
  const [state, setState] = useState<DetailState>({ kind: "loading" });

  useEffect(() => {
    if (!idOrSlug) {
      setState({ kind: "error", error: new CatalogError("missing", "no item id provided") });
      return;
    }
    let cancelled = false;
    setState({ kind: "loading" });
    client
      .getItem(idOrSlug)
      .then((item) => {
        if (cancelled) return;
        setState({ kind: "ready", item });
      })
      .catch((error: unknown) => {
        if (cancelled) return;
        setState({ kind: "error", error: toError(error) });
      });
    return () => {
      cancelled = true;
    };
  }, [client, idOrSlug]);

  if (state.kind === "loading") {
    return (
      <main className="item-page" data-testid="item-page">
        <BackToCatalog />
        <EmptyState kind="loading" />
      </main>
    );
  }
  if (state.kind === "error") {
    const kind = errorToEmptyKind(state.error);
    return (
      <main className="item-page" data-testid="item-page" data-state={kind}>
        <BackToCatalog />
        <EmptyState kind={kind} message={state.error.message} />
      </main>
    );
  }

  return <ItemDetailView item={state.item} />;
}

function ItemDetailView({ item }: { item: ContentItem }) {
  const { session } = useSession();
  const [shareAccess, setShareAccess] = useState<ShareAccess>(() => ({
    sharing: item.access.sharing,
    embeddable: item.access.embeddable,
  }));
  const displayItem: ContentItem = {
    ...item,
    access: {
      ...item.access,
      sharing: shareAccess.sharing,
      embeddable: shareAccess.embeddable,
    },
  };
  const action = getOpenAction(displayItem);
  const licenseUrl = safeHttpUrl(displayItem.license.url);
  const [closure, setClosure] = useState<GetDependenciesResponse | null>(null);
  const [closureState, setClosureState] = useState<"idle" | "loading" | "error">("idle");
  const [closureError, setClosureError] = useState<string | null>(null);
  const client = useCatalogClient();

  useEffect(() => {
    setShareAccess({ sharing: item.access.sharing, embeddable: item.access.embeddable });
  }, [item.access.sharing, item.access.embeddable]);

  const handleShowClosure = useCallback(async () => {
    setClosureState("loading");
    setClosureError(null);
    try {
      const response = await client.getDependencies(item.id);
      setClosure(response);
      setClosureState("idle");
    } catch (error: unknown) {
      setClosureError(toError(error).message);
      setClosureState("error");
    }
  }, [client, item.id]);

  const loadShareClosure = useCallback(async () => {
    const response = await client.getDependencies(item.id);
    setClosure(response);
    return response;
  }, [client, item.id]);

  const patchShareAccess = useCallback(
    async (access: ShareAccess) => {
      if (!client.patchAccess) {
        return { kind: "error" as const, message: "sharing updates are unavailable for this catalog client" };
      }
      return client.patchAccess({ id: item.id, access });
    },
    [client, item.id],
  );

  const description = paragraphs(displayItem.description);
  const endpointRows = collectEndpoints(displayItem);
  const datasetJsonLd = buildDatasetJsonLd(displayItem);

  return (
    <main className="item-page" data-testid="item-page" data-item-id={displayItem.id} data-item-type={displayItem.type}>
      {datasetJsonLd ? (
        <script type="application/ld+json" data-testid="dataset-json-ld">
          {serializeJsonLd(datasetJsonLd)}
        </script>
      ) : null}
      <BackToCatalog />

      <header className="item-page__hero">
        <div className="item-page__hero-thumb">
          <Thumbnail
            src={displayItem.preview.thumbnail ?? displayItem.preview.image}
            alt={displayItem.title}
            type={displayItem.type}
          />
        </div>
        <div className="item-page__hero-body">
          <div className="item-page__pills">
            <TypePill type={displayItem.type} />
            <VisibilityPill sharing={displayItem.access.sharing} />
            {displayItem.access.openData ? <Pill tone="success">Open data</Pill> : null}
            {displayItem.access.embeddable ? <Pill tone="info">Embeddable</Pill> : null}
          </div>
          <h1 className="item-page__title">{displayItem.title}</h1>
          <p className="item-page__owner">
            <span>{displayItem.owner.name}</span>
            <span aria-hidden="true"> · </span>
            <span>
              Updated{" "}
              <time dateTime={displayItem.timestamps.modified}>{formatDate(displayItem.timestamps.modified)}</time>
            </span>
          </p>
          <p className="item-page__summary">{displayItem.summary}</p>
          <div className="item-page__actions">
            <PrimaryAction action={action} />
          </div>
          {action.kind === "unsupported" && action.reason ? (
            <p className="item-page__unsupported-reason" role="note" data-testid="unsupported-reason">
              {action.reason}
            </p>
          ) : null}
        </div>
      </header>

      <section className="item-page__section" aria-labelledby="section-description">
        <h2 id="section-description">Description</h2>
        {description.length > 0 ? (
          description.map((paragraph) => <p key={paragraph}>{paragraph}</p>)
        ) : (
          <p className="item-page__empty">No description provided.</p>
        )}
      </section>

      {displayItem.extent ? (
        <section className="item-page__section" aria-labelledby="section-extent">
          <h2 id="section-extent">Extent</h2>
          <ExtentPreview extent={displayItem.extent} title={`Extent of ${displayItem.title}`} />
        </section>
      ) : null}

      <RelatedItemsSection item={displayItem} headingId="section-related" />

      <section className="item-page__section" aria-labelledby="section-metadata">
        <h2 id="section-metadata">Metadata</h2>
        <dl className="item-page__metadata">
          <Row label="Tags">
            {displayItem.tags.length > 0 ? (
              <ul className="item-page__taglist">
                {displayItem.tags.map((tag) => (
                  <li key={tag}>
                    <Pill tone="muted">{tag}</Pill>
                  </li>
                ))}
              </ul>
            ) : (
              <EmptyCell value={null} />
            )}
          </Row>
          <Row label="License">
            {licenseUrl ? (
              <a href={licenseUrl} target="_blank" rel="noreferrer noopener">
                {displayItem.license.name}
                {displayItem.license.spdx ? (
                  <span className="item-page__license-spdx"> ({displayItem.license.spdx})</span>
                ) : null}
              </a>
            ) : (
              <span>
                {displayItem.license.name}
                {displayItem.license.spdx ? (
                  <span className="item-page__license-spdx"> ({displayItem.license.spdx})</span>
                ) : null}
              </span>
            )}
          </Row>
          <Row label="Attribution">
            <EmptyCell value={displayItem.attribution}>{displayItem.attribution}</EmptyCell>
          </Row>
          <Row label="Native CRS">
            <EmptyCell value={displayItem.nativeCrs}>{displayItem.nativeCrs}</EmptyCell>
          </Row>
          <Row label="Created">
            <time dateTime={displayItem.timestamps.created}>{formatDate(displayItem.timestamps.created)}</time>
          </Row>
          <Row label="Modified">
            <time dateTime={displayItem.timestamps.modified}>{formatDate(displayItem.timestamps.modified)}</time>
          </Row>
          <Row label="Published">
            <EmptyCell value={displayItem.timestamps.published}>
              {displayItem.timestamps.published ? (
                <time dateTime={displayItem.timestamps.published}>{formatDate(displayItem.timestamps.published)}</time>
              ) : null}
            </EmptyCell>
          </Row>
          <Row label="Refreshed">
            <EmptyCell value={displayItem.timestamps.refreshed}>
              {displayItem.timestamps.refreshed ? (
                <time dateTime={displayItem.timestamps.refreshed}>{formatDate(displayItem.timestamps.refreshed)}</time>
              ) : null}
            </EmptyCell>
          </Row>
          <Row label="Source">
            <span>
              {displayItem.source.kind}
              {displayItem.source.publishedBy ? <span> · by {displayItem.source.publishedBy}</span> : null}
              {displayItem.source.jobId ? <span> · job {displayItem.source.jobId}</span> : null}
            </span>
          </Row>
          {displayItem.source.history.length > 0 ? (
            <Row label="History">
              <ul className="item-page__history">
                {displayItem.source.history.map((event, index) => (
                  <li key={`${event.at}-${index}`}>
                    <time dateTime={event.at}>{formatDate(event.at)}</time> · {event.kind} · {event.actor}
                  </li>
                ))}
              </ul>
            </Row>
          ) : null}
        </dl>
      </section>

      <RevisionContextSection item={displayItem} headingId="section-revisions" />

      <SharePanel
        item={displayItem}
        session={session}
        loadClosure={loadShareClosure}
        patchAccess={patchShareAccess}
        onAccessChange={setShareAccess}
      />

      <section className="item-page__section" aria-labelledby="section-endpoints">
        <h2 id="section-endpoints">Endpoints</h2>
        {endpointRows.length > 0 ? (
          <div className="item-page__endpoints">
            {endpointRows.map((row) => (
              <CopyRow key={row.label} label={row.label} value={row.value} description={row.description} />
            ))}
          </div>
        ) : (
          <p className="item-page__empty">No published endpoints.</p>
        )}
      </section>

      <section className="item-page__section" aria-labelledby="section-capabilities">
        <h2 id="section-capabilities">Capabilities</h2>
        {displayItem.capabilities.length > 0 ? (
          <ul className="item-page__capabilities">
            {displayItem.capabilities.map((capability) => (
              <li key={capability}>
                <Pill tone="muted">{capability}</Pill>
              </li>
            ))}
          </ul>
        ) : (
          <p className="item-page__empty">No capability metadata reported.</p>
        )}
      </section>

      <section className="item-page__section" aria-labelledby="section-deps">
        <h2 id="section-deps">Dependencies</h2>
        {displayItem.dependencies.length === 0 ? (
          <p className="item-page__empty">No direct dependencies.</p>
        ) : (
          <ul className="item-page__deplist">
            {displayItem.dependencies.map((dep) => (
              <li key={dep.id} data-dep-id={dep.id}>
                <Link to={`/catalog/${encodeURIComponent(dep.id)}`}>{dep.id}</Link>
                <span className="item-page__deprole"> · {dep.role}</span>
                <span className="item-page__deptype"> · {dep.type}</span>
              </li>
            ))}
          </ul>
        )}
        <div className="item-page__deps-actions">
          <button
            type="button"
            className="item-page__closure"
            onClick={handleShowClosure}
            disabled={closureState === "loading" || displayItem.dependencies.length === 0}
          >
            {closureState === "loading" ? "Loading dependency closure…" : "Show full dependency closure"}
          </button>
          {closureState === "error" && closureError ? <p className="item-page__deps-error">{closureError}</p> : null}
          {closure ? <ClosureSummary closure={closure} /> : null}
        </div>
      </section>
    </main>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="item-page__row">
      <dt>{label}</dt>
      <dd>{children}</dd>
    </div>
  );
}

function PrimaryAction({ action }: { action: OpenAction }) {
  if (action.kind === "open-in-map" && action.href) {
    return (
      <Link to={action.href} className="item-page__primary item-page__primary--map">
        {action.label}
      </Link>
    );
  }
  if (action.kind === "open-external" && action.href) {
    return (
      <a
        href={action.href}
        target="_blank"
        rel="noreferrer noopener"
        className="item-page__primary item-page__primary--external"
      >
        {action.label}
      </a>
    );
  }
  return (
    <button
      type="button"
      className="item-page__primary item-page__primary--disabled"
      disabled
      aria-disabled="true"
      title={action.reason ?? "This item is not viewable."}
    >
      {action.label}
    </button>
  );
}

function ClosureSummary({ closure }: { closure: GetDependenciesResponse }) {
  return (
    <div className="closure-summary" data-testid="closure-summary">
      <p>
        {closure.nodes.length} viewable, {closure.unauthorized.length} unauthorized, {closure.unsupported.length}{" "}
        unsupported, {closure.missing.length} missing
        {closure.truncated ? " (truncated)" : ""}
      </p>
      {closure.nodes.length > 0 ? (
        <ul className="closure-summary__list">
          {closure.nodes.map((node) => (
            <li key={node.id}>
              <Link to={`/catalog/${encodeURIComponent(node.id)}`}>{node.summary?.title ?? node.id}</Link>
              <span>
                {" "}
                · depth {node.depth} · {node.role}
              </span>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}

function BackToCatalog() {
  return (
    <p className="item-page__crumb">
      <Link to="/catalog">← Back to catalog</Link>
    </p>
  );
}

interface EndpointRow {
  readonly label: string;
  readonly value: string;
  readonly description?: string;
}

function collectEndpoints(item: ContentItem): EndpointRow[] {
  const rows: EndpointRow[] = [];
  if (item.endpoints.self) {
    rows.push({
      label: "Portal URL",
      value: item.endpoints.self.accessURL,
      description: "Stable portal-side URL for this item.",
    });
  }
  if (item.endpoints.geoservices) {
    rows.push({
      label: "ArcGIS REST",
      value: endpointURL(item.endpoints.geoservices),
      description: "GeoServices / ArcGIS REST endpoint.",
    });
  }
  if (item.endpoints.ogcFeatures) {
    rows.push({
      label: "OGC Features",
      value: endpointURL(item.endpoints.ogcFeatures),
      description: "OGC API – Features collection URL.",
    });
  }
  if (item.endpoints.stac) {
    rows.push({
      label: "STAC",
      value: endpointURL(item.endpoints.stac),
      description: "STAC catalog or item URL.",
    });
  }
  if (item.endpoints.tiles) {
    rows.push({
      label: "Tiles",
      value: endpointURL(item.endpoints.tiles),
      description: "Tile URL template.",
    });
  }
  return rows;
}

function endpointURL(link: ServiceLink): string {
  return link.accessURL;
}

function errorToEmptyKind(error: CatalogError | Error): "missing" | "unauthorized" | "unsupported" | "error" {
  if (error instanceof CatalogError) {
    if (error.code === "missing") return "missing";
    if (error.code === "unauthorized") return "unauthorized";
    if (error.code === "unsupported") return "unsupported";
  }
  return "error";
}

function toError(error: unknown): CatalogError | Error {
  if (error instanceof CatalogError) return error;
  if (error instanceof Error) return error;
  return new Error(String(error));
}
