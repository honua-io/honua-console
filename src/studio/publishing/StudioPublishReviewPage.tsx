import { useEffect, useMemo, useState } from "react";
import type { FormEvent } from "react";
import { Link, useParams } from "react-router-dom";

import { EmptyState } from "../../shell/EmptyState.js";
import { DEFAULT_SHARE_SETTINGS } from "./fixtures.js";
import { studioPublishingClient } from "./fixtureClient.js";
import { publishReviewRoute } from "./routes.js";
import { emitStudioPublishTelemetry } from "./telemetry.js";
import type {
  PublishedContentItem,
  ShareEmbedSettings,
  ShareVisibility,
  StudioPublishDraft,
  StudioPublishingProblem
} from "./types.js";
import { studioPublishingProblemFromError } from "./types.js";

type ReviewState =
  | { readonly kind: "loading" }
  | { readonly kind: "ready"; readonly draft: StudioPublishDraft }
  | { readonly kind: "error"; readonly problemKind: StudioPublishingProblem["kind"]; readonly message: string };

interface FormState {
  readonly title: string;
  readonly summary: string;
  readonly tags: string;
  readonly targetAudience: string;
  readonly versionNote: string;
  readonly visibility: ShareVisibility;
  readonly groupIds: string;
  readonly embedEnabled: boolean;
}

const SHARE_OPTIONS: readonly { readonly value: ShareVisibility; readonly label: string }[] = [
  { value: "private", label: "Private" },
  { value: "workspace", label: "Workspace" },
  { value: "group", label: "Group" },
  { value: "public-link", label: "Public link" },
  { value: "public", label: "Public" }
];

