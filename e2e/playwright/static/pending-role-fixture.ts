import type { IncomingMessage, ServerResponse } from 'node:http';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';

export async function pendingRoleFixture(v4Enabled: boolean) {
  let releaseCreate: () => void = () => {};
  const createReleased = new Promise<void>(resolve => { releaseCreate = resolve; });
  const requests: string[] = [];
  const handleRequest = async (request: IncomingMessage, response: ServerResponse) => {
    response.setHeader('Content-Type', 'application/json');
    const path = new URL(request.url!, 'http://localhost').pathname;
    if (path.endsWith('/auth/session')) {
      response.end(JSON.stringify({ isAuthenticated: true, claims: [
        { type: 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier', value: 'fixture-operator' },
        { type: 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role', value: 'admin' },
      ] }));
    } else if (path.endsWith('/auth/csrf')) {
      response.end(JSON.stringify({ requestToken: 'fixture-only' }));
    } else if (path.endsWith('/access/first/roles/audit') && request.method === 'GET') {
      response.end(JSON.stringify({ success: true, data: { entries: [{
        id: 1, timestamp: '2026-11-16T22:00:00Z', actor: 'fixture-operator',
        action: 'role.created', roleId: 'synthetic-role', outcome: 'success',
      }] } }));
    } else if (path.endsWith('/access/first/roles') && request.method === 'GET') {
      response.end(JSON.stringify({ success: true, data: { workspaceId: 'first', workspaceName: 'Synthetic workspace',
        permissions: [{ key: 'view', label: 'View' }], roles: [{ id: 'custom', name: 'Synthetic role', isCustom: true, grants: [] }],
        customRoleCount: 1, canManageRoles: true } }));
    } else if (path.endsWith('/access/first/roles') && request.method === 'POST') {
      requests.push(path);
      await createReleased;
      response.writeHead(503).end('{}');
    } else {
      response.writeHead(404).end('{}');
    }
  };
  const server = await startStaticBundleServer(
    fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 0, {
      configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled },
      handleApi: handleRequest,
    });
  return { server, requests, releaseCreate };
}
