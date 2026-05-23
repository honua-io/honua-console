import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";

import { EmptyState } from "../../shell/EmptyState.js";
import { Forbidden } from "../../shell/Forbidden.js";
import { CopyRow } from "../../ui/CopyRow.js";
import { Pill } from "../../ui/Pill.js";
import { VisibilityPill } from "../../ui/VisibilityPill.js";
import { useGeneratedAppLifecycleClient } from "./GeneratedAppLifecycleContext.js";
import { GeneratedAppLifecycleError } from "./client.js";
import { previousGeneratedAppRevision } from "./lifecycle.js";
import { emitGeneratedAppLifecycleTelemetry } from "./telemetry.js";
import type { GeneratedAppPreviewDescriptor } from "./types.js";

import "./generated-apps.css";

const DATE_FORMAT = new Intl.DateTimeFormat(undefined, {
  year: "numeric",
  month: "short",
  day: "numeric",
});

function formatDate(iso: string | null | undefined): string {
  if (!iso) return "—";
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;
  return DATE_FORMAT.format(parsed);
}

type PreviewState =
  | { kind: "loading" }
  | { kind: "ready"; descriptor: GeneratedAppPreviewDescriptor }
  | { kind: "error"; error: GeneratedAppLifecycleError | Error };

export function GeneratedAppPreviewPage(): JSX.Element {
  const { itemId } = useParams<{ itemId: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const revisionId = searchParams.get("revision");
  const client = useGeneratedAppLifecycleClient();
  const [state, setState] = useState<PreviewState>({ kind: "loading" });
  const [rollbackState, setRollbackState] = useState<"idle" | "working" | "error">("idle");

  useEffect(() => {
    if (!itemId) {
      setState({ kind: "error", error: new GeneratedAppLifecycleError("missing", "No generated app id provided.") });
      return;
    }
    let cancelled = false;
    setState({ kind: "loading" });
    client
      .getPreview(itemId, { revisionId })
      .then((descriptor) => {
        if (cancelled) return;
        setState({ kind: "ready", descriptor });
        emitGeneratedAppLifecycleTelemetry({
          name: "studio.generated-app.preview-opened",
          itemId: descriptor.item.id,
          revisionId: descriptor.activeRevision.id,
        });
      })
      .catch((error: unknown) => {
        if (cancelled) return;
        setState({ kind: "error", error: toError(error) });
      });
    return () => {
      cancelled = true;
    };
  }, [client, itemId, revisionId]);

  const handleRollback = useCallback(async () => {
    if (state.kind !== "ready") return;
    const previous = previousGeneratedAppRevision(state.descriptor.lifecycle);
    if (!previous) return;
    setRollbackState("working");
    emitGeneratedAppLifecycleTelemetry({
      name: "studio.generated-app.rollback-started",
      itemId: state.descriptor.item.id,
      revisionId: previous.id,
    });
    try {
      const record = await client.rollback(state.descriptor.item.id, previous.id);
      const descriptor = await client.getPreview(record.item.id);
      setState({ kind: "ready", descriptor });
      setSearchParams(new URLSearchParams({ revision: descriptor.activeRevision.id }), { replace: true });
      setRollbackState("idle");
      emitGeneratedAppLifecycleTelemetry({
        name: "studio.generated-app.rollback-completed",
        itemId: descriptor.item.id,
        revisionId: descriptor.activeRevision.id,
      });
    } catch (error) {
      setRollbackState("error");
      setState({ kind: "error", error: toError(error) });
      emitGeneratedAppLifecycleTelemetry({
        name: "studio.generated-app.rollback-failed",
        itemId: state.descriptor.item.id,
        revisionId: previous.id,
        detail: { message: error instanceof Error ? error.message : String(error) },
      });
    }
  }, [client, setSearchParams, state]);

  if (state.kind === "loading") {
    return (
      <div className="generated-app" data-testid="generated-app-preview-page">
        <EmptyState title="Loading generated app" description="Resolving the authenticated preview package." />
      </div>
    );
  }

  if (state.kind === "error") {
    return (
      <div className="generated-app" data-testid="generated-app-preview-page">
        <PreviewError error={state.error} />
      </div>
    );
  }

  return (
    <GeneratedAppPreviewView descriptor={state.descriptor} rollbackState={rollbackState} onRollback={handleRollback} />
  );
}

function GeneratedAppPreviewView({
  descriptor,
  rollbackState,
  onRollback,
}: {
  descriptor: GeneratedAppPreviewDescriptor;
  rollbackState: "idle" | "working" | "error";
  onRollback: () => void;
}): JSX.Element {
  const { item, lifecycle, activeRevision, previewUrl } = descriptor;
  const previous = useMemo(() => previousGeneratedAppRevision(lifecycle), [lifecycle]);
  const rollbackLabel = previous ? `Roll back to ${previous.label}` : "No prior revision";

  return (
    <div
      className="generated-app"
      data-testid="generated-app-preview-page"
      data-item-id={item.id}
      data-active-revision={activeRevision.id}
    >
      <header className="generated-app__header">
        <p className="generated-app__crumb">
          <Link to="/">Back to home</Link>
        </p>
        <div className="generated-app__title-row">
          <div>
            <div className="generated-app__pills">
              <Pill tone={lifecycle.state === "published" ? "success" : "warning"}>{lifecycle.state}</Pill>
              <VisibilityPill sharing={item.access.sharing} />
              <Pill tone="info">Revision {activeRevision.sequence}</Pill>
            </div>
            <h1>{item.title}</h1>
            <p>{item.summary}</p>
          </div>
          <a className="hc-btn hc-btn--primary" href={previewUrl} aria-label="Open authenticated preview URL">
            Open preview
          </a>
        </div>
      </header>

      <section className="generated-app__section" aria-labelledby="generated-app-preview-heading">
        <div className="generated-app__section-heading">
          <h2 id="generated-app-preview-heading">Authenticated Preview</h2>
          <span>{activeRevision.label}</span>
        </div>
        <div className="generated-app__preview" data-testid="generated-app-preview-surface">
          <div>
            <strong>SDK AppPackage restore point</strong>
            <p>
              This preview is loaded from the persisted AppPackage and manifest references below. Opening it does not
              call the generation flow again.
            </p>
          </div>
          <dl>
            <div>
              <dt>Manifest</dt>
              <dd>{activeRevision.manifestVersion}</dd>
            </div>
            <div>
              <dt>Source</dt>
              <dd>{lifecycle.source.title}</dd>
            </div>
            <div>
              <dt>Server job</dt>
              <dd>{activeRevision.serverJob?.id ?? "Not recorded"}</dd>
            </div>
          </dl>
        </div>
      </section>

      <section className="generated-app__section" aria-labelledby="generated-app-artifacts-heading">
        <h2 id="generated-app-artifacts-heading">Stored References</h2>
        <div className="generated-app__copy-grid">
          <CopyRow label="Preview URL" value={previewUrl} description="Authenticated Console route" />
          <CopyRow
            label="AppPackage"
            value={activeRevision.appPackageRef.url ?? activeRevision.appPackageRef.id}
            description={activeRevision.appPackageRef.id}
          />
          <CopyRow
            label="Manifest artifact"
            value={activeRevision.manifestArtifact.url ?? activeRevision.manifestArtifact.id}
            description={activeRevision.manifestArtifact.id}
          />
          <CopyRow
            label="BuildSpec reference"
            value={activeRevision.buildSpecRef.url ?? activeRevision.buildSpecRef.id}
            description={activeRevision.buildSpecRef.id}
          />
        </div>
      </section>

      <section className="generated-app__section" aria-labelledby="generated-app-history-heading">
        <div className="generated-app__section-heading">
          <h2 id="generated-app-history-heading">Revision History</h2>
          <button
            type="button"
            className="hc-btn"
            disabled={!previous || rollbackState === "working"}
            onClick={onRollback}
          >
            {rollbackState === "working" ? "Rolling back..." : rollbackLabel}
          </button>
        </div>
        <ol className="generated-app__revisions" data-testid="generated-app-revisions">
          {lifecycle.revisions
            .slice()
            .sort((a, b) => b.sequence - a.sequence)
            .map((revision) => (
              <li key={revision.id} data-active={revision.id === activeRevision.id}>
                <div>
                  <strong>{revision.label}</strong>
                  <span>{revision.id === activeRevision.id ? "Active" : "Stored"}</span>
                </div>
                <p>
                  <time dateTime={revision.createdAt}>{formatDate(revision.createdAt)}</time> by {revision.actor}
                </p>
                <p>{revision.planRef.warnings.join("; ") || "No plan warnings recorded."}</p>
              </li>
            ))}
        </ol>
      </section>
    </div>
  );
}

function PreviewError({ error }: { error: GeneratedAppLifecycleError | Error }): JSX.Element {
  if (error instanceof GeneratedAppLifecycleError && error.code === "unauthorized") {
    return <Forbidden reason={error.message} />;
  }
  if (error instanceof GeneratedAppLifecycleError && error.code === "missing") {
    return <EmptyState title="Generated app not found" description={error.message} tone="warning" />;
  }
  if (error instanceof GeneratedAppLifecycleError && error.code === "unsupported") {
    return <EmptyState title="Preview unavailable" description={error.message} tone="warning" />;
  }
  return <EmptyState title="Generated app preview failed" description={error.message} tone="warning" />;
}

function toError(error: unknown): GeneratedAppLifecycleError | Error {
  if (error instanceof GeneratedAppLifecycleError) return error;
  if (error instanceof Error) return error;
  return new Error(String(error));
}
