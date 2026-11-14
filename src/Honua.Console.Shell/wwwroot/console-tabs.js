const handlers = new WeakMap();

export function attach(element) {
    detach(element);
    const handler = event => {
        if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
        const tabs = Array.from(element.querySelectorAll('[role="tab"]'));
        const index = tabs.indexOf(event.target);
        if (index < 0) return;
        event.preventDefault();
        const next = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1
            : (index + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
        tabs[next].focus();
        tabs[next].click();
    };
    handlers.set(element, handler);
    element.addEventListener('keydown', handler);
}

export function detach(element) {
    const handler = handlers.get(element);
    if (handler) element.removeEventListener('keydown', handler);
    handlers.delete(element);
}
