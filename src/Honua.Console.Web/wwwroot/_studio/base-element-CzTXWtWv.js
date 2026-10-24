//#region src/elements/base-element.ts
function e(e) {
	return e instanceof HTMLInputElement || e instanceof HTMLTextAreaElement;
}
function t(e) {
	return typeof CSS < "u" && typeof CSS.escape == "function" ? CSS.escape(e) : e.replace(/["\\]/g, "\\$&");
}
function n(e) {
	let n = e.localName;
	for (let r of ["id", "name"]) {
		let i = e.getAttribute(r);
		if (i) return `${n}[${r}="${t(i)}"]`;
	}
	for (let r of e.getAttributeNames().filter((e) => e.startsWith("data-"))) {
		let i = e.getAttribute(r);
		if (i) return `${n}[${r}="${t(i)}"]`;
	}
}
function r(e) {
	try {
		return e.activeElement;
	} catch {
		return null;
	}
}
function i(t) {
	let i = r(t);
	if (!i) return;
	let a = n(i);
	if (!a) return;
	let o = e(i) ? i : void 0;
	return {
		selector: a,
		...o ? { value: o.value } : {},
		...typeof o?.selectionStart == "number" ? { selectionStart: o.selectionStart } : {},
		...typeof o?.selectionEnd == "number" ? { selectionEnd: o.selectionEnd } : {},
		...o?.selectionDirection ? { selectionDirection: o.selectionDirection } : {}
	};
}
function a(t, n) {
	if (!n) return;
	let r = t.querySelector(n.selector);
	!r || !(r instanceof HTMLElement) || typeof r.focus != "function" || (r.focus({ preventScroll: !0 }), !(n.value === void 0 || n.selectionStart === void 0 || n.selectionEnd === void 0 || !e(r) || r.value !== n.value) && r.setSelectionRange(n.selectionStart, n.selectionEnd, n.selectionDirection ?? "none"));
}
var o = class extends HTMLElement {
	#e;
	#t = !1;
	get connectedSignal() {
		return this.#e?.signal;
	}
	connectedCallback() {
		this.#e = new AbortController(), this.ensureShadowRoot(), this.onConnect(this.#e.signal), this.render(), this.#t || (this.#t = !0, this.dispatchTypedEvent("honua-studio-ready", { tagName: this.localName }));
	}
	disconnectedCallback() {
		this.#e?.abort(), this.#e = void 0, this.onDisconnect();
	}
	onConnect(e) {}
	onDisconnect() {}
	ensureShadowRoot() {
		return this.shadowRoot ?? this.attachShadow({ mode: "open" });
	}
	setShadowHtml(e) {
		let t = this.ensureShadowRoot(), n = i(t);
		t.innerHTML = e, a(t, n);
	}
	listen(e, t, n, r) {
		let i = this.connectedSignal;
		i && e.addEventListener(t, n, {
			...r,
			signal: i
		});
	}
	dispatchTypedEvent(e, t) {
		this.dispatchEvent(new CustomEvent(e, {
			bubbles: !0,
			composed: !0,
			detail: t
		}));
	}
};
//#endregion
export { i as n, a as r, o as t };
