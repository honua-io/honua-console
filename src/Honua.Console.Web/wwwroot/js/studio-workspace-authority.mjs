export const studioResourceId = value => typeof value === 'string' && /^[A-Za-z0-9_-]{1,128}$/.test(value);

export function studioAuthority(app) {
    const binding = app.workspaceCookieBinding;
    if (!binding?.principalId || !binding.environmentId) throw new Error('Reconnect Studio before continuing.');
    return JSON.stringify([binding.principalId, binding.environmentId, binding.sourcePathBase || '']);
}
