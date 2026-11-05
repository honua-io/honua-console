{
    const host = document.querySelector('[data-agent-workspace]');
    const narrow = matchMedia('(max-width: 1000px)');
    const toggle = () => document.querySelector('[data-conversation-toggle]');
    let lastPath;
    let lastAuthorityRevision;
    const modal = honuaModalSurface.create({
        surface: () => host,
        backgrounds: () => [...document.body.children].filter(element => element !== host
            && !element.matches('[data-conversation-backdrop], script')),
        returnFocus: toggle,
        onDismiss: close
    });
    function isStudio(path) {
        return /^\/studio(?:\/?|\/proof\/?|\/drafts\/?|\/apps\/[^/]+\/preview\/?)$/.test(path || '');
    }
    function syncPresentation() {
        if (!host) return;
        toggle()?.setAttribute('aria-expanded', String(host.open));
        document.body.classList.toggle('console-conversation-open', host.open);
        const mobileOpen = host.open && narrow.matches;
        document.querySelector('[data-conversation-backdrop]')?.toggleAttribute('hidden', !mobileOpen);
        if (mobileOpen) {
            host.setAttribute('role', 'dialog');
            host.setAttribute('aria-modal', 'true');
            host.setAttribute('aria-label', 'Conversation');
            modal.activate();
            if (!host.contains(document.activeElement)) host.querySelector('[data-workspace-prompt]')?.focus();
        } else {
            host.removeAttribute('role'); host.removeAttribute('aria-modal'); host.removeAttribute('aria-label');
            modal.release();
        }
    }
    function open() {
        if (!host || !document.querySelector('[data-console-location]')) return;
        host.open = true;
        syncPresentation();
        host.querySelector('[data-workspace-prompt]')?.focus();
    }
    function close() {
        if (!host) return;
        const restoreFocus = host.contains(document.activeElement);
        modal.release();
        host.open = false;
        syncPresentation();
        if (restoreFocus) toggle()?.focus();
    }
    function reconcileAuthority(marker) {
        const revision = marker?.dataset.consoleAuthorityRevision;
        if (revision === lastAuthorityRevision) return;
        lastAuthorityRevision = revision;
        window.dispatchEvent(new CustomEvent('honua-console-authority'));
    }
    function reconcileRoute() {
        const marker = document.querySelector('[data-console-location]');
        const path = marker?.dataset.consoleLocation;
        reconcileAuthority(marker);
        if (path === lastPath) { syncPresentation(); return; }
        const entering = isStudio(path) && !isStudio(lastPath);
        if (!isStudio(path) || narrow.matches) close();
        lastPath = path;
        if (entering) open();
        window.dispatchEvent(new CustomEvent('honua-console-navigation'));
        syncPresentation();
    }
    host?.addEventListener('toggle', syncPresentation);
    document.addEventListener('click', event => {
        if (event.target.closest('[data-conversation-close], [data-conversation-backdrop]')) close();
        else if (event.target.closest('[data-conversation-toggle]')) { if (host?.open) close(); else open(); }
        else if (event.target.closest('[data-workspace-open]')) open();
    });
    document.addEventListener('keydown', event => {
        if (event.defaultPrevented) return;
        if (event.key === 'Escape' && host?.open && (narrow.matches || host.contains(event.target))) {
            event.preventDefault(); close(); toggle()?.focus();
        } else modal.trapFocus(event);
    });
    narrow.addEventListener('change', syncPresentation);
    new MutationObserver(reconcileRoute).observe(document.body, {subtree: true, childList: true,
        attributes: true, attributeFilter: ['data-console-location', 'data-console-authority-revision']});
    reconcileRoute();
}
