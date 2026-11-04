{
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
    const expanded = control => control?.matches(menuSelector) ? control.open : control?.getAttribute('aria-expanded') === 'true';
    function setExpanded(control, value) {
        if (control.matches(menuSelector)) control.open = value;
        else control.setAttribute('aria-expanded', String(value));
    }
    function restoreControlFocus(previous, control) {
        if (focused !== opener(previous) && !previous.contains(focused)) return;
        const index = [...previous.querySelectorAll('summary, a')].indexOf(focused);
        startupFocus = index < 0 ? opener(control) : control.querySelectorAll('summary, a')[index];
        startupFocus?.focus({preventScroll: true});
    }
    function collectControls() {
        const next = new Map([...document.querySelectorAll(menuSelector)].map(element => [element.dataset.navigationMenu, element]));
        const toggle = document.querySelector(toggleSelector);
        if (toggle) next.set('toggle', toggle);
        try {
            for (const [key, control] of next) {
                const previous = controls.get(key);
                if (!previous || previous === control) continue;
                setExpanded(control, expanded(previous));
                restoreControlFocus(previous, control);
                hydrated = true;
            }
        } finally {
            controls = next;
        }
    }
    document.addEventListener('focusin', event => {
        if (event.target.matches('h1') && location.href === startupUrl && startupFocus?.isConnected) {
            const target = startupFocus;
            startupFocus = undefined;
            target.focus({preventScroll: true});
            return;
        }
        if (!hydrated && event.target.matches('h1') && focused && !focused.isConnected && keyFor(focused)) return;
        if (event.target !== startupFocus) startupFocus = undefined;
        focused = event.target;
    }, true);
    document.addEventListener('pointerdown', () => {
        startupFocus = undefined;
        focused = undefined;
    }, true);
    document.addEventListener('focusout', event => {
        const menu = event.target.closest(menuSelector);
        if (menu) queueMicrotask(() => {
            if (!menu.contains(document.activeElement)) menu.open = false;
        });
    });
    function closeControls() {
        for (const control of controls.values()) setExpanded(control, false);
    }
    document.addEventListener('click', event => {
        const toggle = event.target.closest(toggleSelector);
        if (toggle) {
            setExpanded(toggle, !expanded(toggle));
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
        setExpanded(target, false);
        opener(target)?.focus();
    });
    document.addEventListener('click', event => {
        if (event.target.closest('.console-skip-link')) document.getElementById('console-main')?.focus({preventScroll: true});
    }, true);
    collectControls();
    new MutationObserver(collectControls).observe(document.body, {childList: true, subtree: true});
}
