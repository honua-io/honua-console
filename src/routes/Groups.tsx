import { useSession } from "../auth/SessionContext";
import { hasAnyScope } from "../auth/permissions";
import { EmptyState } from "../shell/EmptyState";
import { Forbidden } from "../shell/Forbidden";

export default function Groups(): JSX.Element {
  const { session } = useSession();
  // Groups is a workspace-member surface; users without the member scope see a
  // permission-aware empty state instead of an empty list.
  if (!hasAnyScope(session, ["member", "operator", "admin"])) {
    return (
      <div className="hc-page">
        <header className="hc-page__header">
          <h1 className="hc-page__title">Groups</h1>
        </header>
        <Forbidden reason="Groups require workspace membership. Ask a workspace operator to add you, then refresh." />
      </div>
    );
  }
  return (
    <div className="hc-page">
      <header className="hc-page__header">
        <h1 className="hc-page__title">Groups</h1>
        <p className="hc-page__subtitle">Shared collections of maps, layers, and members.</p>
      </header>
      <EmptyState
        title="You're not a member of any groups yet"
        description="Group browsing and sharing controls land with honua-console#7. The shell already routes here so deep links remain valid."
      />
    </div>
  );
}
