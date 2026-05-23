import { useEffect } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";

import { useSession } from "../auth/SessionContext";
import { FIXTURE_PRESETS, setFixtureSession } from "../auth/fixtureDriver";
import { sanitizeReturnTo } from "../auth/returnTo";

export default function SignIn(): JSX.Element {
  const { driverName, refresh, signIn } = useSession();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const presetParam = params.get("preset");
  const returnTo = sanitizeReturnTo(params.get("returnTo"));

  useEffect(() => {
    let cancelled = false;

    async function runSignIn(): Promise<void> {
      if (driverName !== "fixture") {
        await signIn(returnTo);
        return;
      }

      const preset = presetParam && presetParam in FIXTURE_PRESETS ? presetParam : "member";
      setFixtureSession(preset as keyof typeof FIXTURE_PRESETS);
      await refresh();
      if (!cancelled) {
        navigate(returnTo, { replace: true });
      }
    }

    void runSignIn();

    return () => {
      cancelled = true;
    };
  }, [driverName, presetParam, refresh, navigate, returnTo, signIn]);

  return (
    <div className="hc-page">
      <h1 className="hc-page__title">Signing in…</h1>
      <p>Establishing a Honua Console session.</p>
    </div>
  );
}
