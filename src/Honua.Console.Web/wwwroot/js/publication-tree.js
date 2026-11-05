{
    const selectionAttribute = 'aria-selected';
    const expansionAttribute = 'aria-expanded';
    const overlayAttribute = 'data-tree-open';
    const treeSelector = '[data-publication-tree]';
    const typeAheadResetMilliseconds = 700;
    const rowSelector = '[role="treeitem"]';
    const sidebarSelector = '.console-publication-sidebar';
    const narrow = matchMedia('(max-width: 1000px)');
    const suspended = new Map();
    const requestedExpansions = new Map();
    let sidebar;
    let routeRevision;
    let selectedElement;
    let focusedElement;
    let focusedKey;
    let pendingChild;
    let pendingPage;
    let typed = '';
    let typedAt = 0;
    const rows = () => [...(sidebar?.querySelectorAll(rowSelector) || [])];
    const key = row => row?.dataset.publicationKey;
    const children = row => rows().filter(child => child.dataset.treeParent === key(row));
    const parentRow = row => rows().find(item => key(item) === row.dataset.treeParent);
    const expander = row => row.querySelector(':scope > [data-tree-expand]');
    const toggle = () => document.querySelector('[data-tree-toggle]');
    const isOpen = () => sidebar?.hasAttribute(overlayAttribute);

    function focusRow(row) {
        if (!row) return;
        for (const item of rows()) item.tabIndex = item === row ? 0 : -1;
        focusedKey = key(row);
        focusedElement = row;
        row.focus({preventScroll: true});
        keepRowVisible(row);
    }
    const selectedRow = available => available.find(row => row.getAttribute(selectionAttribute) === 'true');
    function keepRowVisible(row) {
        if (!sidebar || !row.isConnected) return;
        const pane = sidebar.getBoundingClientRect();
        const bounds = row.getBoundingClientRect();
        const top = Math.max(pane.top, 0), bottom = Math.min(pane.bottom, innerHeight);
        if (bounds.top < top) sidebar.scrollTop += bounds.top - top;
        else if (bounds.bottom > bottom) sidebar.scrollTop += bounds.bottom - bottom;
    }
    function reconcileSelection(available) {
        const selection = selectedRow(available);
        if (selection === selectedElement) return;
        selectedElement = selection;
        if (!sidebar.contains(document.activeElement)) focusedKey = key(selection);
        if (selection && !narrow.matches) keepRowVisible(selection);
    }
    function resetTabStop() {
        const available = rows(), target = selectedRow(available) || available[0];
        focusedKey = key(target);
        focusedElement = undefined;
        for (const row of available) row.tabIndex = row === target ? 0 : -1;
    }
    function suspendBackground() {
        const backgrounds = document.querySelectorAll('.console-shell > :not(.console-service-workspace), .console-service-workspace > .console-workspace');
        for (const element of backgrounds) {
            if (!suspended.has(element)) suspended.set(element, element.inert);
            element.inert = true;
        }
        if (toggle()) toggle().inert = true;
    }
    function resumeBackground() {
        for (const [element, inert] of suspended) element.inert = inert;
        suspended.clear();
        if (toggle()) toggle().inert = false;
    }
    function openOverlay() {
        if (!sidebar || !narrow.matches) return;
        sidebar.hidden = false;
        sidebar.setAttribute(overlayAttribute, '');
        sidebar.setAttribute('role', 'dialog');
        sidebar.setAttribute('aria-modal', 'true');
        toggle()?.setAttribute(expansionAttribute, 'true');
        document.querySelector('[data-tree-backdrop]')?.removeAttribute('hidden');
        suspendBackground();
        sidebar.querySelector('input:not(:disabled), button:not(:disabled), [tabindex="0"]')?.focus();
    }
    function closeOverlay() {
        if (!sidebar) return;
        const restoreFocus = isOpen() && sidebar.contains(document.activeElement);
        sidebar.removeAttribute(overlayAttribute);
        sidebar.removeAttribute('role');
        sidebar.removeAttribute('aria-modal');
        toggle()?.setAttribute(expansionAttribute, 'false');
        document.querySelector('[data-tree-backdrop]')?.setAttribute('hidden', '');
        resumeBackground();
        sidebar.hidden = narrow.matches;
        if (restoreFocus) toggle()?.focus();
    }
    function reconcileSidebar() {
        const current = document.querySelector(sidebarSelector);
        if (current === sidebar) return;
        resumeBackground();
        sidebar = current;
        closeOverlay();
    }
    function reconcileRoute() {
        const revision = sidebar.closest('[data-publication-navigation]')?.dataset.treeRevision;
        if (revision === routeRevision) return;
        routeRevision = revision;
        closeOverlay();
    }
    function rovingTarget(available) {
        return available.find(row => key(row) === focusedKey)
            || selectedRow(available) || available[0];
    }
    function restoreReplacedFocus(available) {
        if (!focusedElement || focusedElement.isConnected || document.activeElement !== document.body) return;
        focusRow(available.find(row => key(row) === focusedKey));
    }
    function syncTree() {
        reconcileSidebar();
        if (!sidebar) return;
        reconcileRoute();
        const available = rows();
        reconcileSelection(available);
        const target = rovingTarget(available);
        for (const row of available) row.tabIndex = row === target ? 0 : -1;
        restoreReplacedFocus(available);
        reconcileRequestedExpansions(available);
        finishPendingFocus(available);
        if (isOpen()) suspendBackground();
    }
    function reconcileRequestedExpansions(available) {
        for (const [requestedKey, element] of requestedExpansions) {
            const current = available.find(row => key(row) === requestedKey);
            if (current !== element || current?.getAttribute(expansionAttribute) !== 'false') requestedExpansions.delete(requestedKey);
        }
    }
    function finishPendingFocus(available) {
        if (pendingPage) {
            const target = available.find(row => !pendingPage.keys.has(key(row))
                && row.dataset.treeParent === pendingPage.parent);
            if (target) { pendingPage = undefined; focusRow(target); }
        }
        const parent = available.find(row => key(row) === pendingChild);
        if (!parent) return;
        const first = children(parent)[0];
        if (first) { pendingChild = undefined; focusRow(first); }
        else if (parent.getAttribute(expansionAttribute) === 'true' && parent.getAttribute('aria-busy') !== 'true') pendingChild = undefined;
    }
    function moveRight(row) {
        if (row.getAttribute(expansionAttribute) === 'false') {
            if (requestedExpansions.has(key(row))) pendingChild = key(row);
            else { requestedExpansions.set(key(row), row); expander(row)?.click(); }
        }
        else if (children(row)[0]) focusRow(children(row)[0]);
        else if (row.getAttribute('aria-busy') === 'true') pendingChild = key(row);
    }
    function moveLeft(row) {
        if (row.getAttribute(expansionAttribute) === 'true') expander(row)?.click();
        else focusRow(parentRow(row));
    }
    function activate(row) {
        const link = row.querySelector(':scope > [data-tree-link]');
        if (link) link.click();
        else expander(row)?.click();
    }
    function expandSiblings(row) {
        for (const sibling of rows().filter(item => item.dataset.treeParent === row.dataset.treeParent))
            if (sibling.getAttribute(expansionAttribute) === 'false') expander(sibling)?.click();
        focusRow(row);
    }
    function typeAhead(row, letter) {
        const now = performance.now();
        typed = now - typedAt > typeAheadResetMilliseconds ? letter : typed + letter;
        typedAt = now;
        const prefix = [...typed].every(character => character === letter) ? letter : typed;
        const available = rows();
        const offset = available.indexOf(row) + 1;
        const ordered = [...available.slice(offset), ...available.slice(0, offset)];
        focusRow(ordered.find(item => item.dataset.treeLabel.toLocaleLowerCase().startsWith(prefix)));
    }
    function trapFocus(event) {
        if (!isOpen() || event.key !== 'Tab') return false;
        const targets = [...sidebar.querySelectorAll('a, button, input, [tabindex="0"]')]
            .filter(element => element.tabIndex >= 0 && !element.disabled && element.getClientRects().length);
        const first = targets[0], last = targets.at(-1);
        if (event.shiftKey && (document.activeElement === first || !sidebar.contains(document.activeElement))) {
            event.preventDefault(); last?.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault(); first?.focus();
        }
        return true;
    }
    function handleEscape(event) {
        if (event.key !== 'Escape' || !event.target.closest(sidebarSelector)) return false;
        event.preventDefault();
        if (event.target.matches('[data-tree-filter]') && event.target.value) {
            event.target.value = '';
            event.target.dispatchEvent(new Event('input', {bubbles: true}));
        } else if (isOpen()) closeOverlay();
        else focusRow(rows()[0]);
        return true;
    }
    function commandFor(row, pressedKey) {
        const available = rows(), index = available.indexOf(row);
        const commands = {
            ArrowDown: () => focusRow(available[Math.min(index + 1, available.length - 1)]),
            ArrowUp: () => focusRow(available[Math.max(index - 1, 0)]),
            ArrowRight: () => moveRight(row), ArrowLeft: () => moveLeft(row),
            Home: () => focusRow(available[0]), End: () => focusRow(available.at(-1)),
            Enter: () => activate(row), ' ': () => expander(row)?.click(), '*': () => expandSiblings(row)
        };
        return commands[pressedKey];
    }
    function handleRowKey(event, row) {
        const command = commandFor(row, event.key);
        if (command) {
            event.preventDefault();
            command();
        } else if (event.key.length === 1) {
            event.preventDefault(); typeAhead(row, event.key.toLocaleLowerCase());
        }
    }
    function clearPendingFocus() {
        pendingChild = undefined;
        pendingPage = undefined;
    }
    const hasCommandModifier = event => event.altKey || event.ctrlKey || event.metaKey || event.isComposing;
    document.addEventListener('keydown', event => {
        clearPendingFocus();
        if (event.defaultPrevented || handleEscape(event) || trapFocus(event) || hasCommandModifier(event)) return;
        const row = event.target.closest(rowSelector);
        if (!row || event.target !== row || !row.closest(treeSelector)) return;
        handleRowKey(event, row);
    });
    document.addEventListener('click', event => {
        if (event.target.closest('[data-tree-toggle]')) { if (isOpen()) closeOverlay(); else openOverlay(); return; }
        if (event.target.closest('[data-tree-close], [data-tree-backdrop]')) { closeOverlay(); return; }
        const more = event.target.closest('[data-tree-more]');
        if (more) pendingPage = {parent: more.dataset.treeMore, keys: new Set(rows().map(key))};
        const retry = event.target.closest('[data-tree-retry]');
        if (retry) {
            const owner = retry.closest('[data-tree-owner]')?.dataset.treeOwner;
            focusRow(rows().find(row => key(row) === owner));
        }
        const row = event.target.closest(rowSelector);
        if (row?.closest(treeSelector)) {
            focusedKey = key(row);
            focusRow(row);
        }
        if (event.target.closest('a[href]') && isOpen()) closeOverlay();
    });
    document.addEventListener('pointerdown', () => { focusedElement = undefined; clearPendingFocus(); }, true);
    document.addEventListener('focusin', event => {
        if (event.target.matches(rowSelector) && event.target.closest(treeSelector)) {
            pendingPage = undefined;
            focusedKey = key(event.target);
            focusedElement = event.target;
            for (const row of rows()) row.tabIndex = row === event.target ? 0 : -1;
        } else if (!event.target.closest(treeSelector)) { focusedElement = undefined; clearPendingFocus(); }
    });
    document.addEventListener('focusout', event => {
        if (!event.target.closest(treeSelector)) return;
        queueMicrotask(() => {
            if (!event.target.isConnected && document.activeElement === document.body) return;
            if (!document.activeElement?.closest(treeSelector)) resetTabStop();
        });
    });
    narrow.addEventListener('change', closeOverlay);
    addEventListener('popstate', closeOverlay);
    document.addEventListener('enhancedload', closeOverlay);
    syncTree();
    new MutationObserver(syncTree).observe(document.body, {childList: true, subtree: true, attributes: true, attributeFilter: ['data-tree-revision']});
}
