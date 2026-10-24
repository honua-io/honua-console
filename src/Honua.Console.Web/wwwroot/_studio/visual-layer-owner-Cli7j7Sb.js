import { r as e } from "./constants-B8tqYbYF.js";
var t = [
	"table",
	"chart",
	"compare",
	"time",
	"legend",
	"toc"
], n = [
	"navigation",
	"scale",
	"fullscreen",
	"geolocate",
	"search",
	"measure",
	"timeSlider",
	"filterSelect",
	"filterSlider",
	"filterDateRange",
	"bookmarks",
	"opacity",
	"attribution",
	"basemapSwitcher"
], r = [
	"featureSelect",
	"featureHover",
	"selection",
	"change",
	"viewportChange"
], i = [
	"setFilter",
	"setViewport",
	"selectFeature",
	"runWidgetQuery",
	"setVisibility"
], a = [
	"region",
	"point",
	"note"
], o = [
	"layer",
	"feature",
	"region",
	"component",
	"control"
];
function s() {
	return {
		version: 1,
		layers: [],
		view: {},
		widgets: [],
		controls: [],
		interactions: [],
		annotations: [],
		pins: []
	};
}
function c(e, t) {
	let n = `control:${t}`;
	return e.interactions.filter((e) => e.on.ref === n || e.do.ref === n);
}
function l(e) {
	return e.kind === "feature" ? `feature:${e.sourceId}:${String(e.featureId)}` : `${e.kind}:${e.id}`;
}
function u(e, t) {
	return l(e) === l(t);
}
function d(e, t = {}) {
	let n = f(e);
	return t.pretty ? JSON.stringify(n, null, 2) : JSON.stringify(n);
}
function f(e) {
	if (Array.isArray(e)) return e.map(f);
	if (p(e)) {
		let t = {};
		for (let n of Object.keys(e).sort()) t[n] = f(e[n]);
		return t;
	}
	return e;
}
function p(e) {
	if (typeof e != "object" || !e) return !1;
	let t = Object.getPrototypeOf(e);
	return t === Object.prototype || t === null;
}
//#endregion
//#region src/map/visual-layer-owner.ts
function m(t, n) {
	let r = t.metadata, i = r && typeof r == "object" ? r["honua:compositionLayerId"] : void 0, a = t.id.endsWith("__outline") ? t.id.slice(0, -e.length) : void 0;
	return (n.has(t.id) ? t.id : a && n.has(a) ? a : void 0) ?? (typeof i == "string" && n.has(i) ? i : void 0);
}
//#endregion
export { i as a, d as c, s as d, c as f, r as i, l, a as n, o, n as r, t as s, m as t, u };
