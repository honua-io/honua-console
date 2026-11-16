const dialogs = new Map();

export function show(dialog, key) {
    if (!dialog?.isConnected || dialog.open) return;
    dialogs.set(key, { dialog, trigger: document.activeElement });
    dialog.addEventListener('cancel', preventNativeCancel);
    dialog.addEventListener('keydown', containTab);
    dialog.showModal();
}

export function close(key) {
    const entry = dialogs.get(key);
    if (!entry) return;
    dialogs.delete(key);
    entry.dialog.removeEventListener('cancel', preventNativeCancel);
    entry.dialog.removeEventListener('keydown', containTab);
    entry.dialog.close();
    if (entry.trigger?.isConnected) entry.trigger.focus();
}

function preventNativeCancel(event) {
    // Blazor owns cancellation, including refusing Escape during an in-flight action.
    event.preventDefault();
}

function containTab(event) {
    if (event.key !== 'Tab') return;
    const buttons = event.currentTarget.querySelectorAll('button:not(:disabled)');
    if (!buttons.length) {
        event.preventDefault();
        return;
    }
    const first = buttons[0];
    const last = buttons[buttons.length - 1];
    if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
    }
}
