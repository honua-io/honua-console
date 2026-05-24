/**
 * Session contract surfaced to the portal shell.
 *
 * The shape mirrors the payload expected from the planned
 * `GET /portal/whoami` server endpoint so that any session driver
 * (fixture, whoami, future OIDC adapter) can settle on the same type.
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
   * Optional bearer token. The portal does not depend on a token to render the
   * shell, but the api client will attach it when present.
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
 * (b) initiate sign-in / sign-out. Concrete drivers live next to this
 * file (fixture today, whoami/oidc to follow).
 */
export interface SessionDriver {
  readonly name: string;
  probe(): Promise<Session>;
  signIn(returnTo: string): Promise<void>;
  signOut(): Promise<void>;
}
