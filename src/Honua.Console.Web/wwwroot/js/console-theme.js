// The WebAssembly shell configures an opaque key after its authentication state resolves.
(() => {
    const root = document.documentElement;
    let enabled = false;
    let key = null;
    const normalize = value => value === 'dark' ? 'dark' : 'light';

    function syncControls() {
        const dark = root.dataset.theme === 'dark';
        const label = `Switch to ${dark ? 'light' : 'dark'} mode`;
        document.querySelectorAll('[data-console-theme-toggle]').forEach(button => {
            button.setAttribute('aria-pressed', String(dark));
            button.setAttribute('aria-label', label);
            button.setAttribute('title', label);
        });
    }

    function apply(value, announce = true) {
        root.dataset.theme = normalize(value);
        syncControls();
        if (announce) document.dispatchEvent(new CustomEvent('honua:theme', {
            detail: { theme: root.dataset.theme }
        }));
    }

    function configure(isEnabled, storageKey) {
        enabled = isEnabled;
        key = enabled ? storageKey : null;
        root.dataset.consoleThemeEnabled = String(enabled);
        let saved;
        try { saved = key ? localStorage.getItem(key) : null; } catch { /* Storage may be disabled. */ }
        apply(saved, false);
    }
    window.honuaConsoleTheme = { configure };
    configure(root.dataset.consoleThemeEnabled === 'true', root.dataset.consoleThemeKey);
    document.addEventListener('click', event => {
        if (!enabled || !event.target.closest?.('[data-console-theme-toggle]')) return;
        apply(root.dataset.theme === 'dark' ? 'light' : 'dark');
        try { if (key) localStorage.setItem(key, root.dataset.theme); } catch { /* Keep the in-memory choice. */ }
    });
    window.addEventListener('storage', event => {
        if (key && event.key === key) apply(event.newValue);
    });
    // Blazor can replace the navigation after the initial page has painted.
    new MutationObserver(syncControls).observe(root, { childList: true, subtree: true });
})();
