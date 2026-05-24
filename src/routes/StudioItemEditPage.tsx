import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";

import { EmptyState } from "../shell/EmptyState.js";
import { studioPublishingClient } from "../studio/publishing/fixtureClient.js";
import { publishReviewRoute } from "../studio/publishing/routes.js";
import { emitStudioPublishTelemetry } from "../studio/publishing/telemetry.js";
import type { ReopenedStudioArtifact } from "../studio/publishing/types.js";
import { isStudioPublishingError } from "../studio/publishing/types.js";

export function StudioItemEditPage(): JSX.Element {
  const { itemId = "" } = useParams();
  const [artifact, setArtifact] = useState<ReopenedStudioArtifact | null>(null);
  const [error, setError] = useState<{ readonly kind: "missing" | "server"; readonly message: string } | null>(null);

  useEffect(() => {
    let cancelled = false;
    studioPublishingClient
      .reopenPublishedItem(itemId)
      .then((reopened) => {
        if (cancelled) return;
        setArtifact(reopened);
        emitStudioPublishTelemetry({
          name: "publish.reopen.completed",
          itemId: reopened.item.itemId,
          target: reopened.item.type
        });
      })
      .catch((reason: unknown) => {
        if (cancelled) return;
        setError({
          kind: isStudioPublishingError(reason) && reason.kind === "missing" ? "missing" : "server",
          message: reason instanceof Error ? reason.message : "Published item could not reopen."
        });
      });
    return () => {
      cancelled = true;
    };
  }, [itemId]);

  if (error) {
    return <EmptyState kind={error.kind} title="Cannot reopen item" description={error.message} />;
  }

  if (!artifact) {
    return <EmptyState kind="missing" title="Loading edit context" description="Resolving active revision package and provenance." />;
  }

  return (
    <section className="page" data-testid="studio-edit-page">
      <header className="page__header">
        <p className="eyebrow">Edit in Studio</p>
        <h1>{artifact.item.title}</h1>
        <p>{artifact.item.summary}</p>
      </header>
      <section className="card">
        <h2>Reopened package</h2>
        <dl className="summary-list">
          <div>
            <dt>Source version</dt>
            <dd>{artifact.editContext.sourceVersionId}</dd>
          </div>
          <div>
            <dt>Package</dt>
            <dd>{artifact.editContext.packageRef.packageId}</dd>
          </div>
          <div>
            <dt>Generation call</dt>
            <dd data-testid="reopen-generation-state">
              {artifact.editContext.loadedWithoutGeneration ? "Not called" : "Called"}
            </dd>
          </div>
          <div>
            <dt>Prompt</dt>
            <dd>{artifact.editContext.promptRef}</dd>
          </div>
          <div>
            <dt>Plan</dt>
            <dd>{artifact.editContext.planRef}</dd>
          </div>
        </dl>
        <div className="card__actions">
          <Link className="button" to={publishReviewRoute(artifact.editContext.draftId)}>
            Review update
          </Link>
          <Link className="button button--secondary" to={artifact.item.routes.catalog}>
            Catalog detail
          </Link>
        </div>
      </section>
    </section>
  );
}
