import { n as e, r as t } from "./base-element-CzTXWtWv.js";
import { l as n, t as r } from "./visual-layer-owner-Cli7j7Sb.js";
import { a as i, i as a, o, r as s, t as c } from "./control-config-B3vF9Ivt.js";
import { a as l, i as u, n as d, o as f, r as p, t as m } from "./widget-config-BnNjdjF6.js";
//#region src/design/design-inventory.ts
function h(e, t = /* @__PURE__ */ new Set()) {
	if (e && typeof e == "object" && !Array.isArray(e)) {
		let n = e;
		typeof n.property == "string" && Array.isArray(n.stops) && t.add(n.property);
	}
	if (!Array.isArray(e) || e[0] === "literal") return t;
	(e[0] === "get" || e[0] === "has") && e.length === 2 && typeof e[1] == "string" && t.add(e[1]);
	for (let n of e) h(n, t);
	return t;
}
function g(e, t) {
	if (h(e, t), Array.isArray(e)) if ([
		"all",
		"any",
		"none"
	].includes(e[0])) for (let n of e.slice(1)) g(n, t);
	else [
		"==",
		"!=",
		">",
		">=",
		"<",
		"<=",
		"in",
		"!in",
		"has",
		"!has"
	].includes(e[0]) && typeof e[1] == "string" && !["$id", "$type"].includes(e[1]) && t.add(e[1]);
}
function _(e) {
	return !Array.isArray(e) || e[0] === "literal" ? !1 : (e[0] === "get" || e[0] === "has") && e.length === 2 && typeof e[1] != "string" || e.some(_);
}
function v(e, t = {}) {
	let n = [], s = new Set(e.layers.map((e) => e.id));
	for (let t of e.layers) {
		let i = /* @__PURE__ */ new Set(), a = [], c = !1, l = [...Array.isArray(e.mapSpec?.layers) ? e.mapSpec.layers : [], ...Object.entries(e.styleRefs?.find((e) => e.styleId === t.styleRef?.styleId)?.body ?? {}).map(([e, t]) => ({
			id: e,
			...t
		}))];
		for (let e of l) {
			if (!e || typeof e != "object") continue;
			let n = e;
			if (!(typeof n.id != "string" || r({
				id: n.id,
				metadata: n.metadata
			}, s) !== t.id)) {
				for (let e of [n.paint, n.layout]) if (e && typeof e == "object" && !Array.isArray(e)) for (let t of Object.values(e)) h(t, i), c ||= _(t);
				n.filter !== void 0 && (a.push(JSON.stringify(n.filter)), g(n.filter, i), c ||= _(n.filter));
			}
		}
		let u = [...c ? ["Dynamic field references are unresolved; the inventory lists only statically named fields."] : [], ...t.styleRef && !e.styleRefs?.find((e) => e.styleId === t.styleRef?.styleId)?.body ? ["Shared style fields are unavailable until its canonical definition is described."] : []];
		n.push({
			ref: `layer:${t.id}`,
			title: t.title ?? t.id,
			target: {
				kind: "layer",
				id: t.id
			},
			sourceReference: t.sourceId,
			sourceId: o(e, t.sourceId),
			fields: [...i],
			constraints: a,
			...u.length ? { note: u.join(" ") } : {}
		});
	}
	for (let r of e.widgets) {
		let i = [], a;
		if (r.kind === "table") {
			let e = u(r);
			e.ok ? (i.push(...e.config.fields.length ? e.config.fields : t[r.id] ?? []), e.config.primaryKey && i.push(e.config.primaryKey), e.config.fields.length || (a = "Columns inferred from loaded rows (at most 8); no authored field list.")) : a = e.reason;
		}
		if (r.kind === "chart") {
			let e = m(r);
			if (e.ok) for (let t of [
				e.config.groupBy,
				e.config.field,
				e.config.measure.field
			]) t && t !== "*" && i.push(t);
			else a = e.reason;
		}
		r.kind === "time" && r.config?.field && (a = "Field-based time filtering is not implemented by this widget."), n.push({
			ref: `widget:${r.id}`,
			title: r.title ?? r.id,
			target: {
				kind: "component",
				id: r.id
			},
			...r.sourceId ? {
				sourceReference: r.sourceId,
				sourceId: o(e, r.sourceId)
			} : {},
			fields: [...new Set(i)],
			constraints: [],
			...a ? { note: a } : {}
		});
	}
	for (let t of e.controls) {
		let r = i(t), s = r.ok && "field" in r.config.config ? r.config.config.field : void 0, c = a(e, t);
		n.push({
			ref: `control:${t.id}`,
			title: t.title ?? t.id,
			target: {
				kind: "control",
				id: t.id
			},
			...t.sourceId ? {
				sourceReference: t.sourceId,
				sourceId: o(e, t.sourceId)
			} : {},
			fields: typeof s == "string" ? [s] : [],
			constraints: [],
			...c.state === "unsupported" ? { note: c.reason } : {}
		});
	}
	return n.map((t) => {
		let r = new Set(t.fields);
		for (let n of e.controls) {
			let a = i(n);
			!n.sourceId || !a.ok || ![
				"filterSelect",
				"filterSlider",
				"filterDateRange",
				"timeSlider"
			].includes(n.kind) || t.sourceId && o(e, n.sourceId) === t.sourceId && "field" in a.config.config && typeof a.config.config.field == "string" && r.add(a.config.config.field);
		}
		for (let i of e.interactions) {
			if (i.do.ref !== t.ref || i.do.verb !== "setFilter") continue;
			let e = i.do.args, a = (e?.clause && typeof e.clause == "object" ? e.clause : void 0)?.field ?? e?.field;
			if (typeof a == "string" && !a.startsWith("$event") && r.add(a), a === "$event.field" || e?.clause === "$event.clause") {
				let e = n.find((e) => e.ref === i.on.ref);
				if (e?.target.kind === "control") for (let t of e.fields) r.add(t);
			}
		}
		return {
			...t,
			fields: [...r]
		};
	});
}
function y(e, t, n = !1) {
	let r = e.interactions.map((n) => {
		let r = "unresolved", i = "The interaction runtime has not compiled this document.";
		if (t?.issues.length) i = `No bindings installed: ${t.issues.map((e) => e.message).join("; ")}`;
		else if (n.disabled) r = "disabled", i = "Disabled in the authored document.";
		else {
			let o = t?.unsupported.find((e) => e.interactionId === n.id);
			if (o) r = "unsupported", i = o.reason;
			else if (t?.bindings.some((e) => e.interactionId === n.id)) {
				r = "supported", i = "Installed by the current interaction runtime.";
				let t = e.controls.find((e) => n.on.ref === `control:${e.id}`);
				n.on.event === "change" && (!t || !c[t.kind]) ? (r = "unsupported", i = "This component does not publish control change events.") : t && a(e, t).state === "unsupported" ? (r = "unresolved", i = "The source control is unavailable; inspect its data binding.") : n.on.event === "selection" || n.on.event === "viewportChange" ? (r = "unsupported", i = "The canvas does not publish this gesture into the compiled exploration view.") : n.do.verb === "setFilter" && !n.do.ref.startsWith("layer:") && (r = "unsupported", i = "The compiler writes a shared clause, but this target has no filter consumer in Studio."), t && e.pins.some((e) => e.kind === "control" && e.id === t.id) ? (r = "disabled", i = "The source control is pinned; its gesture is disabled.") : n.do.verb === "setVisibility" && e.pins.some((e) => e.kind === "layer" && n.do.ref === `layer:${e.id}`) && (r = "disabled", i = "The target layer is pinned; the existing reducer refuses this visibility command.");
			}
		}
		return {
			origin: "authored",
			id: n.id,
			authoredInteractionId: n.id,
			from: n.on.ref,
			event: n.on.event,
			to: n.do.ref,
			action: n.do.verb,
			behavior: n.do.verb === "setFilter" ? "shared exploration" : "one-shot gesture",
			status: r,
			reason: i
		};
	});
	for (let l of e.controls) {
		let u = a(e, l), d = e.pins.some((e) => e.kind === "control" && e.id === l.id);
		c[l.kind] || r.push({
			origin: "intrinsic",
			id: `intrinsic:${l.id}:map`,
			from: `control:${l.id}`,
			event: "gesture",
			to: "map",
			action: l.kind,
			behavior: "intrinsic",
			status: u.state === "unsupported" ? "unsupported" : d || !n ? "disabled" : "supported",
			reason: u.state === "unsupported" ? u.reason : d ? "The control is pinned." : n ? "Intrinsic map affordance; no authored interaction required." : "The map bridge is currently unavailable."
		});
		let f = i(l);
		if (f.ok && f.config.kind === "opacity") for (let n of f.config.config.layerIds) r.push({
			origin: "intrinsic",
			id: `intrinsic:${l.id}:layer:${n}`,
			from: `control:${l.id}`,
			event: "change",
			to: `layer:${n}`,
			action: "opacity",
			behavior: "intrinsic",
			status: !e.layers.some((e) => e.id === n) || !t ? "unresolved" : d ? "disabled" : "supported",
			reason: "Temporary layer appearance through the existing exploration channel."
		});
		if (![
			"filterSelect",
			"filterSlider",
			"filterDateRange",
			"timeSlider"
		].includes(l.kind)) continue;
		let p = l.sourceId ? o(e, l.sourceId) : void 0, m = [...s(e, l.sourceId).map((e) => `layer:${e}`), ...e.widgets.filter((t) => (t.kind === "table" || t.kind === "chart") && t.sourceId && o(e, t.sourceId) === p && p !== void 0).map((e) => `widget:${e.id}`)];
		for (let e of m) r.push({
			origin: "intrinsic",
			id: `intrinsic:${l.id}:${e}`,
			from: `control:${l.id}`,
			event: "change",
			to: e,
			action: "source filter",
			behavior: "intrinsic",
			status: t ? u.state === "unsupported" ? "unresolved" : d ? "disabled" : "supported" : "unresolved",
			reason: u.state === "unsupported" ? u.reason : d ? "The source control is pinned." : "Temporary source filter shared by the map and bounded table/chart rows; no authored binding required."
		});
	}
	for (let t of e.widgets) {
		let n = [];
		if (t.kind === "toc") {
			let r = f(t);
			r.ok && (n = r.config.layerIds.length ? r.config.layerIds : e.layers.map((e) => e.id));
		}
		if (t.kind === "compare") {
			let e = d(t);
			e.ok && (n = [e.config.left, e.config.right]);
		}
		if (t.kind === "time") {
			let e = l(t);
			e.ok && (n = e.config.steps.map((e) => e.layerId));
		}
		for (let i of n) {
			let n = e.layers.some((e) => e.id === i), a = e.pins.some((e) => e.kind === "layer" && e.id === i);
			r.push({
				origin: "intrinsic",
				id: `intrinsic:widget:${t.id}:layer:${i}`,
				from: `widget:${t.id}`,
				event: "gesture",
				to: `layer:${i}`,
				action: "setVisibility",
				behavior: "intrinsic",
				status: n ? a ? "disabled" : "supported" : "unresolved",
				reason: n ? a ? "Pinned layer; visibility changes are refused." : "Intrinsic widget action through existing typed visibility commands." : "The referenced layer is absent."
			});
		}
	}
	return r;
}
function b(e, t) {
	let n = new Set(e.interactions.filter((e) => e.on.ref === t || e.do.ref === t).map((e) => `interaction:${e.id}`)), r = e.layers.find((e) => t === `layer:${e.id}`);
	if (r) {
		let i = {
			...e,
			layers: e.layers.filter((e) => e.id !== r.id)
		}, a = v(i), c = /* @__PURE__ */ new Set();
		for (let t of e.controls) {
			if (!s(e, t.sourceId).includes(r.id) || s(i, t.sourceId).length > 0) continue;
			n.add(`control:${t.id}`);
			let a = t.sourceId ? o(e, t.sourceId) : void 0;
			a && [
				"filterSelect",
				"filterSlider",
				"filterDateRange",
				"timeSlider"
			].includes(t.kind) && c.add(a);
		}
		for (let r of v(e)) {
			if (r.ref === t) continue;
			let e = a.find((e) => e.ref === r.ref), i = r.target.kind === "component" && r.sourceId && c.has(r.sourceId);
			(r.sourceId !== e?.sourceId || i) && n.add(r.ref);
		}
	}
	for (let t of e.widgets) {
		if (!r) continue;
		let i = [];
		if (t.kind === "toc" || t.kind === "legend") {
			let n = t.kind === "toc" ? f(t) : p(t);
			n.ok && (i = n.config.layerIds.length ? n.config.layerIds : e.layers.map((e) => e.id));
		} else if (t.kind === "compare") {
			let e = d(t);
			e.ok && (i = [e.config.left, e.config.right]);
		} else if (t.kind === "time") {
			let e = l(t);
			e.ok && (i = e.config.steps.map((e) => e.layerId));
		}
		i.includes(r.id) && n.add(`widget:${t.id}`);
	}
	for (let t of e.controls) {
		let e = i(t);
		r && e.ok && e.config.kind === "opacity" && e.config.config.layerIds.includes(r.id) && n.add(`control:${t.id}`);
	}
	return n.delete(t), [...n];
}
//#endregion
//#region src/design/design-inspector-panel.ts
var x = (e) => String(e).replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll("\"", "&quot;"), S = class {
	#e;
	#t = new AbortController();
	#n = /* @__PURE__ */ new Map();
	#r = /* @__PURE__ */ new Map();
	#i;
	#a = !1;
	constructor(e) {
		this.#e = e, this.#i = e.controller.subscribe(() => queueMicrotask(() => this.refresh())), e.container.addEventListener("click", (e) => this.#c(e), { signal: this.#t.signal }), e.container.addEventListener("keydown", (t) => {
			t.key === "Escape" && (t.preventDefault(), e.close());
		}, { signal: this.#t.signal }), e.canvas?.addEventListener("honua-studio-design-runtime-change", () => this.refresh(), { signal: this.#t.signal }), e.canvas?.addEventListener("honua-studio-widget-data-change", () => this.refresh(), { signal: this.#t.signal }), this.refresh();
	}
	dispose() {
		this.#a = !0, this.#t.abort(), this.#i();
		for (let e of this.#r.values()) e.abort();
		this.#r.clear(), this.#n.clear(), this.#e.container.replaceChildren();
	}
	#o() {
		return v(this.#e.controller.state, this.#e.canvas?.widgetDeck?.inspectInferredTableFields());
	}
	refresh() {
		if (this.#a) return;
		let e = this.#o(), t = new Set(e.flatMap((e) => e.sourceId ? [e.sourceId] : []));
		for (let [e, n] of this.#r) t.has(e) || (n.abort(), this.#r.delete(e), this.#n.delete(e));
		for (let e of this.#n.keys()) t.has(e) || this.#n.delete(e);
		for (let e of t) this.#n.has(e) || this.#n.set(e, {
			status: "loading",
			reason: "Waiting for authorized field description."
		});
		if (this.#l(e), this.#r.size === 0) {
			let e = [...t].find((e) => this.#n.get(e)?.status === "loading");
			e && this.#s(e);
		}
	}
	async #s(e) {
		let t = new AbortController();
		this.#r.set(e, t);
		let n = setTimeout(() => t.abort(), 8e3);
		try {
			let n = await this.#e.describeSource(e, t.signal);
			!this.#a && this.#r.get(e) === t && !t.signal.aborted ? this.#n.set(e, {
				status: "ready",
				fields: n
			}) : !this.#a && this.#r.get(e) === t && this.#n.set(e, {
				status: "unavailable",
				reason: "Field description timed out. Refresh to retry."
			});
		} catch (n) {
			!this.#a && this.#r.get(e) === t && this.#n.set(e, {
				status: "unavailable",
				reason: n instanceof Error ? n.message : String(n)
			});
		} finally {
			clearTimeout(n), this.#r.get(e) === t && this.#r.delete(e), this.refresh();
		}
	}
	#c(e) {
		let t = e.target.closest("button[data-action]");
		if (!t) return;
		let n = t.dataset.action;
		if (n === "close") {
			this.#e.close();
			return;
		}
		if (n === "refresh") {
			for (let e of this.#r.values()) e.abort();
			this.#r.clear(), this.#n.clear(), this.refresh();
			return;
		}
		if (n === "reset") {
			this.#e.canvas?.interactions?.resetTemporaryFilters(), this.refresh();
			return;
		}
		let r = this.#o().find((e) => e.ref === t.dataset.ref);
		r && this.#e.select(r, t.dataset.relationship);
	}
	#l(r) {
		let { container: i, controller: a, canvas: o } = this.#e, s = a.state, c = y(s, o?.interactions?.compiled, o?.designMapAvailable), l = o?.interactions?.temporaryFilterSummary() ?? [], u = new Set(a.selection.map(n)), d = (e, t, i = `consumer:${e}`) => {
			let a = r.find((t) => t.ref === e);
			return a ? `<button type="button" id="design-follow-${x(encodeURIComponent(i))}" data-action="select" data-ref="${x(e)}" ${t ? `data-relationship="${x(t)}"` : ""} aria-pressed="${u.has(n(a.target))}">${x(a.title)} (${x(e)})</button>` : `<span>${x(e)} — ${e === "map" ? "map viewport" : "unresolved reference"}</span>`;
		}, f = [...new Set(r.flatMap((e) => e.sourceId ? [e.sourceId] : []))], p = i.getRootNode() instanceof ShadowRoot ? e(i.getRootNode()) : void 0, m = new Set([...i.querySelectorAll("details[open][data-impact]")].map((e) => e.dataset.impact));
		i.innerHTML = `<style>
      .design-inspector{padding:1rem;border:1px solid var(--hn-line,#aaa);max-height:70vh;overflow:auto;background:var(--hn-surface,#fff);color:var(--hn-ink,#222)}
      .design-inspector button{font:inherit;margin:.2rem;padding:.3rem .6rem;cursor:pointer}.design-inspector button:focus-visible{outline:3px solid #2870ba;outline-offset:2px}
      .design-inspector li{margin:.5rem 0}.design-inspector code{overflow-wrap:anywhere}.design-inspector [aria-pressed=true]{font-weight:bold;text-decoration:underline}
    </style><section class="design-inspector" aria-label="Data and interaction inspector" data-testid="design-inspector">
      <h2>Data and interactions</h2><button type="button" data-action="close">Close inspector</button><button type="button" data-action="refresh">Refresh authorized fields</button>
      <p>Inspecting the current draft. Select an object or follow a connection to use it in the shared conversation.</p>
      <h3>Sources and field consumers</h3>
      <p>Map requests use pages of up to 2,000 features and a 32-page preview bound; tables and charts use loaded bounded rows (default 500). Counts describe loaded rows, never complete-dataset totals. Table paging does not request the full source.</p>
      <p role="status" aria-live="polite">${[...this.#n.values()].filter((e) => e.status === "ready").length} of ${f.length} source field descriptions available.</p>
      ${f.map((e) => {
			let t = this.#n.get(e), n = o?.widgetDeck?.inspectSourceData(e);
			return `<section aria-label="Source ${x(e)}"><h4>Source <code>${x(e)}</code></h4>
          <p>${t?.status === "ready" ? `Described fields: ${t.fields.map((e) => `${x(e.name)} (${x(e.type)})`).join(", ") || "none"}` : `Fields unavailable: ${x(t?.reason ?? "Not described")}`}</p>
          ${n ? `<p>${x(n)}</p>` : ""}
          <ul>${r.filter((t) => t.sourceId === e).map((e) => this.#u(e, d, t)).join("")}</ul></section>`;
		}).join("")}
      ${r.some((e) => !e.sourceId) ? `<h4>Other components and unresolved sources</h4><ul>${r.filter((e) => !e.sourceId).map((e) => this.#u(e, d)).join("")}</ul>` : ""}
      ${r.length === 0 ? "<p>No components in this draft.</p>" : ""}
      <h3>Connections</h3><p>${c.filter((e) => e.status === "supported").length} of ${c.length} described connections supported by this runtime. Shared source filters are intrinsic; authored actions execute once per gesture.</p>
      <ul>${c.map((e) => `<li data-connection="${x(e.id)}" data-connection-origin="${e.origin}"><code>${x(e.id)}</code>: ${d(e.from, e.authoredInteractionId, JSON.stringify([
			e.origin,
			e.id,
			"from"
		]))} → <strong>${x(e.event)} → ${x(e.action)}</strong> → ${d(e.to, e.authoredInteractionId, JSON.stringify([
			e.origin,
			e.id,
			"to"
		]))}<br><span>${x(e.behavior)} · ${x(e.status)}: ${x(e.reason)}</span></li>`).join("") || "<li>No authored or source-filter connections.</li>"}</ul>
      <h3>Temporary exploration filters</h3><ul>${l.map((e) => `<li>${x(e.controlId)}: ${x(e.label)} — sources ${x(e.sourceIds.join(", "))}</li>`).join("") || "<li>No temporary source filters.</li>"}</ul>
      <button type="button" data-action="reset" ${l.length ? "" : "disabled"}>Reset temporary filters</button><p>Reset uses the existing exploration reset and preserves authored constraints, controls and interaction bindings. Layer-specific action filters remain local to their layer.</p>
    </section>`;
		for (let e of i.querySelectorAll("details[data-impact]")) e.open = m.has(e.dataset.impact);
		i.getRootNode() instanceof ShadowRoot && t(i.getRootNode(), p);
	}
	#u(e, t, n) {
		let r = n?.status === "ready" ? e.fields.filter((e) => !n.fields.some((t) => t.name === e)) : [], i = b(this.#e.controller.state, e.ref), a = y(this.#e.controller.state, this.#e.canvas?.interactions?.compiled, this.#e.canvas?.designMapAvailable), o = e.target.kind === "layer" ? this.#e.canvas?.designLayerStatus(e.target.id) : void 0, s = a.filter((t) => t.to === e.ref).map((e) => e.id), c = a.filter((t) => t.from === e.ref).map((e) => e.id);
		return `<li>${t(e.ref)}<p>Fields used: ${e.fields.map(x).join(", ") || "No explicit data fields"}. ${r.length ? `Unavailable fields: ${r.map(x).join(", ")}.` : ""}${e.sourceReference && !e.sourceId ? `Unresolved or ambiguous source reference: ${x(e.sourceReference)}.` : ""} ${x(e.note ?? "")}</p>
      ${o ? `<p>Map rendering unavailable: ${x(o)}</p>` : ""}
      <p>Authored constraints: ${e.constraints.map((e) => `<code>${x(e)}</code>`).join("; ") || "None in the available canonical style"}.</p>
      <p>Incoming: ${s.map(x).join(", ") || "none"}. Outgoing: ${c.map(x).join(", ") || "none"}.</p>
      <details data-impact="${x(e.ref)}"><summary id="design-impact-${x(encodeURIComponent(e.ref))}">Removal and source-change impact for ${x(e.ref)}</summary><p>Affected objects: ${i.map(x).join(", ") || "No explicit dependents"}. Existing typed-command validation applies. Removing a referenced control refuses unless its bindings are explicitly removed or cascaded. Accepted layer/widget removal retains references, which become visibly unresolved; no replacement source is chosen.</p></details></li>`;
	}
};
//#endregion
export { S as DesignInspectorPanel };
