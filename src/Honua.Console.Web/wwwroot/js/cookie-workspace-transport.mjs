// Human transport only. Renew the lease without borrowing another view's authority.
export function sameCookieAuthority(left,right) {
    return ['principalId','environmentId','sourcePathBase'].every(key=>left?.[key]===right?.[key]);
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
