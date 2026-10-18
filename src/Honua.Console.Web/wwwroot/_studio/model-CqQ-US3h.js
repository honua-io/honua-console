var e = [
	"table",
	"chart",
	"compare",
	"time",
	"legend",
	"toc"
], t = [
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
], n = [
	"featureSelect",
	"featureHover",
	"selection",
	"change",
	"viewportChange"
], r = [
	"setFilter",
	"setViewport",
	"selectFeature",
	"runWidgetQuery",
	"setVisibility"
], i = [
	"region",
	"point",
	"note"
], a = [
	"layer",
	"feature",
	"region",
	"component",
	"control"
];
function o() {
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
function s(e, t) {
	let n = `control:${t}`;
	return e.interactions.filter((e) => e.on.ref === n || e.do.ref === n);
}
function c(e) {
	return e.kind === "feature" ? `feature:${e.sourceId}:${String(e.featureId)}` : `${e.kind}:${e.id}`;
}
function l(e, t) {
	return c(e) === c(t);
}
function u(e, t = {}) {
	let n = d(e);
	return t.pretty ? JSON.stringify(n, null, 2) : JSON.stringify(n);
}
function d(e) {
	if (Array.isArray(e)) return e.map(d);
	if (f(e)) {
		let t = {};
		for (let n of Object.keys(e).sort()) t[n] = d(e[n]);
		return t;
	}
	return e;
}
function f(e) {
	if (typeof e != "object" || !e) return !1;
	let t = Object.getPrototypeOf(e);
	return t === Object.prototype || t === null;
}
//#endregion
export { a, c, s as d, r as i, l, t as n, e as o, n as r, u as s, i as t, o as u };
