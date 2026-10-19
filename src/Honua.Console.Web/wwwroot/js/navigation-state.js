// Native navigation controls work before the interactive circuit is ready.
// Carry their actual DOM state across Blazor's replacement of the prerendered
// shell, including keyboard focus, without waiting for a server round-trip.
(() => {
    let menu = document.querySelector('.console-more-tools');
    let toggle = document.getElementById('console-collapse');
    let focused = document.activeElement;
    let startupFocus;
    const startupUrl = location.href;
    let prerendered = true;
    const isNavigationFocus = element => element &&
        (element === menu?.querySelector(':scope > summary') || element === toggle);
    document.addEventListener('focusin', event => {
        // FocusOnNavigate may focus the initial heading before or after the
        // mutation observer sees hydration. Keep the user's navigation focus
        // in either order; subsequent navigation still focuses its heading.
        if (event.target.matches('h1') && location.href === startupUrl) {
            if (prerendered && isNavigationFocus(focused) && !focused.isConnected)
                return;
            if (startupFocus?.isConnected) {
                const target = startupFocus;
                startupFocus = undefined;
                target.focus({ preventScroll: true });
                return;
            }
        }
        focused = event.target;
        if (event.target !== startupFocus) startupFocus = undefined;
    }, true);
    // A new human action takes precedence over any pending startup handoff.
    for (const name of ['pointerdown', 'keydown'])
        document.addEventListener(name, () => { startupFocus = undefined; }, true);

    new MutationObserver(() => {
        const nextMenu = document.querySelector('.console-more-tools');
        const nextToggle = document.getElementById('console-collapse');
        let restoreFocus;
        if (menu && nextMenu && menu !== nextMenu) {
            nextMenu.open = menu.open;
            if (focused === menu.querySelector(':scope > summary'))
                restoreFocus = nextMenu.querySelector(':scope > summary');
        }
        if (toggle && nextToggle && toggle !== nextToggle) {
            nextToggle.checked = toggle.checked;
            if (focused === toggle) restoreFocus = nextToggle;
        }
        const replacedPrerender = prerendered && menu && nextMenu && menu !== nextMenu;
        if (nextMenu) menu = nextMenu;
        if (nextToggle) toggle = nextToggle;
        if (restoreFocus && (document.activeElement === document.body ||
            (replacedPrerender && document.activeElement?.matches('h1')))) {
            if (replacedPrerender && location.href === startupUrl) startupFocus = restoreFocus;
            restoreFocus.focus({ preventScroll: true });
        }
        if (replacedPrerender) prerendered = false;
    }).observe(document.body, { childList: true, subtree: true });
})();
