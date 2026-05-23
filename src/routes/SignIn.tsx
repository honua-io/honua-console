import { useEffect } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";

import { useSession } from "../auth/SessionContext";
import { FIXTURE_PRESETS, setFixtureSession } from "../auth/fixtureDriver";
import { sanitizeReturnTo } from "../auth/returnTo";

export default function SignIn(): JSX.Element {
  const { refresh } = useSession();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const returnTo = sanitizeReturnTo(params.get("returnTo"));

  useEffect(() => {
    // Console scaffold ships with a fixture-only auth path; auto-pick member
    // unless the URL specifies a preset. OIDC wiring is a follow-up ticket.
    const presetParam = params.get("preset");
    const preset = presetParam && presetParam in FIXTURE_PRESETS ? presetParam : "member";
    setFixtureSession(preset as keyof typeof FIXTURE_PRESETS);
    void refresh().then(() => {
      navigate(returnTo, { replace: true });
    });
  }, [params, refresh, navigate, returnTo]);

  return (
    <div className="hc-page">
      <h1 className="hc-page__title">Signing in…</h1>
      <p>Establishing a Honua Console session.</p>
    </div>
  );
}
