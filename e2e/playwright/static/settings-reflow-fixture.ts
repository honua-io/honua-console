import type { IncomingMessage, ServerResponse } from 'node:http';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';

function settingsData(path: string) {
  return path.endsWith('/api-keys') ? [{
    id: '4e386945-c5f5-45bf-8d4a-2fed5a3d7b62', name: 'Synthetic integration key',
    keyPrefix: 'fixture', permissions: ['admin:read'], status: 'active',
    createdAt: '2026-11-01T00:00:00Z', updatedAt: '2026-11-01T00:00:00Z',
  }] : path.endsWith('/version') ? { version: 'fixture', metadataApiVersion: '2' }
    : path.endsWith('/capabilities') ? { metadataSchemaVersion: '2' }
      : path.endsWith('/license') ? { edition: 'Fixture', isValid: true, entitlements: [] }
        : path.endsWith('/oidc/providers') ? [] : null;
}

function respondToSettingsApi(request: IncomingMessage, response: ServerResponse, mutations: string[]) {
  response.setHeader('Content-Type', 'application/json');
  if (request.method !== 'GET') mutations.push(`${request.method} ${request.url}`);
  const path = new URL(request.url!, 'http://localhost').pathname.replace(/\/$/, '');
  if (path.endsWith('/auth/session')) {
    response.end(JSON.stringify({ isAuthenticated: true, claims: [
      { type: 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier', value: 'fixture-operator' },
      { type: 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role', value: 'admin' },
    ] }));
  } else if (path.endsWith('/auth/csrf')) {
    response.end(JSON.stringify({ requestToken: 'fixture-only' }));
  } else {
    const data = settingsData(path);
    if (data !== null) response.end(JSON.stringify({ success: true, data }));
    else response.writeHead(404).end('{}');
  }
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
