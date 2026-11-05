{
    const focusable = 'a[href], button, input, select, textarea, summary, [tabindex]';
    class ModalSurface {
        constructor(options) {
            this.options = options;
            this.suspended = new Map();
            this.active = false;
            window.addEventListener('honua-modal-opening', event => {
                if (this.active && event.detail !== this.surface()) this.options.onDismiss();
            });
        }
        surface() { return this.options.surface(); }
        activate() {
            if (!this.active) window.dispatchEvent(new CustomEvent('honua-modal-opening', {detail: this.surface()}));
            this.active = true;
            for (const element of this.options.backgrounds()) {
                if (!this.suspended.has(element)) this.suspended.set(element, element.inert);
                element.inert = true;
            }
        }
        release() {
            this.active = false;
            for (const [element, inert] of this.suspended) element.inert = inert;
            this.suspended.clear();
        }
        deactivate() {
            const restore = this.active && this.surface()?.contains(document.activeElement);
            this.release();
            if (restore) this.options.returnFocus()?.focus();
        }
        trapFocus(event) {
            if (!this.active || event.key !== 'Tab') return false;
            const targets = [...this.surface().querySelectorAll(focusable)]
                .filter(element => element.tabIndex >= 0 && !element.disabled && !element.inert && element.getClientRects().length);
            const first = targets[0], last = targets.at(-1);
            const outside = !this.surface().contains(document.activeElement);
            if (event.shiftKey && (document.activeElement === first || outside)) {
                event.preventDefault(); last?.focus();
            } else if (!event.shiftKey && (document.activeElement === last || outside)) {
                event.preventDefault(); first?.focus();
            }
            return true;
        }
    }
    globalThis.honuaModalSurface = {create: options => new ModalSurface(options)};
}
