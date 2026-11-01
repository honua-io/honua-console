// Async Razor renders may add fields after the containing form first appears.
export function requireSupportedWorkspacePublish(form) {
    if (form.querySelector('[data-publish-generic-geometry]'))
        throw new Error('This connected workspace cannot retain a geometry type yet. Open a fresh browser session without a connected workspace to publish this table.');
}

export function createPublishInputBinder(workspace, {signal, shouldIgnore = () => false} = {}) {
    const bound = new WeakSet();
    return form => {
        for (const input of form.querySelectorAll('[data-workspace-field]')) {
            if (bound.has(input)) continue;
            bound.add(input);
            input.addEventListener('input', () => {
                if (!workspace.context || shouldIgnore()) return;
                const field = input.dataset.workspaceField;
                if (field === 'table') {
                    const option = input.selectedOptions[0];
                    workspace.edit('schema', option?.dataset.schema || '');
                    workspace.edit('table', option?.dataset.table || '');
                } else workspace.edit(field, input.value);
            }, {signal});
        }
    };
}
