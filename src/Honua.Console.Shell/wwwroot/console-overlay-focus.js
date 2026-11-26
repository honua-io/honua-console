const FOCUSABLE_SELECTOR = [
    'a[href]',
    'button:not([disabled])',
    'input:not([disabled]):not([type="hidden"])',
    'select:not([disabled])',
    'textarea:not([disabled])',
    'summary',
    '[tabindex]:not([tabindex="-1"])',
].join(', ');

const activeOverlays = [];
let listening = false;

export function activate(container, key, dotNetRef) {
    if (!container?.isConnected) return;
    removeEntry(key);
    activeOverlays.push({ key, container, trigger: captureTrigger(), dotNetRef });
    ensureKeyListener();
    moveInitialFocus(container);
}

export function deactivate(key) {
    const entry = removeEntry(key);
    if (!entry) return;
    if (activeOverlays.length === 0) removeKeyListener();
    restoreTriggerFocus(entry.trigger);
}

export function captureTrigger() {
    return document.activeElement instanceof HTMLElement ? document.activeElement : null;
}

export function restoreTriggerFocus(trigger) {
    if (!trigger?.isConnected || trigger.hasAttribute('disabled')) return;
    if (trigger.getAttribute('aria-disabled') === 'true') return;
    trigger.focus();
}

export function trapTabKey(event, container) {
    if (event.key !== 'Tab') return;
    const targets = getFocusableTargets(container);
    if (targets.length === 0) {
        event.preventDefault();
        container.focus();
        return;
    }
    const first = targets[0];
    const last = targets.at(-1);
    const active = document.activeElement;
    if (!container.contains(active)) {
        event.preventDefault();
        (event.shiftKey ? last : first).focus();
        return;
    }
    if (event.shiftKey && active === first) {
        event.preventDefault();
        last.focus();
    } else if (!event.shiftKey && active === last) {
        event.preventDefault();
        first.focus();
    }
}

function removeEntry(key) {
    const index = activeOverlays.findIndex(item => item.key === key);
    if (index < 0) return null;
    return activeOverlays.splice(index, 1)[0];
}

function moveInitialFocus(container) {
    if (container.contains(document.activeElement)) return;
    const preferred = container.querySelector('[autofocus]');
    if (preferred instanceof HTMLElement && isFocusable(preferred)) {
        preferred.focus();
        return;
    }
    const first = getFocusableTargets(container)[0] ?? container;
    first.focus();
}

function getFocusableTargets(container) {
    return Array.from(container.querySelectorAll(FOCUSABLE_SELECTOR)).filter(isFocusable);
}

function isFocusable(element) {
    if (!(element instanceof HTMLElement)) return false;
    if (element.tabIndex < 0 || element.hasAttribute('disabled') || element.inert) return false;
    return typeof element.checkVisibility === 'function'
        ? element.checkVisibility()
        : element.offsetParent !== null;
}

function ensureKeyListener() {
    if (listening) return;
    listening = true;
    document.addEventListener('keydown', handleKeyDown, true);
}

function removeKeyListener() {
    if (!listening) return;
    listening = false;
    document.removeEventListener('keydown', handleKeyDown, true);
}

function handleKeyDown(event) {
    const top = activeOverlays.at(-1);
    if (!top?.container?.isConnected) return;
    if (event.key === 'Escape') {
        handleEscape(event, top);
        return;
    }
    trapTabKey(event, top.container);
}

function handleEscape(event, top) {
    if (event.defaultPrevented || top.container.contains(event.target)) return;
    event.preventDefault();
    top.dotNetRef?.invokeMethodAsync('NotifyEscapeAsync').catch(() => {});
}
