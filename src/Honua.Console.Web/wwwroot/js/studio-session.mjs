import { captureConsoleSession } from './console-browser-transport.mjs';

// Studio bootstrap on the native Honua API (K3): GET api/v1/studio/session through the
// Console transport, which owns the cookie/bearer choice and CSRF. No Console host needed.
export class StudioSignInRequired extends Error {}

const SIGN_IN_REQUIRED_STATUSES = new Set([401, 403]);
const ENVIRONMENT_ID = /^[A-Za-z0-9_-]{1,128}$/;
// The native body carries no lease expiry. Callers re-read within this window; a failed
// re-read leaves the old lease to expire, so Studio fails closed rather than open.
export const STUDIO_SESSION_RECHECK_MS = 5 * 60 * 1000;

export async function readStudioSession({ capture = captureConsoleSession, now = Date.now } = {}) {
    let transport;
    try {
        transport = await capture();
    } catch {
        throw new StudioSignInRequired('Sign in to Honua to open Studio.');
    }
    const response = await transport.fetch('api/v1/studio/session', { headers: { Accept: 'application/json' } });
    if (SIGN_IN_REQUIRED_STATUSES.has(response.status)) throw new StudioSignInRequired('Sign in to Honua to open Studio.');
    if (!response.ok) throw new Error(`The Studio session request failed with ${response.status}.`);
    const data = (await response.json())?.data;
    if (data?.authenticated !== true || typeof data.principalId !== 'string' || !data.principalId
        || typeof data.environmentId !== 'string' || !ENVIRONMENT_ID.test(data.environmentId)
        || typeof data.sourcePathBase !== 'string' || data.sourcePathBase.endsWith('/')) {
        throw new Error('The Studio session response is invalid. Reload the page.');
    }
    return Object.freeze({
        authenticated: true,
        principalId: data.principalId,
        environmentId: data.environmentId,
        environmentName: data.environmentId,
        sourcePathBase: data.sourcePathBase,
        tenantId: typeof data.tenantId === 'string' ? data.tenantId : undefined,
        expiresAt: new Date(now() + STUDIO_SESSION_RECHECK_MS).toISOString(),
        transport,
    });
}
