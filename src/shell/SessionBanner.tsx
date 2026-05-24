import { useSession } from "../session/SessionProvider";

export function SessionBanner(): JSX.Element {
  const { status } = useSession();
  if (status.kind === "loading") {
    return <div className="session-banner">Loading session…</div>;
  }
  if (status.kind === "anonymous") {
    return <div className="session-banner">Anonymous session — sign in for full Console capabilities.</div>;
  }
  if (status.kind === "error") {
    return <div className="session-banner">Session bootstrap failed: {status.message}</div>;
  }
  return (
    <div className="session-banner">
      Signed in as {status.identity.displayName}
      {status.workspace ? ` · ${status.workspace.name}` : null}
    </div>
  );
}
