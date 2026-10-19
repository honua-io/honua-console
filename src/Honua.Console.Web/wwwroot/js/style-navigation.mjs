// Human navigation only: retained editor state is discarded through its actual
// authorized reread, never by clearing a presentation flag or closing chat.
export async function mayLeaveStyleEditor({current,confirm}) {
    const captured=current();
    if(!captured?.app?.hasUnsavedStyleChanges) return true;
    const app=captured.app,binding=captured.binding;
    if(!confirm('Discard your unsaved map-style changes and leave this view?')) return false;
    try {
        await app.discardStyleChanges();
        const latest=current();
        return latest?.app===app && latest.binding===binding
            && !app.hasUnsavedStyleChanges;
    } catch { return false; }
}

export function mayLeaveCurrentStyleEditor() {
    return mayLeaveStyleEditor({current:()=>globalThis.honuaStudioStyleContext?.(),confirm:message=>globalThis.confirm(message)});
}
