import type { AuthenticatedSession, SessionDriver, UnauthenticatedSession } from "../src/auth/types";

export const memberSession: AuthenticatedSession = {
  status: "authenticated",
  user: { id: "u-member", displayName: "Mira Chen", email: "mira@demo.honua.example" },
  workspace: { id: "w-acme", name: "Acme Geospatial" },
  scopes: ["member"],
};

export const operatorSession: AuthenticatedSession = {
  status: "authenticated",
  user: { id: "u-operator", displayName: "Owen Park", email: "owen@demo.honua.example" },
  workspace: { id: "w-acme", name: "Acme Geospatial" },
  scopes: ["member", "operator"],
};

export const unauthenticatedSession: UnauthenticatedSession = { status: "unauthenticated" };

export function staticDriver(session: AuthenticatedSession | UnauthenticatedSession): SessionDriver {
  return {
    name: "test",
    async probe() {
      return session;
    },
    async signIn() {
      // no-op in tests
    },
    async signOut() {
      // no-op in tests
    },
  };
}

export class MemoryStorage implements Storage {
  private readonly values = new Map<string, string>();

  get length(): number {
    return this.values.size;
  }

  clear(): void {
    this.values.clear();
  }

  getItem(key: string): string | null {
    return this.values.get(key) ?? null;
  }

  key(index: number): string | null {
    return Array.from(this.values.keys())[index] ?? null;
  }

  removeItem(key: string): void {
    this.values.delete(key);
  }

  setItem(key: string, value: string): void {
    this.values.set(key, value);
  }
}
