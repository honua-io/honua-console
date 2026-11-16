import type { IncomingMessage, ServerResponse } from 'node:http';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';
import { respondToSharedAdminApi } from './shared-admin-fixture';

function settingsData(path: string) {
  if (path.endsWith('/api-keys')) return [{
    id: '4e386945-c5f5-45bf-8d4a-2fed5a3d7b62', name: 'Synthetic integration key',
    keyPrefix: 'fixture', permissions: ['admin:read'], status: 'active',
    createdAt: '2026-11-01T00:00:00Z', updatedAt: '2026-11-01T00:00:00Z',
  }];
  if (path.endsWith('/version')) return { version: 'fixture', metadataApiVersion: '2' };
  if (path.endsWith('/capabilities')) return { metadataSchemaVersion: '2' };
  if (path.endsWith('/license')) return { edition: 'Fixture', isValid: true, entitlements: [] };
  if (path.endsWith('/oidc/providers')) return [];
  return null;
}

function respondToSettingsApi(request: IncomingMessage, response: ServerResponse, mutations: string[]) {
  response.setHeader('Content-Type', 'application/json');
  if (request.method !== 'GET') mutations.push(`${request.method} ${request.url}`);
  const path = new URL(request.url!, 'http://localhost').pathname.replace(/\/$/, '');
  if (respondToSharedAdminApi(request, response)) return;
  const data = settingsData(path);
  if (data !== null) response.end(JSON.stringify({ success: true, data }));
  else response.writeHead(404).end('{}');
}

export async function settingsFixture(options: { v4Enabled: boolean }) {
  const mutations: string[] = [];
  const server = await startStaticBundleServer(
    fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 0, {
      configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: options.v4Enabled },
      handleApi: (request, response) => respondToSettingsApi(request, response, mutations),
    });

  return { server, mutations };
}
