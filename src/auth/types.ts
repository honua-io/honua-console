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

export interface SessionDriver {
  readonly name: string;
  probe(): Promise<Session>;
  signIn(returnTo: string): Promise<void>;
  signOut(): Promise<void>;
}
