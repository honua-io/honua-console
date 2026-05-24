import { useEffect, useState } from "react";
import { Link } from "react-router-dom";

import { EmptyState } from "../shell/EmptyState.js";
import { studioPublishingClient } from "../studio/publishing/fixtureClient.js";
import { publishReviewRoute, studioDraftRoute, studioPreviewRoute } from "../studio/publishing/routes.js";
import type { StudioPublishDraft, StudioPublishingProblem } from "../studio/publishing/types.js";
import { studioPublishingProblemFromError } from "../studio/publishing/types.js";

export function StudioHomePage(): JSX.Element {
  const [drafts, setDrafts] = useState<readonly StudioPublishDraft[] | null>(null);
  const [error, setError] = useState<StudioPublishingProblem | null>(null);

  useEffect(() => {
    let cancelled = false;
    setDrafts(null);
    setError(null);
    studioPublishingClient
      .listDrafts()
      .then((items) => {
        if (cancelled) return;
        setDrafts(items.filter((draft) => draft.draftId !== "draft-map-conflict"));
        setError(null);
      })
      .catch((reason: unknown) => {
        if (cancelled) return;
        setDrafts(null);
        setError(studioPublishingProblemFromError(reason, "Studio drafts could not load."));
      });
    return () => {
      cancelled = true;
    };
  }, []);

  if (error) {
    return <EmptyState kind={error.kind} title="Studio drafts unavailable" description={error.message} />;
  }

  if (!drafts) {
    return <EmptyState kind="missing" title="Loading Studio drafts" description="Resolving draft packages for publish review." />;
  }

  return (
    <section className="page" data-testid="studio-home">
      <header className="page__header">
        <p className="eyebrow">Studio</p>
        <h1>Drafts and generated previews</h1>
        <p>Builder drafts stay in Studio until a publish review turns them into versioned Console content items.</p>
      </header>

      <div className="grid">
        {drafts.map((draft) => (
          <article className="card" key={draft.draftId} data-testid={`studio-draft-${draft.target}`}>
            <p className="eyebrow">{draft.target}</p>
            <h2>{draft.title}</h2>
            <p>{draft.summary}</p>
            <div className="pill-row">
              {draft.tags.map((tag) => (
                <span className="pill" key={tag}>
                  {tag}
                </span>
              ))}
            </div>
            <div className="card__actions">
              <Link className="button button--secondary" to={studioDraftRoute(draft.draftId)}>
                Open draft
              </Link>
              <Link className="button button--secondary" to={studioPreviewRoute(draft.draftId)}>
                Generated preview
              </Link>
              <Link className="button" to={publishReviewRoute(draft.draftId)}>
                Review publish
              </Link>
            </div>
          </article>
        ))}
      </div>
    </section>
  );
}
