// Keep the Console host contract stable across the pinned Studio vocabulary change.
// This adapter changes names only; Studio still owns authority and draft lifecycle behavior.
export function adaptStudioBundle(module) {
    return {...module,
        createWorkspaceAgentSession: module.createEditorAgentSession,
        workspaceAgentFeatures: module.editorAgentFeatures,
        ServerProcessWorkspace: module.ServerProcessEditor,
        LocalProcessWorkspace: module.LocalProcessEditor,
        createWorkspaceLifecycleReader: module.createContentLifecycleReader,
        createReadOnlyCompositionView: module.createReadOnlyAppView};
}

let bundle;
export function loadStudioBundle() {
    return bundle ||= import(new URL('../_studio/console.js', import.meta.url).href).then(adaptStudioBundle);
}

const connected = new WeakSet();
const methods = {
    createWorkspaceStyleProvider: 'createEditorStyleProvider',
    setWorkspaceConversation: 'setEditorConversation',
    enableLiveComposition: 'enableLiveEditing',
    disableLiveComposition: 'disableLiveEditing',
    createWorkspaceDraft: 'createEditorDraft',
    attachWorkspaceDraft: 'attachEditorDraft',
    refreshWorkspaceDraft: 'refreshEditorDraft',
    prepareWorkspaceTurn: 'prepareEditorTurn',
    discardWorkspaceApprovalHold: 'discardEditorApprovalHold',
    saveWorkspaceVersion: 'saveEditorVersion',
    listWorkspaceVersions: 'listEditorVersions',
    reviewWorkspacePublication: 'reviewEditorRelease',
    publishWorkspaceVersion: 'releaseEditorVersion',
    readWorkspacePublication: 'readEditorRelease',
    reopenWorkspaceVersion: 'reopenEditorVersion',
    prepareWorkspaceTask: 'prepareEditorTask',
    attachWorkspaceTask: 'attachEditorTask',
    refreshWorkspaceTask: 'refreshEditorTask'
};

export function connectStudioHost(element) {
    if (connected.has(element)) return element;
    connected.add(element);
    for (const [host, studio] of Object.entries(methods)) {
        if (typeof element[studio] === 'function')
            element[host] = function (...args) { return this[studio](...args); };
    }
    element.addEventListener('honua-studio-draft-change', event => {
        if (event.target === element)
            element.dispatchEvent(new CustomEvent('honua-workspace-draft-change',
                {bubbles: true, composed: true, detail: event.detail}));
    });
    return element;
}
