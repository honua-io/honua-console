import { sanitizeReturnTo } from "./returnTo";
import type { Session, SessionDriver } from "./types";

const RETURN_TO_KEY = "honua.console.return-to";

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
