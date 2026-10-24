//#region src/composition/source-reference.ts
function e(e, t) {
	if (!t.trim() || t.trim() !== t || [...t].some((e) => e.charCodeAt(0) < 32)) return;
	let n = e.layers.find((e) => e.id === t);
	if (!n) return t;
	if (!(n.sourceId !== t && e.layers.some((e) => e.sourceId === t)) && !e.layers.some((e) => e.id === n.sourceId && e.sourceId !== n.sourceId)) return n.sourceId;
}
//#endregion
//#region src/controls/control-config.ts
var t = {
	navigation: !1,
	scale: !1,
	fullscreen: !1,
	geolocate: !1,
	search: !1,
	measure: !1,
	attribution: !1,
	basemapSwitcher: !1,
	bookmarks: !1,
	timeSlider: !0,
	filterSelect: !0,
	filterSlider: !0,
	filterDateRange: !0,
	opacity: !0
}, n = {
	navigation: "Navigation",
	scale: "Scale",
	fullscreen: "Fullscreen",
	geolocate: "My location",
	search: "Search",
	measure: "Measure",
	timeSlider: "Time",
	filterSelect: "Filter",
	filterSlider: "Range",
	filterDateRange: "Date range",
	bookmarks: "Bookmarks",
	opacity: "Opacity",
	attribution: "Attribution",
	basemapSwitcher: "Basemap"
};
function r(e) {
	if (typeof e != "object" || !e) return !1;
	let t = Object.getPrototypeOf(e);
	return t === Object.prototype || t === null;
}
function i(e) {
	return r(e.config) ? e.config : {};
}
function a(e, ...t) {
	for (let n of t) {
		let t = e[n];
		if (typeof t == "string" && t.length > 0) return t;
	}
}
function o(e, ...t) {
	for (let n of t) {
		let t = e[n];
		if (typeof t == "number" && Number.isFinite(t)) return t;
		if (typeof t == "string" && t.trim() !== "" && Number.isFinite(Number(t))) return Number(t);
	}
}
function s(e, ...t) {
	for (let n of t) {
		let t = e[n];
		if (typeof t == "boolean") return t;
	}
}
function c(e, ...t) {
	for (let n of t) {
		let t = e[n];
		if (Array.isArray(t)) {
			let e = t.filter((e) => typeof e == "string" && e.length > 0);
			if (e.length > 0) return e;
		}
	}
	return [];
}
function l(e) {
	let t = (a(e, "unit", "units", "system") ?? "").toLowerCase();
	return t.startsWith("imp") || t === "us" || t === "english" || t === "mi" || t === "ft" ? "imperial" : "metric";
}
function u(e) {
	if (typeof e == "number" && Number.isFinite(e)) {
		let t = new Date(e);
		return Number.isNaN(t.getTime()) ? void 0 : t.toISOString().slice(0, 10);
	}
	if (typeof e != "string" || e.length === 0) return;
	let t = /^(\d{4}-\d{2}-\d{2})/.exec(e);
	if (t?.[1]) return t[1];
	let n = new Date(e);
	return Number.isNaN(n.getTime()) ? void 0 : n.toISOString().slice(0, 10);
}
var d = 120, f = 13, p = [{
	id: "honua-offline-light",
	label: "Light",
	kind: "vector",
	theme: "light"
}, {
	id: "honua-offline-dark",
	label: "Dark",
	kind: "vector",
	theme: "dark"
}];
function m(e) {
	let t = i(e);
	switch (e.kind) {
		case "navigation": return h({
			kind: "navigation",
			config: {
				showZoom: s(t, "showZoom", "zoom") ?? !0,
				showCompass: s(t, "showCompass", "compass") ?? !0
			}
		});
		case "scale": return h({
			kind: "scale",
			config: {
				unit: l(t),
				maxWidthPx: o(t, "maxWidth", "maxWidthPx", "width") ?? d
			}
		});
		case "fullscreen": return h({
			kind: "fullscreen",
			config: { targetLabel: a(t, "targetLabel", "label") ?? "map" }
		});
		case "geolocate": return h({
			kind: "geolocate",
			config: {
				zoom: o(t, "zoom") ?? f,
				highAccuracy: s(t, "highAccuracy", "enableHighAccuracy") ?? !0
			}
		});
		case "attribution": return h({
			kind: "attribution",
			config: { compact: s(t, "compact") ?? !1 }
		});
		case "measure": return h({
			kind: "measure",
			config: {
				mode: (a(t, "mode", "measure") ?? "").toLowerCase().startsWith("area") ? "area" : "distance",
				unit: l(t)
			}
		});
		case "basemapSwitcher": {
			let e = b(t);
			return h({
				kind: "basemapSwitcher",
				config: { bases: e.length > 0 ? e : p }
			});
		}
		case "bookmarks": {
			let n = x(t, e.id);
			return n.length === 0 ? g(`Bookmarks "${e.id}" has no config.bookmarks: [{ label, bbox }] (or { label, center, zoom }) to travel to.`) : h({
				kind: "bookmarks",
				config: { bookmarks: n }
			});
		}
		case "opacity": {
			let n = c(t, "layerIds", "layers"), r = n.length > 0 ? n : e.sourceId ? [e.sourceId] : [];
			return r.length === 0 ? g(`Opacity "${e.id}" names no layer — set the control's sourceId, or config.layerIds.`) : h({
				kind: "opacity",
				config: {
					layerIds: r,
					value: _(o(t, "value", "opacity", "default") ?? 1)
				}
			});
		}
		case "filterSelect": {
			let n = a(t, "field", "attribute", "property");
			return n ? h({
				kind: "filterSelect",
				config: {
					field: n,
					options: y(t),
					multiple: s(t, "multiple", "multi") ?? !1,
					includeAllOption: s(t, "includeAllOption", "includeAll") ?? !0
				}
			}) : g(`Filter "${e.id}" has no config.field, so there is nothing for it to filter on.`);
		}
		case "filterSlider": {
			let n = a(t, "field", "attribute", "property");
			if (!n) return g(`Range filter "${e.id}" has no config.field, so there is nothing for it to filter on.`);
			let r = o(t, "min", "minimum", "from"), i = o(t, "max", "maximum", "to");
			if (r === void 0 || i === void 0 || !(i > r)) return g(`Range filter "${e.id}" needs a numeric config.min and config.max with max greater than min — a slider with no domain cannot be drawn.`);
			let s = (a(t, "operator", "op", "comparison") ?? "").trim(), c = s === "<=" || s === "=" ? s : ">=";
			return h({
				kind: "filterSlider",
				config: {
					field: n,
					min: r,
					max: i,
					step: o(t, "step") ?? v(r, i),
					operator: c
				}
			});
		}
		case "filterDateRange": {
			let n = a(t, "field", "dateField", "timeField", "attribute");
			if (!n) return g(`Date filter "${e.id}" has no config.field naming the date attribute to filter on.`);
			let r = u(t.from ?? t.start ?? t.min), i = u(t.to ?? t.end ?? t.max);
			return h({
				kind: "filterDateRange",
				config: {
					field: n,
					...r === void 0 ? {} : { from: r },
					...i === void 0 ? {} : { to: i }
				}
			});
		}
		case "timeSlider": {
			let n = a(t, "field", "timeField", "dateField");
			if (!n) return g(`Time slider "${e.id}" has no config.field naming the time attribute it ranges over. (A "time as a layer stack" stepper is the \`time\` WIDGET — see ../widgets/widget-config.ts.)`);
			let r = u(t.from ?? t.start ?? t.min), i = u(t.to ?? t.end ?? t.max);
			return !r || !i ? g(`Time slider "${e.id}" needs config.from and config.to (ISO dates) — the composition carries no field schema to infer a time extent from.`) : h({
				kind: "timeSlider",
				config: {
					field: n,
					from: r,
					to: i,
					stepDays: Math.max(Math.trunc(o(t, "stepDays", "step") ?? 1), 1)
				}
			});
		}
		case "search": return g(`Search "${e.id}" cannot be rendered: no search provider is declared. ADR-0031's control config carries no provider vocabulary (geocoder endpoint vs. feature search over sourceId), and Studio will not invent one privately or call an off-origin geocoder. Upstream gap — geospatial-mcp needs a provider field before this kind can render.`);
	}
}
function h(e) {
	return {
		ok: !0,
		config: e
	};
}
function g(e) {
	return {
		ok: !1,
		reason: e
	};
}
function _(e) {
	return Math.min(Math.max(e, 0), 1);
}
function v(e, t) {
	let n = t - e;
	return n <= 1 ? n / 100 : n <= 10 ? .1 : n <= 1e3 ? 1 : 10 ** (Math.floor(Math.log10(n)) - 2);
}
function y(e) {
	let t = e.options ?? e.values ?? e.choices;
	if (!Array.isArray(t)) return [];
	let n = [];
	for (let e of t) {
		if (typeof e == "string" || typeof e == "number") {
			n.push({
				value: String(e),
				label: String(e)
			});
			continue;
		}
		if (!r(e)) continue;
		let t = e.value ?? e.id ?? e.key;
		typeof t != "string" && typeof t != "number" || n.push({
			value: String(t),
			label: a(e, "label", "title", "name") ?? String(t)
		});
	}
	return n;
}
function b(e) {
	let t = e.bases ?? e.basemaps ?? e.options;
	if (!Array.isArray(t)) return [];
	let n = [];
	for (let e of t) {
		if (!r(e)) continue;
		let t = a(e, "id", "value", "key");
		if (!t) continue;
		let i = (a(e, "theme", "mode") ?? "").toLowerCase(), o = (a(e, "kind", "type") ?? "").toLowerCase();
		n.push({
			id: t,
			label: a(e, "label", "title", "name") ?? t,
			kind: o === "raster" ? "raster" : o.startsWith("raster-dem") ? "raster-dem-composite" : "vector",
			...i === "dark" || i === "light" ? { theme: i } : {}
		});
	}
	return n;
}
function x(e, t) {
	let n = e.bookmarks ?? e.views ?? e.places;
	if (!Array.isArray(n)) return [];
	let i = [];
	for (let [e, s] of n.entries()) {
		if (!r(s)) continue;
		let n = r(s.view) ? s.view : s, c = S(n.bbox ?? n.extent, 4), l = S(n.center ?? n.coordinate, 2), u = o(n, "zoom");
		if (!c && !l) continue;
		let d = a(s, "label", "title", "name") ?? `View ${e + 1}`;
		i.push({
			id: a(s, "id") ?? `${t}-${e}`,
			label: d,
			view: {
				...c ? { bbox: c } : {},
				...l ? { center: l } : {},
				...u === void 0 ? {} : { zoom: u },
				...o(n, "pitch") === void 0 ? {} : { pitch: o(n, "pitch") },
				...o(n, "bearing") === void 0 ? {} : { bearing: o(n, "bearing") }
			}
		});
	}
	return i;
}
function S(e, t) {
	if (!Array.isArray(e) || e.length !== t) return;
	let n = e.map((e) => typeof e == "number" ? e : Number(e));
	return n.every((e) => Number.isFinite(e)) ? n : void 0;
}
function C(e, r) {
	let i = m(r);
	if (!i.ok) return {
		state: "unsupported",
		reason: i.reason
	};
	if (r.sourceId !== void 0 && !w(e, r.sourceId)) return {
		state: "unsupported",
		reason: `${n[r.kind]} "${r.id}" reads from "${r.sourceId}", which is not a layer in this composition (nor a source any layer binds).`
	};
	if (i.config.kind === "opacity") {
		let t = i.config.config.layerIds.filter((t) => !e.layers.some((e) => e.id === t));
		if (t.length === i.config.config.layerIds.length) return {
			state: "unsupported",
			reason: `Opacity "${r.id}" drives ${t.map((e) => `"${e}"`).join(", ")}, which the composition does not hold.`
		};
	}
	if (!t[r.kind]) return {
		state: "rendered",
		config: i.config
	};
	let a = e.interactions.some((e) => e.on.ref === `control:${r.id}` && e.disabled !== !0);
	return {
		state: "rendered",
		config: i.config,
		...a ? {} : { note: "Emits change — no interaction binds it yet." }
	};
}
function w(t, n) {
	return !!e(t, n) && t.layers.some((e) => e.id === n || e.sourceId === n);
}
function T(t, n) {
	return !n || !e(t, n) ? [] : t.layers.filter((e) => e.id === n || e.sourceId === n).map((e) => e.id);
}
//#endregion
export { m as a, C as i, n, e as o, T as r, t };
