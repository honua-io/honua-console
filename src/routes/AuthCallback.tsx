import { useEffect } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";

import { useSession } from "../auth/SessionContext";
import { sanitizeReturnTo } from "../auth/returnTo";
import { LoadingShell } from "../shell/LoadingShell";

import "./auth.css";

export default function AuthCallback(): JSX.Element {
  const { refresh, session } = useSession();
  const navigate = useNavigate();
  const [search] = useSearchParams();
  const returnTo = sanitizeReturnTo(search.get("returnTo"));

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    if (session.status === "authenticated") {
      navigate(returnTo, { replace: true });
    } else if (session.status === "unauthenticated" || session.status === "error") {
      navigate("/auth/signin", { replace: true });
    }
  }, [session.status, navigate, returnTo]);

  return (
    <main className="hc-auth-page" id="hc-main" tabIndex={-1}>
      <div className="hc-auth-card">
        <h1 className="hc-auth-card__title">Finishing sign-in…</h1>
        <LoadingShell label="Completing authentication" />
      </div>
    </main>
  );
}
