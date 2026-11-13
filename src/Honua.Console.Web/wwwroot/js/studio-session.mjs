import { captureConsoleSession } from './console-browser-transport.mjs';

// Studio bootstrap on the native Honua API (K3): GET api/v1/studio/session through the
// Console transport, which owns the cookie/bearer choice and CSRF. No Console host needed.
export class StudioSignInRequired extends Error {}

const SIGN_IN_REQUIRED_STATUSES = new Set([401, 403]);
const nativeAuthorities = new WeakMap();
const ENVIRONMENT_ID = /^[A-Za-z0-9_-]{1,128}$/;
// The native body carries no lease expiry. Callers re-read within this window; a failed
// re-read leaves the old lease to expire, so Studio fails closed rather than open.
export const STUDIO_SESSION_RECHECK_MS = 5 * 60 * 1000;

export async function readStudioSession({ capture = captureConsoleSession, now = Date.now, request = globalThis.fetch,
    mode = globalThis.document?.documentElement?.dataset?.studioTransport ?? 'native' } = {}) {
    if (mode === 'legacy') return readLegacyStudioSession({request,now});
    if (mode !== 'native') throw new Error('Unsupported Studio transport.');
    let authority = nativeAuthorities.get(capture);
    if (!authority) {
        authority = { requested: 0, observed: 0, identity: undefined, generation: 0 };
        nativeAuthorities.set(capture, authority);
    }
    const observation = ++authority.requested;
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
    await transport.assertCurrent();
    // The API can change tenant without changing the Console principal/revision.
    // Revoke every earlier reader when a bootstrap observes a different authority.
    const identity = JSON.stringify([transport.revision, data.principalId, data.environmentId,
        data.sourcePathBase, typeof data.tenantId === 'string' ? data.tenantId : null]);
    if (observation < authority.observed) throw new Error('A newer Studio connection replaced this request.');
    authority.observed = observation;
    if (authority.identity !== identity) { authority.identity = identity; authority.generation++; }
    const generation = authority.generation;
    const captured = transport;
    const assertCurrent = async () => {
        if (authority.generation !== generation) throw new Error('The Studio authority changed. Reopen this view before continuing.');
        await captured.assertCurrent();
        if (authority.generation !== generation) throw new Error('The Studio authority changed. Reopen this view before continuing.');
    };
    transport = Object.freeze({
        principalId: captured.principalId, revision: captured.revision, assertCurrent,
        async fetch(route, init) {
            await assertCurrent();
            const response = await captured.fetch(route, init);
            await assertCurrent();
            return response;
        },
    });
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

async function readLegacyStudioSession({request,now}) {
    const response=await request('/api/agent-workspace/session',{
        credentials:'same-origin',redirect:'error',headers:{Accept:'application/json'},
    });
    if(SIGN_IN_REQUIRED_STATUSES.has(response.status)) throw new StudioSignInRequired('Sign in to Honua to open Studio.');
    if(!response.ok) throw new Error(`The Studio session request failed with ${response.status}.`);
    const data=await response.json();
    if(data?.authenticated!==true || typeof data.principalId!=='string' || !data.principalId
        || typeof data.environmentId!=='string' || !ENVIRONMENT_ID.test(data.environmentId)
        || typeof data.sourcePathBase!=='string' || data.sourcePathBase.endsWith('/')
        || typeof data.csrfToken!=='string' || !data.csrfToken
        || typeof data.expiresAt!=='string' || !(Date.parse(data.expiresAt)>now()))
        throw new Error('The Studio session response is invalid. Reload the page.');
    return Object.freeze(data);
}