export function StudioPublishReviewPage(): JSX.Element {
  const { draftId = "" } = useParams();
  const [state, setState] = useState<ReviewState>({ kind: "loading" });
  const [form, setForm] = useState<FormState | null>(null);
  const [result, setResult] = useState<PublishedContentItem | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setState({ kind: "loading" });
    setResult(null);
    studioPublishingClient
      .getDraft(draftId)
      .then((draft) => {
        if (cancelled) return;
        setState({ kind: "ready", draft });
        setForm({
          title: draft.title,
          summary: draft.summary,
          tags: draft.tags.join(", "),
          targetAudience: draft.targetAudience,
          versionNote: "Initial publish from Studio",
          visibility: DEFAULT_SHARE_SETTINGS.visibility,
          groupIds: "",
          embedEnabled: DEFAULT_SHARE_SETTINGS.embedEnabled
        });
        emitStudioPublishTelemetry({
          name: "publish.review.opened",
          draftId: draft.draftId,
          target: draft.target
        });
      })
      .catch((error: unknown) => {
        if (cancelled) return;
        const problem = studioPublishingProblemFromError(error, "Publish review could not load.");
        setState({ kind: "error", problemKind: problem.kind, message: problem.message });
      });
    return () => {
      cancelled = true;
    };
  }, [draftId]);

  const shareSettings = useMemo<ShareEmbedSettings | null>(() => {
    if (!form) return null;
    return {
      visibility: form.visibility,
      groupIds: form.groupIds
        .split(",")
        .map((group) => group.trim())
        .filter(Boolean),
      publicLinkEnabled: form.visibility === "public-link",
      embedEnabled: form.embedEnabled,
      embedPolicy: form.embedEnabled ? (form.visibility === "public" || form.visibility === "public-link" ? "public" : "same-origin") : "disabled"
    };
  }, [form]);

  if (state.kind === "error") {
    return <EmptyState kind={state.problemKind} title="Publish review unavailable" description={state.message} />;
  }

  if (state.kind === "loading" || !form || !shareSettings) {
    return <EmptyState kind="missing" title="Loading publish review" description="Resolving the Studio draft and package context." />;
  }

  const { draft } = state;
  const currentForm = form;
  const currentShareSettings = shareSettings;

  async function handleSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();
    setSubmitting(true);
    setSubmitError(null);

    if (currentShareSettings.visibility === "group" && currentShareSettings.groupIds.length === 0) {
      setSubmitError("Choose at least one group before publishing with group visibility.");
      emitStudioPublishTelemetry({
        name: "publish.failed",
        draftId: draft.draftId,
        target: draft.target,
        problemKind: "invalid"
      });
      setSubmitting(false);
      return;
    }

    emitStudioPublishTelemetry({
      name: "publish.submitted",
      draftId: draft.draftId,
      target: draft.target
    });

    try {
      const item = await studioPublishingClient.publishDraft({
        draftId: draft.draftId,
        title: currentForm.title,
        summary: currentForm.summary,
        tags: currentForm.tags.split(",").map((tag) => tag.trim()).filter(Boolean),
        targetAudience: currentForm.targetAudience,
        versionNote: currentForm.versionNote,
        share: currentShareSettings
      });
      setResult(item);
      emitStudioPublishTelemetry({
        name: "publish.succeeded",
        draftId: draft.draftId,
        itemId: item.itemId,
        target: item.type
      });
    } catch (error) {
      const problem = studioPublishingProblemFromError(error, "Publish failed.");
      setSubmitError(problem.message);
      emitStudioPublishTelemetry({
        name: "publish.failed",
        draftId: draft.draftId,
        target: draft.target,
        problemKind: problem.kind
      });
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <section className="page" data-testid="publish-review" data-target={draft.target}>
      <header className="page__header">
        <p className="eyebrow">Studio publish review</p>
        <h1>{draft.title}</h1>
        <p>{draft.summary}</p>
      </header>

      <div className="grid grid--two">
        <form className="card form" onSubmit={handleSubmit} aria-label="Publish review form">
          <h2>Publish settings</h2>
          <div className="field">
            <label htmlFor="publish-title">Name</label>
            <input
              id="publish-title"
              value={form.title}
              onChange={(event) => setForm({ ...form, title: event.target.value })}
              required
            />
          </div>
          <div className="field">
            <label htmlFor="publish-summary">Summary</label>
            <textarea
              id="publish-summary"
              value={form.summary}
              onChange={(event) => setForm({ ...form, summary: event.target.value })}
              required
            />
          </div>
          <div className="field">
            <label htmlFor="publish-tags">Tags</label>
            <input id="publish-tags" value={form.tags} onChange={(event) => setForm({ ...form, tags: event.target.value })} />
          </div>
          <div className="field">
            <label htmlFor="publish-audience">Target audience</label>
            <input
              id="publish-audience"
              value={form.targetAudience}
              onChange={(event) => setForm({ ...form, targetAudience: event.target.value })}
            />
          </div>
          <div className="field">
            <label htmlFor="publish-visibility">Visibility</label>
            <select
              id="publish-visibility"
              value={form.visibility}
              onChange={(event) => setForm({ ...form, visibility: event.target.value as ShareVisibility })}
            >
              {SHARE_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </div>
          {form.visibility === "group" ? (
            <div className="field">
              <label htmlFor="publish-groups">Group ids</label>
              <input
                id="publish-groups"
                value={form.groupIds}
                onChange={(event) => setForm({ ...form, groupIds: event.target.value })}
                placeholder="group-emergency-ops"
                required
              />
            </div>
          ) : null}
          <label className="checkbox">
            <input
              type="checkbox"
              checked={form.embedEnabled}
              onChange={(event) => setForm({ ...form, embedEnabled: event.target.checked })}
            />
            Allow embed when the selected visibility supports it
          </label>
          <div className="field">
            <label htmlFor="publish-note">Version note</label>
            <textarea
              id="publish-note"
              value={form.versionNote}
              onChange={(event) => setForm({ ...form, versionNote: event.target.value })}
              required
            />
          </div>
          {submitError ? (
            <p role="alert" className="pill pill--warning" data-testid="publish-error">
              {submitError}
            </p>
          ) : null}
          <button type="submit" disabled={submitting} data-testid="publish-submit">
            {submitting ? "Publishing" : "Publish"}
          </button>
        </form>

        <aside className="stack">
          <section className="card">
            <h2>Package context</h2>
            <dl className="summary-list">
              <div>
                <dt>Target</dt>
                <dd>{formatTarget(draft.target)}</dd>
              </div>
              <div>
                <dt>Package</dt>
                <dd>{draft.packageRef.packageType} {draft.packageRef.packageId}</dd>
              </div>
              <div>
                <dt>Rollback target</dt>
                <dd>{draft.rollbackTargetVersionId ?? "None"}</dd>
              </div>
            </dl>
          </section>

          <section className="card">
            <h2>Dependencies</h2>
            <div className="pill-row">
              {draft.dependencies.map((dependency) => (
                <span className="pill" key={dependency.itemId}>
                  {dependency.title} ({dependency.requiredVisibility})
                </span>
              ))}
            </div>
          </section>

          <section className="card">
            <h2>Warnings</h2>
            {draft.warnings.length > 0 ? (
              <div className="pill-row">
                {draft.warnings.map((warning) => (
                  <span className={warning.severity === "blocking" ? "pill pill--warning" : "pill"} key={warning.code}>
                    {warning.code}
                  </span>
                ))}
              </div>
            ) : (
              <p>No package warnings.</p>
            )}
          </section>

          <section className="card">
            <h2>Provenance</h2>
            <dl className="summary-list">
              <div>
                <dt>Prompt</dt>
                <dd>{draft.provenance.promptRef}</dd>
              </div>
              <div>
                <dt>Plan</dt>
                <dd>{draft.provenance.planRef}</dd>
              </div>
              <div>
                <dt>Apply job</dt>
                <dd>{draft.provenance.applyJobRef}</dd>
              </div>
            </dl>
          </section>
        </aside>
      </div>

      {result ? <PublishResult item={result} /> : null}
    </section>
  );
}

function PublishResult({ item }: { readonly item: PublishedContentItem }): JSX.Element {
  return (
    <section className="card" data-testid="publish-result">
      <p className="eyebrow">Published</p>
      <h2>{item.title}</h2>
      <dl className="summary-list">
        <div>
          <dt>Stable route</dt>
          <dd data-testid="published-canonical-route">{item.routes.canonical}</dd>
        </div>
        <div>
          <dt>Version</dt>
          <dd>{item.version.versionId} ({item.version.changeNote})</dd>
        </div>
        <div>
          <dt>Share</dt>
          <dd>{item.share.visibility}, embed {item.share.embedPolicy}</dd>
        </div>
      </dl>
      <nav className="result-links" aria-label="Published item links">
        <Link className="button button--secondary" to={item.routes.catalog} data-testid="result-catalog-link">
          Catalog detail
        </Link>
        <Link className="button button--secondary" to={item.routes.preview} data-testid="result-preview-link">
          Preview
        </Link>
        <Link className="button button--secondary" to={item.routes.share} data-testid="result-share-link">
          Share
        </Link>
        <Link className="button button--secondary" to={item.routes.embed} data-testid="result-embed-link">
          Embed
        </Link>
        <Link className="button button--secondary" to={item.routes.editInStudio} data-testid="result-edit-link">
          Edit in Studio
        </Link>
      </nav>
    </section>
  );
}

function formatTarget(target: StudioPublishDraft["target"]): string {
  return target[0].toUpperCase() + target.slice(1);
}

export function publishReviewHref(draftId: string): string {
  return publishReviewRoute(draftId);
}
