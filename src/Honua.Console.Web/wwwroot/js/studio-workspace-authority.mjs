export const studioResourceId = value => typeof value === 'string' && /^[A-Za-z0-9_-]{1,128}$/.test(value);

export function studioAuthority(app) {
    const binding = app.workspaceCookieBinding;
    if (!binding?.principalId || !binding.environmentId) throw new Error('Reconnect Studio before continuing.');
    const identity = [binding.principalId, binding.environmentId, binding.sourcePathBase || ''];
    // Keep existing recovery keys for sessions without tenant identity.
    if (binding.tenantId !== undefined && binding.tenantId !== null) identity.push(binding.tenantId);
    return JSON.stringify(identity);
}
