import type { ContentItem } from "../contracts/content-item.js";
import { formatDate } from "./components/format.js";
import { getRevisionContext } from "./open-data-metadata.js";

export interface RevisionContextSectionProps {
  readonly item: ContentItem;
  readonly headingId: string;
}

export function RevisionContextSection({ item, headingId }: RevisionContextSectionProps): JSX.Element {
  const revision = getRevisionContext(item);

  return (
    <section className="item-page__section" aria-labelledby={headingId}>
      <h2 id={headingId}>Revision history</h2>
      <dl className="item-page__metadata">
        <RevisionRow label="Published">
          {revision.publishedAt ? (
            <time dateTime={revision.publishedAt}>{formatDate(revision.publishedAt)}</time>
          ) : (
            "Not published"
          )}
        </RevisionRow>
        <RevisionRow label="Latest modified">
          <time dateTime={revision.modifiedAt}>{formatDate(revision.modifiedAt)}</time>
        </RevisionRow>
        {revision.refreshedAt ? (
          <RevisionRow label="Latest refresh">
            <time dateTime={revision.refreshedAt}>{formatDate(revision.refreshedAt)}</time>
          </RevisionRow>
        ) : null}
        {revision.version ? <RevisionRow label="Version">{revision.version}</RevisionRow> : null}
        {revision.sourceVersion ? <RevisionRow label="Source version">{revision.sourceVersion}</RevisionRow> : null}
        {revision.sourceIdentifier ? (
          <RevisionRow label="Source identifier">{revision.sourceIdentifier}</RevisionRow>
        ) : null}
        {revision.updateNotes ? <RevisionRow label="Update notes">{revision.updateNotes}</RevisionRow> : null}
        {revision.changeSummary ? <RevisionRow label="Change summary">{revision.changeSummary}</RevisionRow> : null}
      </dl>

      {revision.history.length > 0 ? (
        <ul className="item-page__history" aria-label="Revision events">
          {revision.history.map((event, index) => (
            <li key={`${event.at}-${event.kind}-${index}`}>
              <time dateTime={event.at}>{formatDate(event.at)}</time> · {event.kind} · {event.actor}
            </li>
          ))}
        </ul>
      ) : null}

      <p className="item-page__revision-note">
        Beta revision history is metadata-level context only. It does not include immutable version snapshots or
        row/feature-level diffs.
      </p>
    </section>
  );
}

function RevisionRow({ label, children }: { label: string; children: React.ReactNode }): JSX.Element {
  return (
    <div className="item-page__row">
      <dt>{label}</dt>
      <dd>{children}</dd>
    </div>
  );
}
