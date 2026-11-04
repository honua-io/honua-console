{
    const menuSelector = '[data-navigation-menu]';
    let controls = new Map();
    let focused;
    let startupFocus;
    let hydrated = false;
    const startupUrl = location.href;
    const keyFor = element => element?.matches('[data-navigation-toggle]') ? 'toggle'
        : element?.closest(menuSelector)?.dataset.navigationMenu;
    const opener = control => control?.matches(menuSelector) ? control.querySelector(':scope > summary') : control;
    const expanded = control => control?.matches(menuSelector) ? control.open : control?.getAttribute('aria-expanded') === 'true';
    function setExpanded(control, value) {
        if (control.matches(menuSelector)) control.open = value;
        else control.setAttribute('aria-expanded', String(value));
    }
    function collectControls() {
        const next = new Map([...document.querySelectorAll(menuSelector)].map(element => [element.dataset.navigationMenu, element]));
        const toggle = document.querySelector('[data-navigation-toggle]');
        if (toggle) next.set('toggle', toggle);
        for (const [key, control] of next) {
            const previous = controls.get(key);
            if (!previous || previous === control) continue;
            setExpanded(control, expanded(previous));
            if (focused === opener(previous) || previous.contains(focused)) {
                const index = [...previous.querySelectorAll('summary, a')].indexOf(focused);
                startupFocus = index < 0 ? opener(control) : control.querySelectorAll('summary, a')[index];
                startupFocus.focus({preventScroll: true});
            }
            hydrated = true;
        }
        controls = next;
    }
    document.addEventListener('focusin', event => {
        if (event.target.matches('h1') && location.href === startupUrl && startupFocus?.isConnected) {
            startupFocus.focus({preventScroll: true});
            startupFocus = undefined;
            return;
        }
        if (!hydrated && event.target.matches('h1') && focused && !focused.isConnected && keyFor(focused)) return;
        focused = event.target;
    }, true);
    document.addEventListener('click', event => {
        const toggle = event.target.closest('[data-navigation-toggle]');
        if (toggle) setExpanded(toggle, !expanded(toggle));
        if (event.target.closest('.console-navigation-items a, .console-topnav > a')) {
            for (const control of controls.values()) setExpanded(control, false);
        } else if (!event.target.closest(menuSelector)) {
            for (const control of controls.values()) if (control.matches(menuSelector)) setExpanded(control, false);
        }
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
        if (event.key !== 'Escape') return;
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
