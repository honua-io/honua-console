/**
 * Session contract surfaced to the Honua Console shell.
 *
 * This is a temporary shim. honua-console#7 wires Console to the shared
 * metadata/content/RBAC contracts; at that point this file becomes a
 * re-export of the canonical Session type from `@honua/sdk-js`
 * (`/contract` or the eventual `/console` subpath from honua-sdk-js#225)
 * and the local interfaces below are removed.
 *
 * Until then, `permissions.ts` only reads `scopes: string[]`, which is the
 * minimal surface every plausible session contract will expose.
 */

export type Scope = "member" | "operator" | "admin" | (string & {});

export interface SessionUser {
  id: string;
  displayName: string;
  email: string;
}

export interface SessionWorkspace {
  id: string;
  name: string;
}

export interface AuthenticatedSession {
  status: "authenticated";
  user: SessionUser;
  workspace: SessionWorkspace;
  scopes: Scope[];
  /**
   * Optional bearer token. The shell does not depend on a token to render, but
   * the api client will attach it when present.
   */
  accessToken?: string;
}

export interface UnauthenticatedSession {
  status: "unauthenticated";
}

export interface LoadingSession {
  status: "loading";
}

export interface ErroredSession {
  status: "error";
  message: string;
}

export type Session = LoadingSession | UnauthenticatedSession | AuthenticatedSession | ErroredSession;

/**
 * A session driver knows how to (a) probe the current session and
 * (b) initiate sign-in / sign-out. Drivers live next to this file
 * (`fixtureDriver`, `whoamiDriver`).
 */
export interface SessionDriver {
  readonly name: string;
  probe(): Promise<Session>;
  signIn(returnTo: string): Promise<void>;
  signOut(): Promise<void>;
}
