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
    const transport = session.transport;
    const request = transport?.fetch.bind(transport);
    const nativeTransport = transport ? { fetchRequest: (input,init)=>request(input,init)
        .then(response=>guardNativeResponse(response,()=>transport.assertCurrent())) } : {};
    const maximumDelay = 2147483647;
    return Object.freeze({
        kind: 'cookie', baseUrl: transport ? '/api/v1/studio' : '/api/studio', expiresAt,
        ...nativeTransport,
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

// K3: the legacy host forwarded /api/processes/X to the server's native api/v1/X. Native
// sessions call those routes directly, with the same allowlist and method rules as that host.
const PROCESS_ID='[A-Za-z0-9_-]{1,128}';
const PROCESS_PACKAGE=/^console\/workflow-packages\/([^/]{1,256})\/versions(?:\/([1-9][0-9]{0,9})(?:\/(validate|dry-run|publish))?)?$/;
const PROCESS_ROUTE=new RegExp(`^(?:admin/import/upload|admin/import/jobs/${PROCESS_ID}(?:/cancel)?|admin/connections/${PROCESS_ID}/layers/?`
    +`|console/workflow-node-registry|console/workflow-packages|console/workflow-publications(?:/${PROCESS_ID}/runs)?`
    +`|operations/handles/${PROCESS_ID}|admin/operations/${PROCESS_ID}(?:/cancel)?|admin/proposals/${PROCESS_ID})$`);

export function nativeProcessRoute(input,method='GET') {
    if(typeof input!=='string' || !input.startsWith('/api/processes/') || /[?#]/.test(input)) throw new Error('Unapproved Processes transport route.');
    const path=input.slice('/api/processes/'.length);
    if(path.startsWith('local/')) throw new Error('Local Python and .NET processes are not available on this server yet.');
    const verb=String(method).toUpperCase();
    const post=verb==='POST', get=verb==='GET';
    const pkg=PROCESS_PACKAGE.exec(path);
    let allowed;
    if(pkg) {
        let id;
        try { id=decodeURIComponent(pkg[1]); } catch { id=''; }
        if(!id || id!==id.trim() || /[\u0000-\u001f\u007f\\/]/.test(id) || id==='.' || id==='..') throw new Error('Unapproved Processes transport route.');
        allowed=pkg[3] ? post : pkg[2] ? get : get || post;
    } else {
        if(!PROCESS_ROUTE.test(path)) throw new Error('Unapproved Processes transport route.');
        allowed=path==='admin/import/upload' || path.endsWith('/cancel') || path.endsWith('/runs') ? post
            : path==='console/workflow-packages' ? get || post : get;
    }
    if(!allowed) throw new Error('Unapproved Processes transport method.');
    return `api/v1/${path}`;
}

export function capturedProcessFetch(session,{request=fetch,current=()=>session}={}) {
    const binding={...session};
    return (input,init={})=>{
        if(binding.transport) return nativeProcessFetch({binding,current},input,init);
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

// The Console transport owns cookie/bearer, CSRF and the API origin; the legacy partition and
// environment headers were Console-host checks and are not sent to the native API.
function nativeProcessFetch({binding,current},input,init) {
    const route=nativeProcessRoute(input,init.method);
    const lease=current();
    if(!binding.principalId || !sameCookieAuthority(binding,lease) || lease?.transport?.principalId!==binding.transport.principalId)
        throw new Error('The process connection changed. Reopen it after saving your work.');
    const headers=new Headers(init.headers);
    for(const name of ['X-Honua-Environment','X-Honua-Session-Partition']) headers.delete(name);
    return lease.transport.fetch(route,{...init,headers}).then(response=>guardNativeResponse(response,()=>lease.transport.assertCurrent()));
}

// Embedded editors consume bodies after fetch resolves; check the captured authority through consumption.
function guardNativeResponse(response,assertCurrent) {
    let body;
    return new Proxy(response,{
        get(target,key) {
            if(key==='body') return target.body ? body ??= guardNativeBody(target.body,assertCurrent) : null;
            if(key==='clone') return ()=>guardNativeResponse(target.clone(),assertCurrent);
            if(['json','text','blob','arrayBuffer','formData','bytes'].includes(key)) return async()=>{
                await assertCurrent();
                const value=await target[key]();
                await assertCurrent();
                return value;
            };
            const value=Reflect.get(target,key,target);
            return typeof value==='function' ? value.bind(target) : value;
        },
    });
}

function guardNativeBody(body,assertCurrent) {
    let reader;
    return new ReadableStream({
        async pull(controller) {
            try {
                await assertCurrent();
                reader ??= body.getReader();
                const part=await reader.read();
                await assertCurrent();
                if(part.done) controller.close();
                else controller.enqueue(part.value);
            } catch(error) {
                if(reader) void reader.cancel(error).catch(()=>{});
                controller.error(error);
            }
        },
        cancel(reason) { return reader ? reader.cancel(reason) : body.cancel(reason); },
    },{highWaterMark:0});
}
