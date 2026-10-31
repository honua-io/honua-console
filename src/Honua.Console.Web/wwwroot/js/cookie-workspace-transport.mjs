// Human transport only. Renew the lease without borrowing another view's authority.
export function sameCookieAuthority(left,right) {
    return ['principalId','environmentId','sourcePathBase'].every(key=>left?.[key]===right?.[key]);
}

// Rebind before the captured lease expires, including short server bearers. The
// caller still owns authority/view checks; this loop only schedules its renewal.
export function scheduleCookieSessionRenewal(current, renew) {
    let stopped = false, timer;
    const schedule = () => {
        if (stopped) return;
        const remaining = Date.parse(current()?.expiresAt || '') - Date.now();
        const delay = Number.isFinite(remaining) ? Math.max(1000, Math.min(60000, remaining / 2)) : 1000;
        timer = setTimeout(async () => {
            timer = undefined;
            try { await renew(); }
            catch { /* Retry the same captured authority; expiry remains fail closed. */ }
            finally { schedule(); }
        }, delay);
    };
    schedule();
    return () => { stopped = true; if (timer !== undefined) clearTimeout(timer); };
}

/** The Studio auth session disposes this subscription when its cookie lease is replaced. */
export function createStudioCookieSession(session) {
    const expiresAt = session?.expiresAt;
    const deadline = typeof expiresAt === 'string' ? Date.parse(expiresAt) : NaN;
    if (!Number.isFinite(deadline)) throw new Error('The Studio cookie lease expiry is invalid. Reconnect before opening Studio.');
    const maximumDelay = 2147483647;
    return Object.freeze({
        kind: 'cookie', baseUrl: '/api/studio', expiresAt,
        csrfToken: session.csrfToken, environmentId: session.environmentId,
        principalId: session.principalId, sourcePathBase: session.sourcePathBase,
        onExpired(listener) {
            if (typeof listener !== 'function') throw new TypeError('A Studio expiry listener is required.');
            let cancelled = false;
            let timer;
            const check = () => {
                if (cancelled) return;
                const remaining = deadline - Date.now();
                if (remaining > 0) {
                    timer = setTimeout(check, Math.min(remaining, maximumDelay));
                    return;
                }
                cancelled = true;
                timer = undefined;
                listener();
            };
            timer = setTimeout(check, Math.max(0, Math.min(deadline - Date.now(), maximumDelay)));
            return () => {
                cancelled = true;
                if (timer !== undefined) clearTimeout(timer);
                timer = undefined;
            };
        },
    });
}

export function capturedProcessFetch(session,{request=fetch,current=()=>session}={}) {
    const binding={...session};
    return (input,init={})=>{
        if(typeof input!=='string' || !input.startsWith('/api/processes/')) throw new Error('Unapproved Processes transport route.');
        const lease=current();
        if(!binding.principalId || !sameCookieAuthority(binding,lease)) throw new Error('The process connection changed. Reopen it after saving your work.');
        const headers=new Headers(init.headers);
        headers.set('X-Honua-Environment',binding.environmentId);
        headers.set('X-Honua-Session-Partition',binding.principalId);
        headers.set('X-CSRF-Token',lease.csrfToken);
        return request(input,{...init,credentials:'same-origin',headers});
    };
}
