import type { IncomingMessage, ServerResponse } from 'node:http';

export function respondToSharedAdminApi(request: IncomingMessage, response: ServerResponse) {
  const path = new URL(request.url!, 'http://localhost').pathname.replace(/\/$/, '');
  let body: unknown;
  if (path.endsWith('/auth/session')) {
    body = { isAuthenticated: true, claims: [
      { type: 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier', value: 'fixture-operator' },
      { type: 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role', value: 'admin' },
    ] };
  } else if (path.endsWith('/auth/csrf')) {
    body = { requestToken: 'fixture-only' };
  } else if (path.endsWith('/access/first/roles') && request.method === 'GET') {
    body = { success: true, data: { environmentId: 'first', environmentName: 'Synthetic environment',
      permissions: [{ key: 'view', label: 'View' }], roles: [{ id: 'custom', name: 'Synthetic role', isCustom: true, grants: [] }],
      customRoleCount: 1, canManageRoles: true } };
  } else {
    return false;
  }
  response.end(JSON.stringify(body));
  return true;
}
