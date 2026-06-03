// Clipboard helper for the Operate > Catalogs item editor (CatalogItemEditorPage).
//
// "Copy item URL" copies the absolute URL of the current item-editor page to the clipboard. We try the
// async Clipboard API first (requires a secure context + user gesture, which the button click provides),
// and fall back to a hidden-textarea + execCommand("copy") for older / non-secure contexts. Returns true
// only when the copy succeeded so the component can confirm the action to the operator.

export async function copyText(text) {
    if (typeof text !== "string" || text.length === 0) {
        return false;
    }

    if (navigator.clipboard && window.isSecureContext) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            // Fall through to the legacy path (permission denied / not allowed).
        }
    }

    try {
        const textarea = document.createElement("textarea");
        textarea.value = text;
        textarea.setAttribute("readonly", "");
        textarea.style.position = "absolute";
        textarea.style.left = "-9999px";
        document.body.appendChild(textarea);
        textarea.select();
        const ok = document.execCommand("copy");
        document.body.removeChild(textarea);
        return ok;
    } catch {
        return false;
    }
}
