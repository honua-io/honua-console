import { EmptyState } from "../shell/EmptyState";

export default function Data(): JSX.Element {
  return (
    <div className="hc-page">
      <header className="hc-page__header">
        <h1 className="hc-page__title">Data</h1>
        <p className="hc-page__subtitle">Datasets, layers, and tabular content.</p>
      </header>
      <EmptyState
        title="Data view is coming soon"
        description="Dataset detail pages and tabular inspection land with honua-console#7. The shell already routes here so deep links remain valid."
      />
    </div>
  );
}
