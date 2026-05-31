// Browser-level unsaved-changes guard for the console (Shell/Validation, Wave 0).
//
// Blazor's NavigationManager.RegisterLocationChangingHandler only intercepts internal SPA navigation;
// it cannot catch a browser tab close, refresh (F5), or navigation to an external URL. This module
// wires window.onbeforeunload for exactly those cases. When armed, the browser shows its native
// "Leave site? Changes you made may not be saved." prompt — we cannot customise that text (browsers
// ignore a custom string for security), so the in-app SPA prompt carries the editor's message.
//
// The handler is enabled/disabled by the UnsavedChangesGuard component as its IsDirty flag changes and
// is fully removed on dispose so a clean editor never blocks a page close.

let armed = false;

function onBeforeUnload(event) {
    if (!armed) {
        return undefined;
    }

    // The triad (preventDefault + returnValue + return) covers every browser's contract for
    // triggering the native confirmation dialog.
    event.preventDefault();
    event.returnValue = "";
    return "";
}

export function enable() {
    if (!armed) {
        armed = true;
        window.addEventListener("beforeunload", onBeforeUnload);
    }
}

export function disable() {
    if (armed) {
        armed = false;
        window.removeEventListener("beforeunload", onBeforeUnload);
    }
}
