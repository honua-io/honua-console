import { test as base, expect, request, type APIRequestContext } from '@playwright/test';

// honua-server admin API used by the live e2e specs to (a) seed/verify state independently of the
// Console UI under test, and (b) clean up everything a spec created. Mirrors the ServerStateVerifier
// discipline from docs/testing/console-integration-test-plan.md: assert results through a different
// path than the operation went through.

const SERVER_URL = process.env.HONUA_CONSOLE_E2E_SERVER_URL ?? 'http://127.0.0.1:8088';
const ADMIN_KEY = process.env.HONUA_CONSOLE_E2E_ADMIN_KEY ?? 'honua-console-dev-key';

export interface AdminConnection {
  connectionId: string;
  name: string;
  provider?: string;
  host?: string;
  databaseName?: string;
  healthStatus?: string;
}

export interface AdminApi {
  readonly serverUrl: string;
  listConnections(): Promise<AdminConnection[]>;
  findConnectionByName(name: string): Promise<AdminConnection | undefined>;
  createConnection(body: Record<string, unknown>): Promise<AdminConnection>;
  deleteConnection(id: string): Promise<void>;
  /** Register a connection name so the fixture deletes it (by lookup) during teardown. */
  trackConnectionName(name: string): void;
}

export const test = base.extend<{ admin: AdminApi }>({
  admin: async ({ playwright }, use) => {
    const ctx: APIRequestContext = await request.newContext({
      baseURL: SERVER_URL,
      extraHTTPHeaders: { 'X-API-Key': ADMIN_KEY },
    });
    const tracked = new Set<string>();

    const api: AdminApi = {
      serverUrl: SERVER_URL,
      async listConnections() {
        const res = await ctx.get('/api/v1/admin/connections/');
        expect(res.ok(), `list connections failed: ${res.status()}`).toBeTruthy();
        const body = await res.json();
        return (body.data ?? []) as AdminConnection[];
      },
      async findConnectionByName(name) {
        return (await this.listConnections()).find((c) => c.name === name);
      },
      async createConnection(body) {
        const res = await ctx.post('/api/v1/admin/connections/', { data: body });
        expect(res.ok(), `create connection failed: ${res.status()} ${await res.text()}`).toBeTruthy();
        const json = await res.json();
        if (typeof body.name === 'string') {
          tracked.add(body.name);
        }
        return json.data as AdminConnection;
      },
      async deleteConnection(id) {
        await ctx.delete(`/api/v1/admin/connections/${id}`);
      },
      trackConnectionName(name) {
        tracked.add(name);
      },
    };

    await use(api);

    // Teardown: delete every connection a spec created (best-effort; ignore in-use/404).
    for (const name of tracked) {
      try {
        const conn = await api.findConnectionByName(name);
        if (conn) {
          await api.deleteConnection(conn.connectionId);
        }
      } catch {
        // Leave cleanup failures non-fatal so one stuck row doesn't fail the suite.
      }
    }
    await ctx.dispose();
  },
});

export { expect };
