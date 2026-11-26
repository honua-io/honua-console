import { captureTrigger, restoreTriggerFocus, trapTabKey } from './console-overlay-focus.js';

const dialogs = new Map();

export function show(dialog, key) {
    if (!dialog?.isConnected || dialog.open) return;
    const onKeyDown = event => trapTabKey(event, dialog);
    dialogs.set(key, { dialog, trigger: captureTrigger(), onKeyDown });
    dialog.addEventListener('cancel', preventNativeCancel);
    dialog.addEventListener('keydown', onKeyDown);
    dialog.showModal();
}

export function close(key) {
    const entry = dialogs.get(key);
    if (!entry) return;
    dialogs.delete(key);
    entry.dialog.removeEventListener('cancel', preventNativeCancel);
    entry.dialog.removeEventListener('keydown', entry.onKeyDown);
    entry.dialog.close();
    restoreTriggerFocus(entry.trigger);
}

function preventNativeCancel(event) {
    event.preventDefault();
}
