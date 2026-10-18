import { n as e, t } from "./error-envelope-C9J-pNSA.js";
import { s as n } from "./model-CqQ-US3h.js";
import { a as r, n as i, r as a, s as o, t as s } from "./source-resolution-DLJUVzYE.js";
import { r as c, t as l } from "./constants-B8tqYbYF.js";
import { n as u, r as d } from "./palette-BSfbwTvA.js";
import { installAuthenticatedSourceProtocol as f } from "./authenticated-source-protocol-Bnh3Pz5D.js";
import { n as p } from "./basemap-BCNqaLJX.js";
//#region node_modules/@honua/sdk-js/dist/src/runtime/errors.js
var m = class extends t {
	packageId;
	stage;
	detail;
	cause;
	constructor(t, n) {
		super(h[n.stage], t, {
			cause: n.cause,
			operationId: n.operationId,
			requestId: n.requestId,
			context: e(n.context, {
				packageId: n.packageId,
				stage: n.stage
			})
		}), this.name = "HonuaMapPackageError", this.packageId = n.packageId, this.stage = n.stage, this.detail = n.detail, this.cause = n.cause;
	}
}, h = {
	fetch: "runtime.map-package.fetch",
	load: "runtime.map-package.load",
	export: "runtime.map-package.export",
	import: "runtime.map-package.import",
	validate: "runtime.map-package.validate",
	update: "runtime.map-package.update",
	"style-compose": "runtime.map-package.style-compose",
	"source-bind": "runtime.map-package.source-bind",
	view: "runtime.map-package.view",
	popup: "runtime.map-package.popup",
	dispose: "runtime.map-package.dispose"
};
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/runtime/style-compose.js
async function g(e, t, n) {
	try {
		let r = await _(t, e, n.resolveStyleRef), i = await y(e, n.resolveTheme);
		return w(i ? v(r, i) : r);
	} catch (t) {
		throw t instanceof m ? t : new m("style composition failed", {
			packageId: e.mapPackageId,
			stage: "style-compose",
			cause: t
		});
	}
}
async function _(e, t, n) {
	let r = t.styleRefs ?? [];
	if (r.length === 0) return e;
	let i = e.layers.map((e) => x(e));
	for (let e of r) {
		let r = e.body ?? (n ? await n(e.styleId, e.presetId) : void 0);
		if (!r) throw new m(`StyleRef "${e.styleId}" has no inline body and no resolver was provided`, {
			packageId: t.mapPackageId,
			stage: "style-compose",
			detail: { styleId: e.styleId }
		});
		for (let [e, t] of Object.entries(r)) {
			let n = i.findIndex((t) => t.id === e);
			n !== -1 && (i[n] = b(i[n], t));
		}
	}
	return {
		...e,
		layers: i
	};
}
function v(e, t) {
	let n = t.tokens;
	if (!n) return e;
	let r = e.layers.map((e) => S(e, n));
	return {
		...e,
		layers: r
	};
}
async function y(e, t) {
	if (e.theme) return e.theme;
	if (e.themeId && t) try {
		return await t(e.themeId);
	} catch (t) {
		throw new m(`theme resolution failed for themeId "${e.themeId}"`, {
			packageId: e.mapPackageId,
			stage: "style-compose",
			detail: { themeId: e.themeId },
			cause: t
		});
	}
}
function b(e, t) {
	return {
		...e,
		...t.filter === void 0 ? {} : { filter: t.filter },
		...t.minzoom === void 0 ? {} : { minzoom: t.minzoom },
		...t.maxzoom === void 0 ? {} : { maxzoom: t.maxzoom },
		...C("paint", {
			...e.paint ?? {},
			...t.paint ?? {}
		}),
		...C("layout", {
			...e.layout ?? {},
			...t.layout ?? {}
		}),
		...C("metadata", {
			...e.metadata ?? {},
			...t.metadata ?? {}
		})
	};
}
function x(e) {
	return {
		...e,
		...C("paint", e.paint ? { ...e.paint } : void 0),
		...C("layout", e.layout ? { ...e.layout } : void 0),
		...C("metadata", e.metadata ? { ...e.metadata } : void 0)
	};
}
function S(e, t) {
	return {
		...e,
		...C("paint", e.paint ? D(e.paint, t) : void 0),
		...C("layout", e.layout ? D(e.layout, t) : void 0)
	};
}
function C(e, t) {
	return !t || Object.keys(t).length === 0 ? {} : { [e]: t };
}
function w(e) {
	return T(e);
}
function T(e) {
	if (Array.isArray(e)) {
		let t = !1, n = e.map((e) => {
			let n = T(e);
			return n !== e && (t = !0), n;
		});
		return t ? n : e;
	}
	if (!E(e)) return e;
	let t = !1, n = {};
	for (let [r, i] of Object.entries(e)) {
		if (i === void 0) {
			t = !0;
			continue;
		}
		let e = T(i);
		e !== i && (t = !0), n[r] = e;
	}
	return t ? n : e;
}
function E(e) {
	if (typeof e != "object" || !e) return !1;
	let t = Object.getPrototypeOf(e);
	return t === Object.prototype || t === null;
}
function D(e, t) {
	let n = {};
	for (let [r, i] of Object.entries(e)) n[r] = k(i, t);
	return n;
}
var O = /^\{theme:([^}]+)\}$/;
function k(e, t) {
	if (typeof e == "string") {
		let n = e.match(O);
		if (n) {
			let r = t[n[1]];
			return r === void 0 ? e : r;
		}
		return e;
	}
	return Array.isArray(e) ? e.map((e) => k(e, t)) : e && typeof e == "object" ? D(e, t) : e;
}
//#endregion
//#region src/styles/composition-style-override.ts
function A(e, t, n, r) {
	let i = { ...e.paint };
	if (r?.opacity !== void 0) {
		let e = Math.min(1, Math.max(0, r.opacity)), n = `${t}-opacity`, a = i[n] ?? 1;
		i[n] = typeof a == "number" ? a * e : [
			"*",
			a,
			e
		];
	}
	let a = r?.filter === void 0 ? e.filter : e.filter === void 0 ? r.filter : [
		"all",
		e.filter,
		r.filter
	];
	return {
		...e,
		paint: i,
		layout: {
			...e.layout,
			visibility: n ? "visible" : "none"
		},
		...a === void 0 ? {} : { filter: a }
	};
}
//#endregion
//#region src/map/style-presets.ts
function j(e, t) {
	switch (e) {
		case "fill": return {
			"fill-color": t,
			"fill-opacity": .45
		};
		case "line": return {
			"line-color": t,
			"line-width": 1.75
		};
		case "circle": return {
			"circle-color": t,
			"circle-radius": 4,
			"circle-stroke-color": "#ffffff",
			"circle-stroke-width": 1
		};
		case "raster": return { "raster-opacity": 1 };
	}
}
function M(e, t) {
	let n = Math.min(Math.max(t, 0), 1);
	switch (e) {
		case "fill": return { "fill-opacity": .45 * n };
		case "line": return { "line-opacity": n };
		case "circle": return {
			"circle-opacity": n,
			"circle-stroke-opacity": n
		};
		case "raster": return { "raster-opacity": n };
	}
}
function N(e) {
	return {
		"line-color": e,
		"line-width": 1
	};
}
function P(e, t, n) {
	return {
		paint: {
			...j(t, d(e)),
			...n === void 0 ? {} : M(t, n)
		},
		metadata: { "honua:styleRefFallback": e }
	};
}
//#endregion
//#region src/map/map-package-projection.ts
function F(e) {
	return {
		...e,
		sources: { ...e.sources },
		layers: e.layers.map((e) => ({ ...e }))
	};
}
function I(e) {
	return e;
}
function L(e, t, n, r) {
	let i = u(e.id), a = r?.opacity?.[e.id], o = r?.filters?.[e.id], s = {
		id: e.id,
		type: I(t),
		source: n,
		...o === void 0 ? {} : { filter: o },
		paint: {
			...j(t, i),
			...a === void 0 ? {} : M(t, a)
		},
		metadata: {
			"honua:compositionLayerId": e.id,
			"honua:compositionSourceId": e.sourceId,
			...e.title ? { "honua:title": e.title } : {}
		}
	};
	return e.visible || (s.layout = { visibility: "none" }), s;
}
function R(e, t = {}) {
	let n = F(t.basemap ?? p()), l = [], d = [], f = [], m = [], h = /* @__PURE__ */ new Set();
	for (let p of e.layers) {
		let e = r(p, {
			...t.catalog === void 0 ? {} : { catalog: t.catalog },
			...t.baseUrl === void 0 ? {} : { baseUrl: t.baseUrl },
			...t.featureLimit === void 0 ? {} : { featureLimit: t.featureLimit }
		});
		if (!a(e)) {
			f.push({
				layerId: p.id,
				sourceId: p.sourceId,
				reason: e.reason
			});
			continue;
		}
		let g = t.authoritativeStyles?.[p.id], _ = t.authoritativeStyles !== void 0 && (g !== void 0 || o(s(p.sourceId, t.catalog)));
		if (p.styleRef && _ && i(p)) {
			f.push({
				layerId: p.id,
				sourceId: p.sourceId,
				reason: "Authoritative styles cannot certify a renderer source override."
			});
			continue;
		}
		if (p.styleRef && _ && (!g || "reason" in g || g.definition.styleId !== p.styleRef.styleId || g.sourceId !== p.sourceId)) {
			f.push({
				layerId: p.id,
				sourceId: p.sourceId,
				reason: g && "reason" in g ? g.reason : "Authoritative style binding is unavailable or stale."
			});
			continue;
		}
		let v = p.sourceId;
		if (h.has(v) || (h.add(v), n.sources[v] = e.source, e.binding && l.push(e.binding)), n.layers.push(L(p, e.layerType, v, t.appearance)), e.layerType === "fill") {
			let e = t.appearance?.filters?.[p.id], r = t.appearance?.opacity?.[p.id];
			n.layers.push({
				id: `${p.id}${c}`,
				type: "line",
				source: v,
				...e === void 0 ? {} : { filter: e },
				paint: {
					...N(u(p.id)),
					...r === void 0 ? {} : { "line-opacity": Math.min(Math.max(r, 0), 1) }
				},
				...p.visible ? {} : { layout: { visibility: "none" } }
			});
		}
		if (m.push(p.id), p.styleRef) {
			let n = g && !("reason" in g) ? A(g.override, e.layerType, p.visible, {
				filter: t.appearance?.filters?.[p.id],
				opacity: t.appearance?.opacity?.[p.id]
			}) : void 0, r = (n ? {
				[p.id]: n,
				...e.layerType === "fill" ? { [`${p.id}__outline`]: {
					...n.filter === void 0 ? {} : { filter: n.filter },
					...n.layout?.visibility === void 0 ? {} : { layout: { visibility: n.layout.visibility } },
					...n.minzoom === void 0 ? {} : { minzoom: n.minzoom },
					...n.maxzoom === void 0 ? {} : { maxzoom: n.maxzoom }
				} } : {}
			} : t.styleRefBodies?.[p.styleRef.styleId]) ?? { [p.id]: P(p.styleRef.styleId, e.layerType, t.appearance?.opacity?.[p.id]) };
			d.push({
				styleId: p.styleRef.styleId,
				body: r
			});
		}
	}
	let g = z(e);
	return {
		mapPackage: {
			mapPackageId: t.mapPackageId ?? "honua-studio-composition",
			format: "honua_map_package.v1",
			sourceBindings: l,
			mapSpec: n,
			...d.length > 0 ? { styleRefs: d } : {},
			...g ? { initialView: g } : {},
			...m.length > 0 ? { legend: m.map((t) => ({
				label: e.layers.find((e) => e.id === t)?.title ?? t,
				color: u(t)
			})) } : {}
		},
		renderedLayerIds: m,
		unresolved: f
	};
}
function z(e) {
	let { bbox: t, center: n, zoom: r, pitch: i, bearing: a } = e.view;
	if (t !== void 0 || n !== void 0 || r !== void 0 || i !== void 0 || a !== void 0) return {
		...t === void 0 ? {} : { bbox: t },
		...n === void 0 ? {} : { center: n },
		...r === void 0 ? {} : { zoom: r },
		...i === void 0 ? {} : { pitch: i },
		...a === void 0 ? {} : { bearing: a }
	};
}
function B(e) {
	return e.startsWith(l);
}
//#endregion
//#region src/map/composition-map-view.ts
var V = {
	center: [0, 20],
	zoom: 1
};
function H(e) {
	return typeof e == "object" && !!e;
}
var U = async (e) => {
	let [t, n] = await Promise.all([import("./maplibre-gl-DJ17FOvK.js"), import("./maplibre-gl-worker-BzMe-xzf.js")]), r = t.default ?? t, i = r.setWorkerUrl;
	typeof i == "function" && i(n.default);
	let a = r.Map;
	if (typeof a != "function") throw Error("maplibre-gl did not export a Map constructor.");
	let o = r;
	if (e.getAuth && (typeof o.addProtocol != "function" || typeof o.removeProtocol != "function")) throw Error("maplibre-gl did not export the source protocol registry.");
	let s = e.getAuth ? f(o, {
		getAuth: e.getAuth,
		sourceBaseUrl: e.sourceBaseUrl ?? "/api",
		legacySourceBaseUrl: e.legacySourceBaseUrl,
		documentUrl: document.baseURI,
		onError: e.onSourceError
	}) : void 0;
	try {
		let t = new a({
			container: e.container,
			style: e.style,
			center: [...e.center],
			zoom: e.zoom,
			attributionControl: {
				compact: !1,
				customAttribution: e.attributionText
			},
			...s ? { transformRequest: s.transformRequest } : {}
		});
		return s && t.on("remove", () => s.dispose()), t;
	} catch (e) {
		throw s?.dispose(), e;
	}
};
function W() {
	if (typeof document > "u" || typeof document.createElement != "function") return !1;
	try {
		let e = document.createElement("canvas");
		return typeof e.getContext == "function" ? e.getContext("webgl2") !== null || e.getContext("webgl") !== null : !1;
	} catch {
		return !1;
	}
}
var G = class {
	#e;
	#t;
	#n;
	#r = "idle";
	#i;
	#a;
	#o = Promise.resolve();
	#s = 0;
	#c;
	#l;
	#u;
	#d = !1;
	#f;
	#p = 0;
	#m = /* @__PURE__ */ new Map();
	#h;
	constructor(e) {
		this.#e = e, this.#l = e.appearance, this.#u = e.projection?.basemap;
	}
	setAppearance(e) {
		this.#l = e, this.#y();
	}
	setBasemap(e) {
		this.#u = e, this.#y();
	}
	camera() {
		let e = this.#t;
		if (e && typeof e.getZoom == "function" && typeof e.getCenter == "function") try {
			let t = e.getZoom(), n = e.getCenter(), r = Array.isArray(n) ? [Number(n[0]), Number(n[1])] : [Number(n.lng), Number(n.lat)];
			if (!Number.isFinite(t) || !Number.isFinite(r[0]) || !Number.isFinite(r[1])) return;
			let i = typeof e.getBearing == "function" ? e.getBearing() : 0;
			return {
				zoom: t,
				center: r,
				bearing: Number.isFinite(i) ? i : 0
			};
		} catch {
			return;
		}
	}
	nudgeCamera(e) {
		let t = this.#t, n = this.camera();
		if (!t || !n) return;
		let r = {};
		if (e.zoomBy !== void 0 && (r.zoom = Math.min(Math.max(n.zoom + e.zoomBy, 0), 24)), e.bearing !== void 0 && (r.bearing = e.bearing), Object.keys(r).length !== 0) try {
			t.easeTo({
				...r,
				duration: this.#e.viewTransitionMs ?? 300
			});
		} catch {}
	}
	container() {
		let e = this.#t;
		if (e && typeof e.getContainer == "function") try {
			return e.getContainer();
		} catch {
			return;
		}
		return this.#e.container;
	}
	get status() {
		return this.#r;
	}
	get statusDetail() {
		return this.#i;
	}
	get map() {
		return this.#t;
	}
	get projection() {
		return this.#a;
	}
	observeAuthoritativeStyles() {
		let e = this.#t?.getStyle?.();
		if (!e?.layers || !this.#t?.queryRenderedFeatures) return [];
		let t = [];
		for (let n of this.#e.controller.state.layers) {
			let r = this.#h?.[n.id], i = e.layers.find((e) => e.id === n.id);
			!r || "reason" in r || !i || !n.styleRef || t.push({
				layerId: n.id,
				sourceId: n.sourceId,
				styleId: n.styleRef.styleId,
				binding: r,
				renderedLayer: structuredClone(i),
				appearance: {
					filter: this.#l?.filters?.[n.id],
					opacity: this.#l?.opacity?.[n.id]
				},
				renderedFeatureCount: this.#t.queryRenderedFeatures(void 0, { layers: [n.id] }).length
			});
		}
		return t;
	}
	get mapPackage() {
		return this.#a?.mapPackage;
	}
	async start() {
		if (this.#d || this.#r !== "idle") return;
		this.#r = "starting";
		let e = this.#e.controller.state;
		if (await this.#g(), this.#d) return;
		let t = this.#_(), n = t.mapPackage.initialView, r = await this.#v(t.mapPackage);
		try {
			let e = this.#e.mapFactory ?? U;
			this.#t = await e({
				container: this.#e.container,
				style: r,
				center: n?.center ?? V.center,
				zoom: n?.zoom ?? V.zoom,
				attributionText: "Honua Studio",
				getAuth: this.#e.getAuth,
				sourceBaseUrl: this.#e.sourceAuthBaseUrl ?? this.#e.projection?.baseUrl,
				legacySourceBaseUrl: this.#e.projection?.baseUrl ?? "/api",
				onSourceError: (e) => {
					this.#i = `Map source could not load: ${e.message}`, this.#C();
				}
			});
		} catch (e) {
			this.#r = "unavailable", this.#i = e instanceof Error ? e.message : String(e), this.#C();
			return;
		}
		this.#d ? (this.#t.remove(), this.#t = void 0) : (this.#S(this.#t), this.#c = this.#e.controller.state.view, this.#x(this.#t, this.#e.controller.state.view, !0), this.#r = "ready", this.#n = this.#e.controller.subscribe(() => this.#y()), e !== this.#e.controller.state && this.#y(), this.#C());
	}
	resize() {
		try {
			this.#t?.resize();
		} catch {}
	}
	dispose() {
		this.#d = !0, this.#m.clear(), this.#n?.(), this.#n = void 0;
		try {
			this.#t?.remove();
		} catch {}
		this.#t = void 0, this.#r = "idle";
	}
	async refreshAuthoritativeStyles() {
		this.#p++, this.#m.clear(), this.#y(), await this.settled();
	}
	async #g() {
		let e = this.#e.styleProvider;
		if (!e) return;
		let t = {}, r = this.#e.controller.state, a = e.sessionIdentity, c = this.#p;
		a !== this.#f && this.#m.clear(), this.#f = a;
		let l = /* @__PURE__ */ new Map();
		for (let a of r.layers) {
			if (!a.styleRef) continue;
			let r = s(a.sourceId, this.#e.projection?.catalog);
			if (!r || o(r)) {
				try {
					if (i(a)) throw Error("Authoritative styles cannot certify a renderer source override.");
					if (!r) throw Error("An authoritative source descriptor is required for styles.");
					let o = r.id === a.sourceId ? r : {
						...r,
						id: a.sourceId
					}, s = n({
						styleId: a.styleRef.styleId,
						source: o
					}), c = this.#m.get(s), u = c && Date.now() - c.readAt < 3e4 ? c : {
						binding: await e.bind(a.styleRef.styleId, o),
						readAt: Date.now()
					};
					t[a.id] = u.binding, l.set(s, u);
				} catch (e) {
					t[a.id] = { reason: e instanceof Error ? e.message : String(e) };
				}
				if (this.#d) return;
			}
		}
		this.#m = a === e.sessionIdentity && c === this.#p && !this.#d ? l : /* @__PURE__ */ new Map(), this.#h = r === this.#e.controller.state && a === e.sessionIdentity ? t : {};
	}
	#_() {
		let e = R(this.#e.controller.state, {
			...this.#e.projection ?? {},
			...this.#h === void 0 ? {} : { authoritativeStyles: this.#h },
			...this.#u === void 0 ? {} : { basemap: this.#u },
			...this.#l === void 0 ? {} : { appearance: this.#l }
		});
		return this.#a = e, e;
	}
	attributions() {
		let e = this.#a?.mapPackage.mapSpec.sources ?? {}, t = [];
		for (let n of Object.values(e)) {
			let e = n.attribution;
			typeof e == "string" && e.length > 0 && !t.includes(e) && t.push(e);
		}
		return t;
	}
	async #v(e) {
		try {
			return await g(e, e.mapSpec, {});
		} catch (t) {
			return this.#i = `Style composition fell back to the base style: ${t instanceof Error ? t.message : String(t)}`, e.mapSpec;
		}
	}
	#y() {
		this.#s += 1;
		let e = this.#s;
		this.#o = this.#o.then(async () => {
			this.#d || e !== this.#s || await this.#b();
		});
	}
	settled() {
		return this.#o;
	}
	async #b() {
		let e = this.#t;
		if (!e || (await this.#g(), this.#d)) return;
		let t = this.#_(), n = await this.#v(t.mapPackage);
		if (this.#d) return;
		try {
			e.setStyle(n, { diff: !0 });
		} catch (e) {
			this.#i = `The map could not apply the latest composition: ${e instanceof Error ? e.message : String(e)}`, this.#C();
			return;
		}
		let r = this.#e.controller.state.view;
		J(r, this.#c) || (this.#c = r, this.#x(e, r, !1)), this.#C();
	}
	#x(e, t, n) {
		let r = n ? 0 : this.#e.viewTransitionMs ?? 700;
		try {
			if (t.bbox) {
				e.fitBounds(t.bbox, {
					duration: r,
					...t.pitch === void 0 ? {} : { pitch: t.pitch },
					...t.bearing === void 0 ? {} : { bearing: t.bearing }
				});
				return;
			}
			let n = {};
			if (t.center && (n.center = [...t.center]), t.zoom !== void 0 && (n.zoom = t.zoom), t.pitch !== void 0 && (n.pitch = t.pitch), t.bearing !== void 0 && (n.bearing = t.bearing), Object.keys(n).length === 0) return;
			r === 0 ? e.jumpTo(n) : e.easeTo({
				...n,
				duration: r
			});
		} catch (e) {
			this.#i = `The map could not move to the composed view: ${e instanceof Error ? e.message : String(e)}`;
		}
	}
	#S(e) {
		e.on("click", (t) => {
			if (!H(t)) return;
			let n = q(t.lngLat);
			n && this.#e.onMapClick?.(n);
			let r = this.#e.onSelection;
			if (!r) return;
			let i = this.#a?.renderedLayerIds ?? [];
			if (i.length === 0) return;
			let a = K(e.queryRenderedFeatures?.(t.point, { layers: [...i] }) ?? []);
			a.length > 0 && r(a);
		});
	}
	#C() {
		this.#e.onChange?.(this);
	}
};
function K(e) {
	let t = e[0];
	if (!H(t)) return [];
	let n = H(t.layer) && typeof t.layer.id == "string" ? t.layer.id : void 0;
	if (!n || B(n)) return [];
	let r = [], i = typeof t.source == "string" ? t.source : void 0, a = t.id;
	return i && (typeof a == "string" || typeof a == "number") && r.push({
		kind: "feature",
		sourceId: i,
		featureId: a
	}), r.push({
		kind: "layer",
		id: n
	}), r;
}
function q(e) {
	if (Array.isArray(e) && e.length >= 2) {
		let [t, n] = e;
		return Number.isFinite(t) && Number.isFinite(n) ? {
			lng: Number(t),
			lat: Number(n)
		} : void 0;
	}
	if (!H(e)) return;
	let { lng: t, lat: n } = e;
	return typeof t == "number" && typeof n == "number" && Number.isFinite(t) && Number.isFinite(n) ? {
		lng: t,
		lat: n
	} : void 0;
}
function J(e, t) {
	return t ? e.zoom === t.zoom && e.pitch === t.pitch && e.bearing === t.bearing && Y(e.center, t.center) && Y(e.bbox, t.bbox) : !1;
}
function Y(e, t) {
	return e === t ? !0 : !e || !t || e.length !== t.length ? !1 : e.every((e, n) => e === t[n]);
}
//#endregion
export { G as CompositionMapView, K as compositionTargetsFromFeatures, U as defaultCompositionMapFactory, W as isWebglAvailable };
