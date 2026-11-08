{
    const expansionAttribute = 'aria-expanded';
    const menuSelector = '[data-navigation-menu]';
    const toggleSelector = '[data-navigation-toggle]';
    const chromeSelector = '.console-topbar, .console-contextbar';
    let controls = new Map();
    let focused;
    let startupFocus;
    let hydrated = false;
    const startupUrl = location.href;
    const keyFor = element => element?.matches(toggleSelector) ? 'toggle'
        : element?.closest(menuSelector)?.dataset.navigationMenu;
    const opener = control => control?.matches(menuSelector) ? control.querySelector(':scope > summary') : control;
    const expanded = control => control?.matches(menuSelector) ? control.open : control?.getAttribute(expansionAttribute) === 'true';
    function collapseControl(control) {
        if (control.matches(menuSelector)) control.open = false;
        else control.setAttribute(expansionAttribute, 'false');
    }
    function expandControl(control) {
        if (control.matches(menuSelector)) control.open = true;
        else control.setAttribute(expansionAttribute, 'true');
    }
    function copyExpansion(source, target) {
        if (expanded(source)) expandControl(target);
        else collapseControl(target);
    }
    function toggleControl(control) {
        if (expanded(control)) collapseControl(control);
        else expandControl(control);
    }
    function replacementFocusTarget(previous, control) {
        if (focused !== opener(previous) && !previous.contains(focused)) return;
        const index = [...previous.querySelectorAll('summary, a')].indexOf(focused);
        return index < 0 ? opener(control) : control.querySelectorAll('summary, a')[index];
    }
    function transferControl(key, control) {
        const previous = controls.get(key);
        if (!previous || previous === control) return;
        const target = replacementFocusTarget(previous, control);
        copyExpansion(previous, control);
        if (target) {
            startupFocus = target;
            target.focus({preventScroll: true});
        }
        hydrated = true;
    }
    function collectControls() {
        const next = new Map([...document.querySelectorAll(menuSelector)].map(element => [element.dataset.navigationMenu, element]));
        const toggle = document.querySelector(toggleSelector);
        if (toggle) next.set('toggle', toggle);
        try {
            for (const [key, control] of next) transferControl(key, control);
        } finally {
            controls = next;
        }
    }
    const isStartupHeading = element => element.matches('h1') && location.href === startupUrl;
    const shouldRetainPrerenderFocus = element => !hydrated && isStartupHeading(element)
        && focused && !focused.isConnected && keyFor(focused);
    document.addEventListener('focusin', event => {
        if (isStartupHeading(event.target) && startupFocus?.isConnected) {
            const target = startupFocus;
            startupFocus = undefined;
            target.focus({preventScroll: true});
            return;
        }
        if (shouldRetainPrerenderFocus(event.target)) return;
        if (event.target !== startupFocus) startupFocus = undefined;
        focused = event.target;
    }, true);
    document.addEventListener('pointerdown', () => {
        startupFocus = undefined;
        focused = undefined;
    }, true);
    document.addEventListener('focusout', event => {
        const menu = event.target.closest(menuSelector);
        if (menu && !menu.contains(event.relatedTarget)) queueMicrotask(() => {
            if (menu.isConnected && !menu.contains(document.activeElement)) menu.open = false;
        });
    });
    function closeControls() {
        for (const control of controls.values()) collapseControl(control);
    }
    document.addEventListener('click', event => {
        const toggle = event.target.closest(toggleSelector);
        if (toggle) {
            toggleControl(toggle);
            return;
        }
        if (!event.target.closest(menuSelector) || event.target.closest('.console-navigation-items a')) closeControls();
    });
    document.addEventListener('toggle', event => {
        if (!event.target.matches(menuSelector) || !event.target.open) return;
        for (const control of controls.values())
            if (control.matches(menuSelector) && control !== event.target) control.open = false;
        if (document.activeElement === opener(event.target) || !event.target.contains(document.activeElement))
            event.target.querySelector('a')?.focus();
    }, true);
    document.addEventListener('keydown', event => {
        startupFocus = undefined;
        if (event.defaultPrevented || event.key !== 'Escape') return;
        if (event.target.closest('[role="dialog"], dialog[open]') && !event.target.closest(chromeSelector)) return;
        const menu = event.target.closest(menuSelector);
        const target = menu?.open ? menu : controls.get('toggle');
        if (!target || !expanded(target)) return;
        event.preventDefault();
        collapseControl(target);
        opener(target)?.focus();
    });
    document.addEventListener('click', event => {
        if (event.target.closest('.console-skip-link')) document.getElementById('console-main')?.focus({preventScroll: true});
    }, true);
    collectControls();
    new MutationObserver(collectControls).observe(document.body, {childList: true, subtree: true});
}
