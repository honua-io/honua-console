const activeDialogs = new Map();

function getFocusableElements(dialog) {
    return Array.from(dialog.querySelectorAll(
        'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), ' +
        'textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
    )).filter(element => !element.hidden && element.getAttribute('aria-hidden') !== 'true');
}

function restoreFocus(state) {
    const { dialog, previousFocus, onKeyDown } = state;
    dialog.removeEventListener('keydown', onKeyDown);

    if (previousFocus instanceof HTMLElement && previousFocus.isConnected) {
        previousFocus.focus({ preventScroll: true });
    }
}

function initializeDialog(backdrop) {
    if (activeDialogs.has(backdrop)) {
        return;
    }

    const dialog = backdrop.querySelector('[data-console-confirm-dialog]');
    if (!(dialog instanceof HTMLElement)) {
        return;
    }

    const previousFocus = document.activeElement instanceof HTMLElement
        ? document.activeElement
        : null;

    const onKeyDown = event => {
        if (event.key === 'Escape') {
            event.preventDefault();
            event.stopPropagation();
            dialog.querySelector('[data-console-confirm-cancel]:not([disabled])')?.click();
            return;
        }

        if (event.key !== 'Tab') {
            return;
        }

        const focusable = getFocusableElements(dialog);
        if (focusable.length === 0) {
            event.preventDefault();
            dialog.focus();
            return;
        }

        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        const current = document.activeElement;

        if (event.shiftKey && (current === first || !dialog.contains(current))) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && (current === last || !dialog.contains(current))) {
            event.preventDefault();
            first.focus();
        }
    };

    dialog.addEventListener('keydown', onKeyDown);
    activeDialogs.set(backdrop, { dialog, previousFocus, onKeyDown });

    requestAnimationFrame(() => {
        const initialFocus = dialog.querySelector('[data-console-confirm-cancel]:not([disabled])')
            ?? getFocusableElements(dialog)[0]
            ?? dialog;
        initialFocus.focus({ preventScroll: true });
    });
}

function synchronizeDialogs() {
    document.querySelectorAll('[data-console-confirm]').forEach(initializeDialog);

    for (const [backdrop, state] of activeDialogs) {
        if (!backdrop.isConnected) {
            restoreFocus(state);
            activeDialogs.delete(backdrop);
        }
    }
}

document.addEventListener('click', event => {
    const dismiss = event.target instanceof Element
        ? event.target.closest('[data-blazor-error-dismiss]')
        : null;

    if (dismiss) {
        const errorUi = document.getElementById('blazor-error-ui');
        if (errorUi) {
            errorUi.style.display = 'none';
        }
    }
});

const observer = new MutationObserver(synchronizeDialogs);
observer.observe(document.body, { childList: true, subtree: true });
synchronizeDialogs();
