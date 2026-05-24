import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";

import { EmptyState } from "../shell/EmptyState.js";
import { studioPublishingClient } from "../studio/publishing/fixtureClient.js";
import { publishReviewRoute, studioDraftRoute } from "../studio/publishing/routes.js";
import type { StudioPublishDraft } from "../studio/publishing/types.js";

export function StudioPreviewPage(): JSX.Element {
  const { draftId = "" } = useParams();
  const [draft, setDraft] = useState<StudioPublishDraft | null>(null);
  const [error, setError] = useState<string | null>(null);

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
        setError(reason instanceof Error ? reason.message : "Preview could not load.");
      });
    return () => {
      cancelled = true;
    };
  }, [draftId]);

  if (error) {
    return <EmptyState kind="missing" title="Preview not found" description={error} />;
  }

  if (!draft) {
    return <EmptyState kind="missing" title="Loading preview" description="Resolving generated preview package." />;
  }

  return (
    <section className="page" data-testid="studio-preview-page">
      <header className="page__header">
        <p className="eyebrow">Generated preview</p>
        <h1>{draft.title}</h1>
        <p>{draft.summary}</p>
      </header>
      <section className="preview-frame" data-testid="generated-preview">
        <p className="eyebrow">{draft.target} preview</p>
        <h2>{draft.packageRef.packageId}</h2>
        <p>Preview uses the active Studio package and does not create a published item until review is submitted.</p>
      </section>
      <div className="card__actions">
        <Link className="button button--secondary" to={studioDraftRoute(draft.draftId)}>
          Back to draft
        </Link>
        <Link className="button" to={publishReviewRoute(draft.draftId)} data-testid="preview-publish-link">
          Review publish
        </Link>
      </div>
    </section>
  );
}
