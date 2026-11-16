import type { IncomingMessage, ServerResponse } from 'node:http';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';

export async function populatedMembersFixture(v4Enabled: boolean) {
  const requests: string[] = [];
  const handleRequest = async (request: IncomingMessage, response: ServerResponse) => {
    response.setHeader('Content-Type', 'application/json');
    const path = new URL(request.url!, 'http://localhost').pathname;
    if (!['GET', 'HEAD'].includes(request.method ?? 'GET')) {
      requests.push(`${request.method} ${path}`);
      response.writeHead(405).end('{}');
    } else if (path.endsWith('/auth/session')) {
      response.end(JSON.stringify({ isAuthenticated: true, claims: [
        { type: 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier', value: 'fixture-operator' },
        { type: 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role', value: 'admin' },
      ] }));
    } else if (path.endsWith('/auth/csrf')) {
      response.end(JSON.stringify({ requestToken: 'fixture-only' }));
    } else if (path.endsWith('/access/first/members') && request.method === 'GET') {
      response.end(JSON.stringify({ success: true, data: {
        workspaceId: 'first', workspaceName: 'RegionalOperationsAndInfrastructurePlanningWorkspace',
        members: [{ id: 'operator', displayName: 'Synthetic operator', identity: 'fixture-operator@example.invalid', roleName: 'Operator', scope: 'RegionalOperationsAndInfrastructurePlanningWorkspace', source: 'OIDC', lastActive: '2026-11-16', principalKind: 'user' }],
        invitations: [{ id: 'invite', identity: 'synthetic-invitation@example.invalid', roleName: 'Operator', scope: 'RegionalOperationsAndInfrastructurePlanningWorkspace', status: 'pending' }],
        activeCount: 1, pendingCount: 1, deactivatedCount: 0, canInvite: true,
      } }));
    } else if (path.endsWith('/access/first/roles') && request.method === 'GET') {
      response.end(JSON.stringify({ success: true, data: { workspaceId: 'first', workspaceName: 'Synthetic workspace',
        permissions: [{ key: 'view', label: 'View' }], roles: [{ id: 'custom', name: 'Synthetic role', isCustom: true, grants: [] }],
        customRoleCount: 1, canManageRoles: true } }));
    } else {
      response.writeHead(404).end('{}');
    }
  };
  const server = await startStaticBundleServer(
    fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 0, {
      configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled },
      handleApi: handleRequest,
    });
  return { server, requests };
}
