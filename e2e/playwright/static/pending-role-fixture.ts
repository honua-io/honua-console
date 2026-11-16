import type { IncomingMessage, ServerResponse } from 'node:http';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';
import { respondToSharedAdminApi } from './shared-admin-fixture';

export async function pendingRoleFixture(v4Enabled: boolean) {
  let releaseCreate: () => void = () => {};
  const createReleased = new Promise<void>(resolve => { releaseCreate = resolve; });
  const requests: string[] = [];
  const handleRequest = async (request: IncomingMessage, response: ServerResponse) => {
    response.setHeader('Content-Type', 'application/json');
    const path = new URL(request.url!, 'http://localhost').pathname;
    if (respondToSharedAdminApi(request, response)) return;
    if (path.endsWith('/access/first/roles/audit') && request.method === 'GET') {
      response.end(JSON.stringify({ success: true, data: { entries: [{
        id: 1, timestamp: '2026-11-16T22:00:00Z', actor: 'fixture-operator',
        action: 'role.created', roleId: 'synthetic-role', outcome: 'success',
      }] } }));
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
