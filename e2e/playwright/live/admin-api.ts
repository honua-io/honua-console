import { test as base, expect, request, type APIRequestContext } from '@playwright/test';
import { establishGovernedOperatorSession } from './operator-session';
import { readAdminItems } from './admin-items.mjs';

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
  readonly request: APIRequestContext;
  listConnections(): Promise<AdminConnection[]>;
  findConnectionByName(name: string): Promise<AdminConnection | undefined>;
  createConnection(body: Record<string, unknown>): Promise<AdminConnection>;
  /** Find a connection by name or create it WITHOUT tracking, so it outlives the test (and its retries). */
  getOrCreateKeptConnection(body: Record<string, unknown> & { name: string }): Promise<AdminConnection>;
  deleteConnection(id: string): Promise<void>;
  /** Register a connection name so the fixture deletes it (by lookup) during teardown. */
  trackConnectionName(name: string): void;
  /** Admin layer registry for a service slot (metadata validation). */
  listLayers(connectionId: string, serviceName?: string): Promise<any[]>;
  /** Publish a table as a layer directly (seeds a layer a spec then drives through the UI). */
  publishLayer(connectionId: string, body: Record<string, unknown>): Promise<any>;
  /** PUT arbitrary server JSON (e.g. a service access policy a spec starts from). */
  putJson(path: string, body: Record<string, unknown>): Promise<any>;
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
      request: ctx,
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
        const connection = await postConnection(ctx, body);
        if (typeof body.name === 'string') {
          tracked.add(body.name);
        }
        return connection;
      },
      async getOrCreateKeptConnection(body) {
        return (await this.findConnectionByName(body.name)) ?? postConnection(ctx, body);
      },
      async deleteConnection(id) {
        await ctx.delete(`/api/v1/admin/connections/${id}`);
      },
      trackConnectionName(name) {
        tracked.add(name);
      },
      async listLayers(connectionId, serviceName) {
        const q = serviceName ? `?serviceName=${encodeURIComponent(serviceName)}` : '';
        return readAdminItems(path => this.getJson(path), `/api/v1/admin/connections/${connectionId}/layers/${q}`);
      },
      publishLayer: (connectionId, body) => publishLayer(ctx, connectionId, body),
      putJson: (path, body) => putJson(ctx, path, body),
      async getJson(path) {
        const res = await ctx.get(path);
        expect(res.ok(), `GET ${path} failed: ${res.status()} ${await res.text()}`).toBeTruthy();
        return res.json();
      },
    };

    await use(api);
    await deleteTrackedConnections(api, tracked);
    await ctx.dispose();
  },
});

async function postConnection(ctx: APIRequestContext, body: Record<string, unknown>): Promise<AdminConnection> {
  const res = await ctx.post('/api/v1/admin/connections/', { data: body });
  expect(res.ok(), `create connection failed: ${res.status()} ${await res.text()}`).toBeTruthy();
  return (await res.json()).data as AdminConnection;
}

async function publishLayer(ctx: APIRequestContext, connectionId: string, body: Record<string, unknown>): Promise<any> {
  const res = await ctx.post(`/api/v1/admin/connections/${connectionId}/layers/`, { data: body });
  expect(res.status(), `publish layer failed: ${await res.text()}`).toBe(201);
  return (await res.json()).data;
}

async function putJson(ctx: APIRequestContext, path: string, body: Record<string, unknown>): Promise<any> {
  const res = await ctx.put(path, { data: body });
  expect(res.ok(), `PUT ${path} failed: ${res.status()} ${await res.text()}`).toBeTruthy();
  return res.json();
}

async function deleteTrackedConnections(api: AdminApi, tracked: Set<string>): Promise<void> {
  for (const name of tracked) {
    await deleteConnectionNamed(api, name);
  }
}

// Best effort: a connection still in use, or already gone, must not fail the suite; the testbed
// teardown removes whatever is left.
async function deleteConnectionNamed(api: AdminApi, name: string): Promise<void> {
  try {
    const conn = await api.findConnectionByName(name);
    if (conn) await api.deleteConnection(conn.connectionId);
  } catch {
    return;
  }
}

export { expect };
