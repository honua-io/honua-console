import { useEffect, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";

import { useSession } from "../auth/SessionContext";
import { FIXTURE_PRESETS, consumeReturnTo, setFixtureSession } from "../auth/fixtureDriver";
import { sanitizeReturnTo } from "../auth/returnTo";

import "./auth.css";

const PRESET_OPTIONS = Object.entries(FIXTURE_PRESETS).map(([key, session]) => ({
  key,
  label: session.user.displayName,
  scopes: session.scopes.join(", "),
  email: session.user.email,
}));

export default function SignIn(): JSX.Element {
  const { session, refresh, driverName } = useSession();
  const navigate = useNavigate();
  const [search] = useSearchParams();
  const [working, setWorking] = useState(false);
  const queryReturn = search.get("returnTo");
  const oidcReturnTo = queryReturn ? sanitizeReturnTo(queryReturn) : null;
  const presetParam = search.get("as");

  // If a preset was requested via ?as=member|operator (used by smoke tests
  // and link-shared dev sessions), apply it and probe.
  useEffect(() => {
    if (driverName !== "fixture") return;
    if (!presetParam) return;
    if (!FIXTURE_PRESETS[presetParam]) return;
    setFixtureSession(presetParam);
    void refresh();
  }, [driverName, presetParam, refresh]);

  // Once authenticated, send the user back to where they came from.
  useEffect(() => {
    if (session.status === "authenticated") {
      const returnTo = sanitizeReturnTo(queryReturn ?? consumeReturnTo());
      navigate(returnTo, { replace: true });
    }
  }, [session.status, navigate, queryReturn]);

  async function activatePreset(preset: string): Promise<void> {
    if (driverName !== "fixture") return;
    setWorking(true);
    try {
      setFixtureSession(preset);
      await refresh();
    } finally {
      setWorking(false);
    }
  }

  return (
    <main className="hc-auth-page" id="hc-main" tabIndex={-1}>
      <div className="hc-auth-card">
        <h1 className="hc-auth-card__title">Sign in to Honua Console</h1>
        {driverName === "fixture" ? (
          <>
            <p className="hc-auth-card__lede">
              Console is running with the development fixture session driver. Pick a profile to enter the workspace.
              Real OIDC sign-in lands once <code>GET /console/whoami</code> ships on
              <a href="https://github.com/honua-io/honua-server"> honua-server</a> (tracked by honua-console#7).
            </p>
            <ul className="hc-auth-card__presets" data-testid="signin-preset-list">
              {PRESET_OPTIONS.map((preset) => (
                <li key={preset.key} className="hc-auth-card__preset">
                  <button
                    type="button"
                    className="hc-btn hc-btn--primary"
                    onClick={() => {
                      void activatePreset(preset.key);
                    }}
                    disabled={working}
                    data-testid={`signin-as-${preset.key}`}
                  >
                    Continue as {preset.label}
                  </button>
                  <div className="hc-auth-card__preset-meta">
                    <span>{preset.email}</span>
                    <span>scopes: {preset.scopes}</span>
                  </div>
                </li>
              ))}
            </ul>
          </>
        ) : (
          <>
            <p className="hc-auth-card__lede">
              Console is configured to talk to the server <code>/console/whoami</code> endpoint. Hit that endpoint via
              the configured identity provider and return here.
            </p>
            <a
              className="hc-btn hc-btn--primary"
              href={oidcReturnTo ? `/auth/oidc/login?returnTo=${encodeURIComponent(oidcReturnTo)}` : "/auth/oidc/login"}
            >
              Continue with single sign-on
            </a>
          </>
        )}
      </div>
    </main>
  );
}
