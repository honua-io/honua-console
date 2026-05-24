import { EmptyState } from "./EmptyState";

interface ForbiddenProps {
  /** Sentence-case description of what permission is missing. */
  reason?: string;
  /** Optional CTA, e.g. a contact link. */
  action?: React.ReactNode;
}

export function Forbidden({ reason, action }: ForbiddenProps): JSX.Element {
  return (
    <EmptyState
      tone="warning"
      title="You don't have access to this view"
      description={reason ?? "Ask a workspace operator to grant you the required permission, then refresh this page."}
      primaryAction={action}
    />
  );
}
