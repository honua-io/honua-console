import { test as base, expect, request, type APIRequestContext } from '@playwright/test';
import { establishGovernedOperatorSession } from './operator-session';

// Shared live fixtures.
//
// `page` completes the governed server/Console operator session before the test body when the
// setup project's storage state is not already on the context. Specs that import `test` from
// @playwright/test still receive that session: playwright.live.config.ts applies the same
// storage state to the chromium project.
//
// `admin` is the independent honua-server admin API. It uses the service key only on this
// direct client, never as a browser operator. The Console host is started without that key.

const SERVER_URL = process.env.HONUA_CONSOLE_E2E_SERVER_URL ?? 'http://127.0.0.1:8088';
const ADMIN_KEY = process.env.HONUA_CONSOLE_E2E_ADMIN_KEY ?? 'honua-console-dev-key';

export interface AdminConnection {
  connectionId: string;
  name: string;
  provider?: string;
  host?: string;
  databaseName?: string;
  healthStatus?: string;
  storageType?: string;
}

export interface AdminApi {
  readonly serverUrl: string;
  listConnections(): Promise<AdminConnection[]>;
  findConnectionByName(name: string): Promise<AdminConnection | undefined>;
  createConnection(body: Record<string, unknown>): Promise<AdminConnection>;
  deleteConnection(id: string): Promise<void>;
  /** Register a connection name so the fixture deletes it (by lookup) during teardown. */
  trackConnectionName(name: string): void;
  /** Admin layer registry for a service slot (metadata validation). */
  listLayers(connectionId: string, serviceName?: string): Promise<any[]>;
  /** GET arbitrary server JSON (e.g. GeoServices /rest/services catalog or a FeatureServer /query). */
  getJson(path: string): Promise<any>;
}

export const test = base.extend<{ admin: AdminApi }>({
  page: async ({ page }, use) => {
    await establishGovernedOperatorSession(page);
    await use(page);
  },
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
      async listLayers(connectionId, serviceName) {
        const q = serviceName ? `?serviceName=${encodeURIComponent(serviceName)}` : '';
        const res = await ctx.get(`/api/v1/admin/connections/${connectionId}/layers/${q}`);
        expect(res.ok(), `list layers failed: ${res.status()}`).toBeTruthy();
        const body = await res.json();
        return (body.data ?? []) as any[];
      },
      async getJson(path) {
        const res = await ctx.get(path);
        expect(res.ok(), `GET ${path} failed: ${res.status()} ${await res.text()}`).toBeTruthy();
        return res.json();
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
