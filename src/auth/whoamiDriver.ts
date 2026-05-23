import { sanitizeReturnTo } from "./returnTo";
import type { Session, SessionDriver } from "./types";

const RETURN_TO_KEY = "honua.console.return-to";

/**
 * Driver that hydrates the session from a server-rendered `whoami` endpoint.
 *
 * The endpoint itself is being defined in honua-server#1162; this driver is
 * shipped so that flipping `VITE_AUTH_DRIVER=whoami` in a deployment is a
 * configuration change, not a code change. Sign-in/sign-out redirect to
 * server-rendered routes so the OIDC handshake stays out of the SPA — a
 * future ticket can swap in oidc-client-ts here without touching the rest
 * of the console.
 */
export function createWhoamiDriver(whoamiUrl: string): SessionDriver {
  return {
    name: "whoami",
    async probe(): Promise<Session> {
      try {
        const response = await fetch(whoamiUrl, {
          credentials: "include",
          headers: { Accept: "application/json" },
        });
        if (response.status === 401 || response.status === 403) {
          return { status: "unauthenticated" };
        }
        if (response.status === 501) {
          // Honua Server has not shipped the endpoint yet (honua-server#1162).
          // Surface that explicitly so the shell can degrade to a clear message.
          return { status: "error", message: "Session endpoint not yet available" };
        }
        if (!response.ok) {
          return { status: "error", message: `whoami failed: ${response.status}` };
        }
        const payload = (await response.json()) as {
          user: { id: string; displayName: string; email: string };
          workspace: { id: string; name: string };
          scopes: string[];
          accessToken?: string;
        };
        return {
          status: "authenticated",
          user: payload.user,
          workspace: payload.workspace,
          scopes: payload.scopes,
          accessToken: payload.accessToken,
        };
      } catch (error) {
        const message = error instanceof Error ? error.message : "whoami probe failed";
        return { status: "error", message };
      }
    },
    async signIn(returnTo: string): Promise<void> {
      const safeReturnTo = sanitizeReturnTo(returnTo);
      try {
        window.sessionStorage.setItem(RETURN_TO_KEY, safeReturnTo);
      } catch {
        // Best-effort.
      }
      const target = new URL("/auth/signin", window.location.origin);
      target.searchParams.set("returnTo", safeReturnTo);
      window.location.assign(target.toString());
    },
    async signOut(): Promise<void> {
      window.location.assign("/auth/signout");
    },
  };
}
