export type EmptyStateKind = "missing" | "unauthorized" | "unsupported" | "invalid" | "conflict" | "server";

interface EmptyStateProps {
  readonly kind: EmptyStateKind;
  readonly title: string;
  readonly description: string;
}

export function EmptyState({ kind, title, description }: EmptyStateProps): JSX.Element {
  return (
    <section className="empty-state" data-testid={`${kind}-state`} data-kind={kind}>
      <p className="eyebrow">{kind}</p>
      <h1>{title}</h1>
      <p>{description}</p>
    </section>
  );
}
