export function hasOperatorShell(root = document) {
    return Boolean(root.querySelector('[data-console-location]'));
}
