export function mountStudioServerSignIn(host, signInPath) {
    if (typeof signInPath !== 'string' || !signInPath.startsWith('/auth/server/login?')) return;
    let action = host.querySelector('[data-studio-server-sign-in]');
    if (!action) {
        action = host.ownerDocument.createElement('a');
        action.dataset.studioServerSignIn = '';
        action.textContent = 'Sign in to the Server';
        host.append(action);
    }
    action.href = signInPath;
}
