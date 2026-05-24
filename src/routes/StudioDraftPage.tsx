import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";

import { EmptyState } from "../shell/EmptyState.js";
import { studioPublishingClient } from "../studio/publishing/fixtureClient.js";
import { publishReviewRoute, studioPreviewRoute } from "../studio/publishing/routes.js";
import type { StudioPublishDraft, StudioPublishingProblem } from "../studio/publishing/types.js";
import { studioPublishingProblemFromError } from "../studio/publishing/types.js";

export function StudioDraftPage(): JSX.Element {
  const { draftId = "" } = useParams();
  const [draft, setDraft] = useState<StudioPublishDraft | null>(null);
  const [error, setError] = useState<StudioPublishingProblem | null>(null);

  useEffect(() => {
    let cancelled = false;
    setDraft(null);
    setError(null);
    studioPublishingClient
      .getDraft(draftId)
      .then((item) => {
        if (cancelled) return;
        setDraft(item);
        setError(null);
      })
      .catch((reason: unknown) => {
        if (cancelled) return;
        setDraft(null);
        setError(studioPublishingProblemFromError(reason, "Draft could not load."));
      });
    return () => {
      cancelled = true;
    };
  }, [draftId]);

  if (error) {
    return <EmptyState kind={error.kind} title="Draft unavailable" description={error.message} />;
  }

  if (!draft) {
    return <EmptyState kind="missing" title="Loading draft" description="Resolving Studio draft metadata." />;
  }

  const hasBlockingWarnings = draft.warnings.some((warning) => warning.severity === "blocking");

  return (
    <section className="page" data-testid="studio-draft-page">
      <header className="page__header">
        <p className="eyebrow">Studio draft</p>
        <h1>{draft.title}</h1>
        <p>{draft.summary}</p>
      </header>
      <section className="card">
        <h2>Draft package</h2>
        <dl className="summary-list">
          <div>
            <dt>Package type</dt>
            <dd>{draft.packageRef.packageType}</dd>
          </div>
          <div>
            <dt>Dependencies</dt>
            <dd>{draft.dependencies.map((dependency) => dependency.title).join(", ")}</dd>
          </div>
          <div>
            <dt>Warnings</dt>
            <dd>{draft.warnings.length}</dd>
          </div>
        </dl>
        {hasBlockingWarnings ? <p className="pill pill--warning">Blocking warnings must be resolved before public publish.</p> : null}
        <div className="card__actions">
          <Link className="button button--secondary" to={studioPreviewRoute(draft.draftId)}>
            Generated preview
          </Link>
          <Link className="button" to={publishReviewRoute(draft.draftId)} data-testid="draft-publish-link">
            Review publish
          </Link>
        </div>
      </section>
    </section>
  );
}

export function errorKindForDraft(error: unknown): StudioPublishingProblem["kind"] {
  return studioPublishingProblemFromError(error, "Draft could not load.").kind;
}
