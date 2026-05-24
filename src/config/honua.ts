export function resolveHonuaBaseUrl(explicit: string | undefined): string {
  if (explicit) return explicit;
  const fromEnv = (import.meta.env as Record<string, string | undefined>).VITE_HONUA_BASE_URL;
  if (fromEnv) return fromEnv;
  if (typeof window !== "undefined") return window.location.origin;
  return "http://localhost";
}
