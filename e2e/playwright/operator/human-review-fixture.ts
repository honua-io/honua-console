import type { IncomingMessage, ServerResponse } from 'node:http';
import { randomUUID } from 'node:crypto';

export const humanSessionCookie = 'operator-fixture-private-session';

export function serveHumanSignIn(request: IncomingMessage, response: ServerResponse, consoleOrigin: string) {
  const url = new URL(request.url!, 'http://127.0.0.1:5198');
  const expiresAt = new Date(Date.now() + 60 * 60 * 1000).toISOString();
  if (url.pathname === '/api/v1/admin/auth/config') {
    response.end(JSON.stringify({ oidcEnabled: true, providers: [{ key: 'fixture', displayName: 'Fixture' }] }));
  } else if (url.pathname === '/api/v1/admin/auth/providers/fixture/authorize-url') {
    response.end(JSON.stringify({ authorizeUrl: `http://127.0.0.1:5198/authorize?state=${randomUUID()}` }));
  } else if (url.pathname === '/authorize') {
    const callback = new URL('/admin/auth/callback', consoleOrigin);
    callback.searchParams.set('code', 'operator-fixture-code');
    callback.searchParams.set('state', url.searchParams.get('state')!);
    response.writeHead(302, { Location: callback.href }); response.end();
  } else if (url.pathname === '/api/v1/admin/auth/providers/fixture/token') {
    response.setHeader('Set-Cookie', `honua_admin_session=${humanSessionCookie}; Path=/; HttpOnly`);
    response.end('{}');
  } else if (url.pathname === '/api/v1/admin/auth/session' || url.pathname === '/api/v1/admin/auth/bearer') {
    if (!request.headers.cookie?.includes(`honua_admin_session=${humanSessionCookie}`)) {
      response.writeHead(401); response.end('{}'); return true;
    }
    response.end(JSON.stringify(url.pathname.endsWith('/session')
      ? { isAuthenticated: true, providerKey: 'fixture', expiresAt, claims: [] }
      : { accessToken: 'alice-valid', tokenType: 'Bearer', expiresAt }));
  } else return false;
  return true;
}
