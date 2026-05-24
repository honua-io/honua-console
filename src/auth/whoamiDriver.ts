import { sanitizeReturnTo } from "./returnTo";
import type { Session, SessionDriver } from "./types";

const RETURN_TO_KEY = "honua.portal.return-to";

/**
 * Driver that hydrates the session from the planned `/portal/whoami` server
 * endpoint. The endpoint is being defined in a sibling honua-server ticket;
 * this driver is shipped so that flipping VITE_SESSION_DRIVER=whoami in a
 * deployment is a configuration change, not a code change.
 *
 * sign-in/sign-out are intentionally minimal: they redirect to server-rendered
 * routes so the OIDC handshake stays out of the SPA. A future ticket can swap
 * in oidc-client-ts here without touching the rest of the portal.
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
