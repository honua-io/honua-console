import { i as e } from "./errors-BIfuuju7.js";
import { a as t, c as n, d as r, f as i, l as a, m as o, r as s, t as c } from "./control-config-DVC01Tzy.js";
//#region node_modules/@honua/sdk-js/dist/src/exploration/types.js
var l = {
	filters: {},
	selection: [],
	sort: [],
	page: {},
	visibleFields: [],
	grouping: [],
	preset: "globalLinked"
}, u = [
	"filters",
	"spatialFilter",
	"extent",
	"selection",
	"sort",
	"page",
	"visibleFields",
	"grouping",
	"aggregation",
	"preset"
], d = [], f = {
	globalLinked: {
		preset: "globalLinked",
		rules: p({
			map: u,
			grid: u,
			chart: u,
			filter: u,
			detail: u,
			form: u,
			custom: u
		})
	},
	mapDriven: {
		preset: "mapDriven",
		rules: p({
			map: [
				"extent",
				"spatialFilter",
				"selection",
				"filters"
			],
			grid: d,
			chart: d,
			filter: ["filters", "spatialFilter"],
			detail: d,
			form: d,
			custom: d
		})
	},
	gridDriven: {
		preset: "gridDriven",
		rules: p({
			map: d,
			grid: [
				"selection",
				"sort",
				"page",
				"filters",
				"visibleFields"
			],
			chart: d,
			filter: ["filters", "spatialFilter"],
			detail: d,
			form: d,
			custom: d
		})
	},
	chartDriven: {
		preset: "chartDriven",
		rules: p({
			map: d,
			grid: d,
			chart: [
				"grouping",
				"aggregation",
				"filters",
				"selection"
			],
			filter: ["filters"],
			detail: d,
			form: d,
			custom: d
		})
	},
	decoupled: {
		preset: "decoupled",
		rules: p({
			map: d,
			grid: d,
			chart: d,
			filter: d,
			detail: d,
			form: d,
			custom: d
		})
	}
};
function p(e) {
	return [
		{
			role: "map",
			propagatesSlices: e.map
		},
		{
			role: "grid",
			propagatesSlices: e.grid
		},
		{
			role: "chart",
			propagatesSlices: e.chart
		},
		{
			role: "filter",
			propagatesSlices: e.filter
		},
		{
			role: "detail",
			propagatesSlices: e.detail
		},
		{
			role: "form",
			propagatesSlices: e.form
		},
		{
			role: "custom",
			propagatesSlices: e.custom
		}
	];
}
function m(e, t) {
	let n = f[e].rules.find((e) => e.role === t);
	return new Set(n?.propagatesSlices ?? d);
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/exploration/reducer.js
function h(e, t) {
	switch (t.kind) {
		case "set-filter": {
			let n = e.filters[t.id];
			if (n && y(n, t.clause)) return {
				state: e,
				changedSlices: g
			};
			let r = {
				...e.filters,
				[t.id]: t.clause
			};
			return {
				state: {
					...e,
					filters: r
				},
				changedSlices: _("filters")
			};
		}
		case "clear-filter": {
			if (!(t.id in e.filters)) return {
				state: e,
				changedSlices: g
			};
			let n = { ...e.filters };
			return delete n[t.id], {
				state: {
					...e,
					filters: n
				},
				changedSlices: _("filters")
			};
		}
		case "set-spatial-filter": return b(e.spatialFilter, t.spatialFilter) ? {
			state: e,
			changedSlices: g
		} : {
			state: O(e, "spatialFilter", t.spatialFilter),
			changedSlices: _("spatialFilter")
		};
		case "set-extent": return S(e.extent, t.extent) ? {
			state: e,
			changedSlices: g
		} : {
			state: O(e, "extent", t.extent),
			changedSlices: _("extent")
		};
		case "select": {
			let n = t.ids, r;
			return r = t.replace === !0 ? D(n) : D([...e.selection, ...n]), E(e.selection, r) ? {
				state: e,
				changedSlices: g
			} : {
				state: {
					...e,
					selection: r
				},
				changedSlices: _("selection")
			};
		}
		case "deselect": {
			if (!t.ids || t.ids.length === 0) return e.selection.length === 0 ? {
				state: e,
				changedSlices: g
			} : {
				state: {
					...e,
					selection: []
				},
				changedSlices: _("selection")
			};
			let n = new Set(t.ids.map((e) => i(e))), r = e.selection.filter((e) => !n.has(i(e)));
			return r.length === e.selection.length ? {
				state: e,
				changedSlices: g
			} : {
				state: {
					...e,
					selection: r
				},
				changedSlices: _("selection")
			};
		}
		case "set-sort": return C(e.sort, t.sort) ? {
			state: e,
			changedSlices: g
		} : {
			state: {
				...e,
				sort: [...t.sort]
			},
			changedSlices: _("sort")
		};
		case "set-page": return w(e.page, t.page) ? {
			state: e,
			changedSlices: g
		} : {
			state: {
				...e,
				page: { ...t.page }
			},
			changedSlices: _("page")
		};
		case "set-visible-fields": return T(e.visibleFields, t.fields) ? {
			state: e,
			changedSlices: g
		} : {
			state: {
				...e,
				visibleFields: [...t.fields]
			},
			changedSlices: _("visibleFields")
		};
		case "set-grouping": return T(e.grouping, t.grouping) ? {
			state: e,
			changedSlices: g
		} : {
			state: {
				...e,
				grouping: [...t.grouping]
			},
			changedSlices: _("grouping")
		};
		case "set-aggregation": return A(e.aggregation, t.aggregation) ? {
			state: e,
			changedSlices: g
		} : {
			state: O(e, "aggregation", t.aggregation),
			changedSlices: _("aggregation")
		};
		case "apply-preset": return e.preset === t.preset ? {
			state: e,
			changedSlices: g
		} : {
			state: {
				...e,
				preset: t.preset
			},
			changedSlices: _("preset")
		};
		case "snapshot-restore": {
			let n = t.snapshot.state, r = k(e, n);
			return r.size === 0 ? {
				state: e,
				changedSlices: g
			} : {
				state: n,
				changedSlices: r
			};
		}
	}
}
var g = /* @__PURE__ */ new Set();
function _(e) {
	return /* @__PURE__ */ new Set([e]);
}
function v(e, t) {
	if (e === t) return !0;
	let n = Object.keys(e), r = Object.keys(t);
	if (n.length !== r.length) return !1;
	for (let r of n) if (!Object.hasOwn(t, r) || !y(e[r], t[r])) return !1;
	return !0;
}
function y(e, t) {
	if (e.field !== t.field || e.operator !== t.operator) return !1;
	if (e.value !== t.value) if (Array.isArray(e.value) && Array.isArray(t.value)) {
		if (e.value.length !== t.value.length) return !1;
		for (let n = 0; n < e.value.length; n++) if (e.value[n] !== t.value[n]) return !1;
	} else return !1;
	return !((e.appliesTo ?? null) !== (t.appliesTo ?? null) && (!e.appliesTo || !t.appliesTo || !T(e.appliesTo, t.appliesTo)));
}
function b(e, t) {
	return e === t ? !0 : !e || !t || e.geometryType !== t.geometryType || (e.spatialRel ?? null) !== (t.spatialRel ?? null) ? !1 : x(e.geometry, t.geometry);
}
function x(e, t) {
	if (e === t) return !0;
	if (typeof e != typeof t) return !1;
	if (Array.isArray(e) || Array.isArray(t)) {
		if (!Array.isArray(e) || !Array.isArray(t) || e.length !== t.length) return !1;
		for (let n = 0; n < e.length; n++) if (!x(e[n], t[n])) return !1;
		return !0;
	}
	if (e && t && typeof e == "object" && typeof t == "object") {
		let n = Object.keys(e), r = Object.keys(t);
		if (n.length !== r.length) return !1;
		for (let r of n) if (!Object.hasOwn(t, r) || !x(e[r], t[r])) return !1;
		return !0;
	}
	return !1;
}
function S(e, t) {
	return e === t ? !0 : !e || !t ? !1 : e.xmin === t.xmin && e.ymin === t.ymin && e.xmax === t.xmax && e.ymax === t.ymax;
}
function C(e, t) {
	if (e === t) return !0;
	if (e.length !== t.length) return !1;
	for (let n = 0; n < e.length; n++) if (e[n].field !== t[n].field || (e[n].direction ?? "asc") !== (t[n].direction ?? "asc")) return !1;
	return !0;
}
function w(e, t) {
	return e.offset === t.offset && e.limit === t.limit;
}
function T(e, t) {
	if (e === t) return !0;
	if (e.length !== t.length) return !1;
	for (let n = 0; n < e.length; n++) if (e[n] !== t[n]) return !1;
	return !0;
}
function E(e, t) {
	if (e === t) return !0;
	if (e.length !== t.length) return !1;
	for (let n = 0; n < e.length; n++) if (i(e[n]) !== i(t[n])) return !1;
	return !0;
}
function D(e) {
	let t = /* @__PURE__ */ new Set(), n = [];
	for (let r of e) {
		let e = i(r);
		t.has(e) || (t.add(e), n.push(r));
	}
	return n;
}
function O(e, t, n) {
	let { spatialFilter: r, extent: i, aggregation: a, ...o } = e, s = {
		...o,
		...r !== void 0 && t !== "spatialFilter" ? { spatialFilter: r } : {},
		...i !== void 0 && t !== "extent" ? { extent: i } : {},
		...a !== void 0 && t !== "aggregation" ? { aggregation: a } : {}
	};
	return n === void 0 ? s : {
		...s,
		[t]: n
	};
}
function k(e, t) {
	let n = /* @__PURE__ */ new Set();
	return v(e.filters, t.filters) || n.add("filters"), b(e.spatialFilter, t.spatialFilter) || n.add("spatialFilter"), S(e.extent, t.extent) || n.add("extent"), E(e.selection, t.selection) || n.add("selection"), C(e.sort, t.sort) || n.add("sort"), w(e.page, t.page) || n.add("page"), T(e.visibleFields, t.visibleFields) || n.add("visibleFields"), T(e.grouping, t.grouping) || n.add("grouping"), A(e.aggregation, t.aggregation) || n.add("aggregation"), e.preset !== t.preset && n.add("preset"), n;
}
function A(e, t) {
	if (e === t) return !0;
	if (!e || !t || !T(e.groupBy ?? [], t.groupBy ?? []) || e.metrics.length !== t.metrics.length) return !1;
	for (let n = 0; n < e.metrics.length; n++) {
		let r = e.metrics[n], i = t.metrics[n];
		if (r.fn !== i.fn || r.field !== i.field || (r.alias ?? null) !== (i.alias ?? null)) return !1;
	}
	return !0;
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/exploration/context.js
function ee(t) {
	let { datasetId: n, sourceIds: r } = t, i = [...r], a = te(t.initialState, t.preset), o = f[a.preset], s = !1, c = /* @__PURE__ */ new Map(), l = /* @__PURE__ */ new Map(), u = !1, d = a, p = /* @__PURE__ */ new Set(), m = /* @__PURE__ */ new Set(), g;
	function _(t) {
		if (s) throw new e("disposed", `ExplorationContext("${n}") cannot ${t} after dispose()`);
	}
	function v() {
		u || (u = !0, queueMicrotask(() => {
			if (u = !1, p.size === 0) return;
			let e = {
				state: a,
				previous: d,
				changedSlices: new Set(p),
				origin: g
			}, t = new Set(m);
			p.clear(), m.clear(), d = a, g = void 0;
			let n = l.get("all");
			if (n) for (let t of [...n]) t(e);
			for (let n of t) {
				let r = l.get(n);
				if (!r) continue;
				let i = {
					state: e.state,
					previous: e.previous,
					changedSlices: t,
					origin: e.origin
				};
				for (let e of [...r]) e(i);
			}
		}));
	}
	return {
		datasetId: n,
		sourceIds: i,
		get state() {
			return a;
		},
		get policy() {
			return o;
		},
		bind(t) {
			_("bind");
			let r = {
				id: t.id,
				role: t.role
			};
			if (c.has(r.id)) throw new e("duplicate-binding", `ExplorationContext("${n}"): a view is already bound with id "${r.id}"`);
			c.set(r.id, r);
			let i = !1;
			return {
				id: r.id,
				unbind() {
					i || (i = !0, c.delete(r.id));
				}
			};
		},
		connectView(t) {
			_("connectView");
			let r = this, i = r.bind(t), a = c.get(i.id) ?? {
				id: i.id,
				role: t.role
			}, o = /* @__PURE__ */ new Set(), s = !1;
			function l(t) {
				if (_(t), s) throw new e("unbound-view", `ExplorationContext("${n}"): view "${a.id}" cannot ${t} after unbind()`);
			}
			function u(e) {
				l("dispatch"), r.dispatch(N(e, a.id));
			}
			function d(e, t, n = {}) {
				l("subscribe");
				let i = ne(e);
				if (i.length === 0) return () => {};
				let s = !0, c = !1, u, d = /* @__PURE__ */ new Set();
				function f() {
					if (c = !1, !s || !u || d.size === 0) return;
					let e = u, n = new Set(d);
					u = void 0, d.clear(), t({
						state: e.state,
						previous: e.previous,
						changedSlices: n,
						origin: e.origin,
						view: a,
						selfOrigin: e.origin?.viewId === a.id
					});
				}
				function p(e) {
					if (s && !(e.origin?.viewId === a.id && !n.includeSelf)) {
						u = e;
						for (let t of e.changedSlices) d.add(t);
						c || (c = !0, queueMicrotask(f));
					}
				}
				let m = i.map((e) => r.subscribe(e, p)), h = () => {
					if (s) {
						s = !1;
						for (let e of m) e();
						o.delete(h);
					}
				};
				return o.add(h), h;
			}
			return {
				id: a.id,
				role: a.role,
				get state() {
					return r.state;
				},
				get policy() {
					return r.policy;
				},
				dispatch: u,
				subscribe: d,
				setFilter(e, t) {
					u({
						kind: "set-filter",
						id: e,
						clause: t
					});
				},
				clearFilter(e) {
					u({
						kind: "clear-filter",
						id: e
					});
				},
				setSpatialFilter(e) {
					u({
						kind: "set-spatial-filter",
						spatialFilter: e
					});
				},
				setExtent(e) {
					u({
						kind: "set-extent",
						extent: e
					});
				},
				select(e, t) {
					u({
						kind: "select",
						ids: e,
						replace: t?.replace
					});
				},
				deselect(e) {
					u({
						kind: "deselect",
						ids: e
					});
				},
				setSort(e) {
					u({
						kind: "set-sort",
						sort: e
					});
				},
				setPage(e) {
					u({
						kind: "set-page",
						page: e
					});
				},
				setVisibleFields(e) {
					u({
						kind: "set-visible-fields",
						fields: e
					});
				},
				setGrouping(e) {
					u({
						kind: "set-grouping",
						grouping: e
					});
				},
				setAggregation(e) {
					u({
						kind: "set-aggregation",
						aggregation: e
					});
				},
				applyPreset(e) {
					u({
						kind: "apply-preset",
						preset: e
					});
				},
				snapshot() {
					return l("snapshot"), r.snapshot();
				},
				restore(e) {
					l("restore"), r.restore(e);
				},
				unbind() {
					if (!s) {
						s = !0;
						for (let e of [...o]) e();
						o.clear(), i.unbind();
					}
				}
			};
		},
		dispatch(e) {
			_("dispatch");
			let t = h(a, e);
			if (t.changedSlices.size === 0) return;
			let n = M(t.changedSlices, e, o, c);
			p.size === 0 && (d = a), a = t.state;
			for (let e of t.changedSlices) p.add(e);
			for (let e of n) m.add(e);
			g = e, e.kind === "apply-preset" ? o = f[e.preset] : e.kind === "snapshot-restore" && (o = f[a.preset]), p.size > 0 && v();
		},
		subscribe(e, t) {
			_("subscribe");
			let n = l.get(e);
			return n || (n = /* @__PURE__ */ new Set(), l.set(e, n)), n.add(t), () => {
				let n = l.get(e);
				n && (n.delete(t), n.size === 0 && l.delete(e));
			};
		},
		snapshot() {
			return {
				version: 1,
				state: j(a)
			};
		},
		restore(t) {
			if (_("restore"), t.version !== 1) throw new e("incompatible-snapshot", `ExplorationContext("${n}"): snapshot version ${String(t.version)} is not supported`);
			this.dispatch({
				kind: "snapshot-restore",
				snapshot: {
					version: 1,
					state: j(t.state)
				}
			});
		},
		dispose() {
			s || (s = !0, c.clear(), l.clear(), p.clear(), m.clear(), u = !1);
		}
	};
}
function te(e, t) {
	let n = {
		...l,
		...e ?? {},
		preset: t ?? e?.preset ?? l.preset
	};
	return structuredClone(n);
}
function j(e) {
	return structuredClone(e);
}
function M(e, t, n, r) {
	if (t.kind === "apply-preset" || t.kind === "snapshot-restore" || !t.viewId) return e;
	let i = r.get(t.viewId);
	if (!i) return e;
	let a = m(n.preset, i.role);
	if (a.size === 0) return /* @__PURE__ */ new Set();
	let o = /* @__PURE__ */ new Set();
	for (let t of e) a.has(t) && o.add(t);
	return o;
}
function N(e, t) {
	return {
		...e,
		viewId: t
	};
}
function ne(e) {
	return [...new Set(Array.isArray(e) ? e : [e])];
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/exploration/selectors.js
function P(e, t, n, r, i = {}) {
	let a = i.equals ?? F, o = n(e.state);
	return i.fireImmediately && r(o, void 0), e.subscribe(t, (e) => {
		let t = n(e.state);
		a(o, t) || (o = t, r(t, e));
	}, i);
}
function F(e, t) {
	if (Object.is(e, t)) return !0;
	if (!e || !t || typeof e != "object" || typeof t != "object" || Object.getPrototypeOf(e) !== Object.getPrototypeOf(t)) return !1;
	if (e instanceof Date && t instanceof Date) return e.getTime() === t.getTime();
	if (e instanceof RegExp && t instanceof RegExp) return e.source === t.source && e.flags === t.flags;
	if (Array.isArray(e) || Array.isArray(t)) {
		if (!Array.isArray(e) || !Array.isArray(t) || e.length !== t.length) return !1;
		for (let n = 0; n < e.length; n++) if (!F(e[n], t[n])) return !1;
		return !0;
	}
	let n = e, r = t, i = Object.keys(n), a = Object.keys(r);
	if (i.length !== a.length || i.length === 0) return !1;
	for (let e of i) if (!Object.hasOwn(r, e) || !F(n[e], r[e])) return !1;
	return !0;
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/interactions/feature-state.js
function re(e, t) {
	let { source: n, layer: r, stateKey: i = "hover", sourceLayer: a, onHoverTargetChange: s } = t, c;
	function l(...t) {
		let r = t[0]?.features?.[0]?.id;
		if (r == null) return;
		let l = c !== r;
		c !== void 0 && l && e.setFeatureState({
			source: n,
			id: c,
			sourceLayer: a
		}, { [i]: !1 }), c = r, e.setFeatureState({
			source: n,
			id: c,
			sourceLayer: a
		}, { [i]: !0 }), l && s?.(o(n, r, { sourceLayer: a }));
	}
	function u() {
		c !== void 0 && (e.setFeatureState({
			source: n,
			id: c,
			sourceLayer: a
		}, { [i]: !1 }), c = void 0, s?.(void 0));
	}
	return e.on("mousemove", r, l), e.on("mouseleave", r, u), {
		remove() {
			u(), e.off("mousemove", r, l), e.off("mouseleave", r, u);
		},
		get hoveredId() {
			return c;
		}
	};
}
function ie(e, t) {
	let { source: n, layer: r, stateKey: i = "selected", multiSelect: a = !1, sourceLayer: s, onChange: c, onSelectionTargetsChange: l } = t, u = /* @__PURE__ */ new Set();
	function d() {
		for (let t of u) e.setFeatureState({
			source: n,
			id: t,
			sourceLayer: s
		}, { [i]: !1 });
		u.clear();
	}
	function f() {
		c?.(u), l?.(p());
	}
	function p() {
		return [...u].map((e) => o(n, e, { sourceLayer: s }));
	}
	function m(...t) {
		let r = t[0]?.features?.[0]?.id;
		r != null && (u.has(r) ? (e.setFeatureState({
			source: n,
			id: r,
			sourceLayer: s
		}, { [i]: !1 }), u.delete(r)) : (a || d(), e.setFeatureState({
			source: n,
			id: r,
			sourceLayer: s
		}, { [i]: !0 }), u.add(r)), f());
	}
	return e.on("click", r, m), {
		remove() {
			d(), e.off("click", r, m);
		},
		get selectedIds() {
			return u;
		},
		get selectedTargets() {
			return p();
		},
		clearSelection() {
			d(), f();
		},
		select(t) {
			a || d(), e.setFeatureState({
				source: n,
				id: t,
				sourceLayer: s
			}, { [i]: !0 }), u.add(t), f();
		},
		deselect(t) {
			u.has(t) && (e.setFeatureState({
				source: n,
				id: t,
				sourceLayer: s
			}, { [i]: !1 }), u.delete(t), f());
		}
	};
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/interactions/exploration-bindings.js
function I(e) {
	return {
		setFilter(t, n) {
			e.setFilter(t, n);
		},
		clearFilter(t) {
			e.clearFilter(t);
		},
		subscribe(t, n) {
			return P(e, "filters", (e) => e.filters, t, n);
		}
	};
}
function L(e, t, n) {
	return e.subscribe("selection", (e) => t(e.state.selection, e), n);
}
function R(e) {
	return {
		select(t, n) {
			e.select(t, n);
		},
		clearSelection() {
			e.deselect();
		},
		subscribe(t, n) {
			return L(e, t, n);
		}
	};
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/interactions/declarative.js
var z = {
	bindings: [],
	disabled: []
};
function B(e, t) {
	let n = r(e, { ...t.fanOutCap === void 0 ? {} : { fanOutCap: t.fanOutCap } }), i = t.components ?? {}, a = Array.isArray(e) ? e : [], o = n.ok ? V(a, i) : [], s = [...n.issues, ...o];
	if (s.length > 0) return {
		ok: !1,
		issues: s,
		...z,
		unsupported: [],
		dispose: () => void 0
	};
	let c = new U(t, i);
	for (let e of a) c.compile(e);
	return c.result();
}
function V(e, t) {
	let n = [];
	return e.forEach((e, r) => {
		for (let i of ["on", "do"]) {
			let a = e[i].ref;
			H(a, t) === void 0 && n.push({
				code: "invalid-ref",
				interactionId: e.id,
				path: `interactions[${r}].${i}.ref`,
				message: `\`${a}\` does not resolve to a declared component.`
			});
		}
	}), n;
}
function H(e, t) {
	let r = n(e);
	if (!r) return;
	if (r.kind === "map") return t.map ? {
		kind: "map",
		id: "map",
		component: t.map
	} : void 0;
	let i = r.id, a = {
		layer: t.layers,
		widget: t.widgets,
		control: t.controls
	}[r.kind]?.[i];
	if (a) return {
		kind: r.kind,
		id: i,
		component: a
	};
}
var U = class {
	#e;
	#t;
	#n;
	#r = [];
	#i = [];
	#a = [];
	#o = !1;
	#s = !1;
	constructor(e, t) {
		this.#e = e.view, this.#t = t, this.#n = e.onDispatch;
	}
	compile(e) {
		if (e.disabled === !0) {
			this.#a.push(e.id);
			return;
		}
		let t = H(e.on.ref, this.#t), n = H(e.do.ref, this.#t), r = this.#u(e, n);
		if (typeof r == "string") {
			this.#c(e, r);
			return;
		}
		let i = this.#l(e, t, r);
		if (typeof i == "string") {
			this.#c(e, i);
			return;
		}
		let a = !1;
		this.#r.push({
			interactionId: e.id,
			on: {
				ref: e.on.ref,
				event: e.on.event
			},
			do: {
				ref: e.do.ref,
				verb: e.do.verb
			},
			pair: Q(e),
			dispose: () => {
				a || (a = !0, i());
			}
		});
	}
	result() {
		let e = [...this.#r];
		return {
			ok: this.#i.length === 0,
			issues: [],
			bindings: e,
			unsupported: [...this.#i],
			disabled: [...this.#a],
			dispose: () => {
				if (!this.#s) {
					this.#s = !0;
					for (let t of e) t.dispose();
				}
			}
		};
	}
	#c(e, t) {
		this.#i.push({
			interactionId: e.id,
			on: {
				ref: e.on.ref,
				event: e.on.event
			},
			do: {
				ref: e.do.ref,
				verb: e.do.verb
			},
			pair: Q(e),
			reason: t
		});
	}
	#l(e, t, n) {
		let r = (e) => {
			if (!(this.#s || this.#o)) {
				this.#o = !0;
				try {
					n(e);
				} finally {
					this.#o = !1;
				}
			}
		};
		switch (e.on.event) {
			case "featureSelect": {
				if (t.kind !== "layer") return `\`featureSelect\` is a layer event; \`${e.on.ref}\` is not a layer.`;
				let n = t.component.map;
				if (!n) return `Layer "${t.id}" declares no \`map\`, so \`featureSelect\` has no primitive to bind to.`;
				let i = t.component.sourceId ?? t.id, a = ie(n, {
					source: i,
					layer: t.component.layerId ?? t.id,
					...t.component.sourceLayer ? { sourceLayer: t.component.sourceLayer } : {},
					...t.component.multiSelect === void 0 ? {} : { multiSelect: t.component.multiSelect },
					onSelectionTargetsChange: (e) => r(W(i, e))
				});
				return () => a.remove();
			}
			case "featureHover": {
				if (t.kind !== "layer") return `\`featureHover\` is a layer event; \`${e.on.ref}\` is not a layer.`;
				let n = t.component.map;
				if (!n) return `Layer "${t.id}" declares no \`map\`, so \`featureHover\` has no primitive to bind to.`;
				let i = t.component.sourceId ?? t.id, a = re(n, {
					source: i,
					layer: t.component.layerId ?? t.id,
					...t.component.sourceLayer ? { sourceLayer: t.component.sourceLayer } : {},
					onHoverTargetChange: (e) => r(G(i, e))
				});
				return () => a.remove();
			}
			case "selection": return t.kind === "widget" ? R(this.#e).subscribe((e) => r(K(e))) : `\`selection\` is a widget event; \`${e.on.ref}\` is not a widget.`;
			case "change": {
				if (t.kind !== "control") return `\`change\` is a control event; \`${e.on.ref}\` is not a control.`;
				let n = t.id, i = this.#e.state.filters[n];
				return I(this.#e).subscribe((e) => {
					let t = e[n];
					t !== i && (i = t, r(q(n, t, e)));
				});
			}
			case "viewportChange": return t.kind === "map" ? P(this.#e, "extent", (e) => e.extent, (e) => r(J(e)), { fireImmediately: !1 }) : "`viewportChange` is a map event; only `map` publishes it.";
		}
	}
	#u(e, t) {
		let n = e.do.verb, r = e.do.args, i = (t) => {
			this.#n?.({
				interactionId: e.id,
				event: e.on.event,
				verb: n,
				ref: e.do.ref,
				args: t
			});
		};
		switch (n) {
			case "setFilter": {
				let e = ae(t), n = t.kind === "map" ? "map" : t.id;
				return (t) => {
					let o = a(r, t), s = oe(o);
					e ? e(s) : s ? this.#e.setFilter(n, s) : this.#e.clearFilter(n), i(o);
				};
			}
			case "setViewport": {
				if (t.kind !== "map") return "`setViewport` targets `map`; no other component accepts it.";
				let e = t.component;
				return (t) => {
					let n = a(r, t), o = ce(n);
					e.setViewport ? e.setViewport(o) : this.#e.setExtent(Z(o.bbox)), i(n);
				};
			}
			case "selectFeature": {
				if (t.kind === "control") return "`selectFeature` has no control-side primitive.";
				let e = t.kind === "layer" ? t.component.sourceId ?? t.id : t.id, n = R(this.#e);
				return (t) => {
					let s = a(r, t), c = s.featureId ?? s.id;
					if (c == null) {
						n.clearSelection(), i(s);
						return;
					}
					let l = o(typeof s.sourceId == "string" ? s.sourceId : e, c);
					n.select([l], { replace: s.replace !== !1 }), i(s);
				};
			}
			case "runWidgetQuery": {
				if (t.kind !== "widget") return "`runWidgetQuery` targets `widget:{id}`; no other component accepts it.";
				let e = t.component.runQuery;
				return e ? (t) => {
					let n = a(r, t);
					e(n), i(n);
				} : `Widget "${t.id}" declares no \`runQuery\`, and there is no exploration-level fallback for \`runWidgetQuery\`.`;
			}
			case "setVisibility": {
				if (t.kind === "map") return "`setVisibility` targets a layer, widget, or control, never `map`.";
				let e = t.component.setVisibility;
				return e ? (t) => {
					let n = a(r, t);
					e(n.visible !== !1), i(n);
				} : `${t.kind === "layer" ? "Layer" : t.kind === "widget" ? "Widget" : "Control"} "${t.id}" declares no \`setVisibility\`.`;
			}
		}
	}
};
function W(e, t) {
	let n = t[0];
	return {
		sourceId: n?.sourceId ?? e,
		...n ? { featureId: n.id } : {},
		...n?.sourceLayer ? { sourceLayer: n.sourceLayer } : {},
		targets: t,
		count: t.length
	};
}
function G(e, t) {
	return {
		sourceId: t?.sourceId ?? e,
		...t ? { featureId: t.id } : {},
		...t?.sourceLayer ? { sourceLayer: t.sourceLayer } : {},
		...t ? { target: t } : {},
		hovering: t !== void 0
	};
}
function K(e) {
	let t = e[0], n = typeof t == "object" && t ? t : void 0;
	return {
		targets: e,
		count: e.length,
		...n ? {
			sourceId: n.sourceId,
			featureId: n.id
		} : {},
		...n === void 0 && t !== void 0 ? { featureId: t } : {}
	};
}
function q(e, t, n) {
	return {
		id: e,
		...t ? {
			field: t.field,
			operator: t.operator,
			value: t.value,
			clause: t
		} : {},
		filters: n
	};
}
function J(e) {
	return { ...e ? {
		bbox: [
			e.xmin,
			e.ymin,
			e.xmax,
			e.ymax
		],
		extent: e
	} : {} };
}
function ae(e) {
	if (e.kind === "map") return;
	let t = e.component.setFilter;
	return t ? (e) => t(e) : void 0;
}
var Y = /* @__PURE__ */ new Set([
	"=",
	"!=",
	"<",
	"<=",
	">",
	">=",
	"in",
	"not-in",
	"between",
	"like",
	"is-null",
	"is-not-null"
]);
function oe(e) {
	let t = e.clause;
	if (t && typeof t == "object" && !Array.isArray(t)) {
		let e = t;
		if (typeof e.field == "string") return {
			field: e.field,
			operator: Y.has(e.operator) ? e.operator : "=",
			value: e.value,
			...Array.isArray(e.appliesTo) ? { appliesTo: e.appliesTo } : {}
		};
	}
	if (typeof e.field == "string" && !(e.value === void 0 && !se(e.operator))) return {
		field: e.field,
		operator: Y.has(e.operator) ? e.operator : "=",
		value: e.value,
		...Array.isArray(e.appliesTo) ? { appliesTo: e.appliesTo } : {}
	};
}
function se(e) {
	return e === "is-null" || e === "is-not-null";
}
function ce(e) {
	let t = X(e.bbox, 4), n = X(e.center, 2);
	return {
		...t ? { bbox: t } : {},
		...n ? { center: n } : {},
		...typeof e.zoom == "number" ? { zoom: e.zoom } : {},
		...typeof e.pitch == "number" ? { pitch: e.pitch } : {},
		...typeof e.bearing == "number" ? { bearing: e.bearing } : {}
	};
}
function X(e, t) {
	if (!(!Array.isArray(e) || e.length !== t)) return e.every((e) => typeof e == "number" && Number.isFinite(e)) ? e : void 0;
}
function Z(e) {
	return e ? {
		xmin: e[0],
		ymin: e[1],
		xmax: e[2],
		ymax: e[3]
	} : void 0;
}
function Q(e) {
	return `${e.on.event} -> ${e.do.verb}`;
}
//#endregion
//#region src/interactions/filter-expression.ts
function le(e) {
	return e === "$id" || e === "id" ? ["id"] : ["get", e];
}
function $(e) {
	return typeof e == "string" || typeof e == "number" || typeof e == "boolean";
}
function ue(e) {
	if (!e || !e.field) return;
	let t = le(e.field);
	switch (e.operator) {
		case "=":
		case "!=":
		case "<":
		case "<=":
		case ">":
		case ">=": return $(e.value) ? [
			e.operator === "=" ? "==" : e.operator,
			t,
			e.value
		] : void 0;
		case "in":
		case "not-in": {
			let n = Array.isArray(e.value) ? e.value.filter($) : void 0;
			if (!n || n.length === 0) return;
			let r = [
				"in",
				t,
				["literal", n]
			];
			return e.operator === "in" ? r : ["!", r];
		}
		case "between": {
			let [n, r] = (Array.isArray(e.value) ? e.value : void 0) ?? [];
			return typeof n != "number" || typeof r != "number" ? void 0 : [
				"all",
				[
					">=",
					t,
					n
				],
				[
					"<=",
					t,
					r
				]
			];
		}
		case "like": {
			if (typeof e.value != "string") return;
			let n = e.value.replace(/^%|%$/g, "");
			return n.includes("%") || n.includes("_") ? void 0 : [
				">=",
				[
					"index-of",
					n,
					["to-string", t]
				],
				0
			];
		}
		case "is-null": return [
			"==",
			["to-string", t],
			""
		];
		case "is-not-null": return [
			"!=",
			["to-string", t],
			""
		];
	}
}
function de(e, t) {
	let n = [], r = /* @__PURE__ */ new Set();
	for (let i of e) {
		if (i.appliesTo && i.appliesTo.length > 0 && !i.appliesTo.some((e) => t.includes(e))) continue;
		let e = ue(i);
		if (!e) continue;
		let a = JSON.stringify(e);
		r.has(a) || (r.add(a), n.push(e));
	}
	if (n.length !== 0) return n.length === 1 ? n[0] : ["all", ...n];
}
//#endregion
//#region src/interactions/studio-interactions.ts
var fe = "$honua:opacity", pe = {
	filters: {},
	opacity: {}
}, me = class {
	#e;
	#t;
	#n;
	#r;
	#i;
	#a;
	#o;
	#s;
	#c;
	#l;
	#u;
	#d = /* @__PURE__ */ new Map();
	#f = pe;
	#p = "";
	#m = !1;
	constructor(e) {
		this.#o = e, this.#e = e.controller, this.#t = ee({
			datasetId: "honua-studio-composition",
			sourceIds: e.controller.state.layers.map((e) => e.sourceId)
		}), this.#n = this.#t.connectView({
			id: "controls",
			role: "filter"
		}), this.#r = this.#t.connectView({
			id: "interactions",
			role: "custom"
		}), this.#i = this.#t.connectView({
			id: "appearance",
			role: "map"
		}), this.#a = I(this.#n), this.#s = I(this.#i).subscribe(() => this.#S(), { includeSelf: !0 }), this.#l = R(this.#r).subscribe((e) => this.#y(e), { includeSelf: !0 }), this.#c = this.#e.subscribe(() => this.#h()), this.#_();
	}
	get compiled() {
		return this.#u;
	}
	get appearance() {
		return this.#f;
	}
	get context() {
		return this.#t;
	}
	publishControlChange(e, t) {
		this.#m || (t === void 0 ? this.#a.clearFilter(e) : this.#a.setFilter(e, t));
	}
	publishOpacity(e, t) {
		this.publishControlChange(e, {
			field: fe,
			operator: "=",
			value: t
		});
	}
	clauseFor(e) {
		return this.#n.state.filters[e];
	}
	dispose() {
		this.#m = !0, this.#s?.(), this.#s = void 0, this.#c?.(), this.#c = void 0, this.#l?.(), this.#l = void 0, this.#u?.dispose(), this.#u = void 0, this.#t.dispose();
	}
	#h() {
		this.#g() !== this.#p && this.#_(), this.#S();
	}
	#g() {
		let e = this.#e.state;
		return JSON.stringify([
			e.interactions,
			e.controls.map((e) => e.id),
			e.layers.map((e) => e.id),
			e.widgets.map((e) => e.id)
		]);
	}
	#_() {
		this.#p = this.#g(), this.#u?.dispose(), this.#d.clear();
		let e = {
			view: this.#r,
			components: this.#v(),
			...this.#o.onDispatch === void 0 ? {} : { onDispatch: this.#o.onDispatch }
		};
		this.#u = B(this.#e.state.interactions, e);
	}
	#v() {
		let e = this.#e.state, t = {};
		for (let n of e.layers) t[n.id] = {
			sourceId: n.sourceId,
			setVisibility: (e) => {
				this.#b({
					name: "setVisibility",
					target: {
						kind: "layer",
						id: n.id
					},
					visible: e
				});
			},
			setFilter: (e) => {
				this.#x(n.id, e);
			}
		};
		let n = {};
		for (let t of e.widgets) n[t.id] = {};
		let r = {};
		for (let t of e.controls) r[t.id] = {};
		return {
			map: { setViewport: (e) => {
				this.#b({
					name: "setView",
					view: { ...e }
				});
			} },
			layers: t,
			widgets: n,
			controls: r
		};
	}
	#y(e) {
		if (this.#m) return;
		let t = [];
		for (let n of e) typeof n != "object" || !n || t.push({
			kind: "feature",
			sourceId: n.sourceId,
			featureId: n.id
		});
		this.#e.select(t);
	}
	#b(e) {
		try {
			this.#e.apply(e);
		} catch {}
	}
	#x(e, t) {
		let n = this.#d.get(e) ?? /* @__PURE__ */ new Map();
		t === void 0 ? n.delete("verb") : n.set("verb", t), n.size === 0 ? this.#d.delete(e) : this.#d.set(e, n), this.#S();
	}
	#S() {
		if (this.#m) return;
		let e = this.#e.state, t = this.#i.state.filters, n = /* @__PURE__ */ new Map(), r = {};
		for (let i of e.controls) {
			let a = t[i.id];
			if (a) {
				if (a.field === "$honua:opacity") {
					for (let t of he(e, i)) {
						let e = typeof a.value == "number" ? a.value : Number(a.value);
						Number.isFinite(e) && (r[t] = Math.min(Math.max(e, 0), 1));
					}
					continue;
				}
				if (c[i.kind]) for (let t of s(e, i.sourceId)) {
					let e = n.get(t) ?? [];
					e.push(a), n.set(t, e);
				}
			}
		}
		for (let [e, t] of this.#d) {
			let r = n.get(e) ?? [];
			r.push(...t.values()), n.set(e, r);
		}
		let i = {};
		for (let [t, r] of n) {
			let n = e.layers.find((e) => e.id === t), a = de(r, n ? [n.id, n.sourceId] : [t]);
			a && (i[t] = a);
		}
		let a = {
			filters: i,
			opacity: r
		};
		ge(a, this.#f) || (this.#f = a, this.#o.onAppearanceChange?.(a));
	}
};
function he(e, n) {
	if (n.kind !== "opacity") return [];
	let r = t(n);
	return !r.ok || r.config.kind !== "opacity" ? [] : r.config.config.layerIds.filter((t) => e.layers.some((e) => e.id === t));
}
function ge(e, t) {
	return JSON.stringify(e) === JSON.stringify(t);
}
//#endregion
export { me as StudioInteractionRuntime };
