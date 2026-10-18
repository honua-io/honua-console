//#region src/widgets/widget-config.ts
function e(e) {
	if (typeof e != "object" || !e) return !1;
	let t = Object.getPrototypeOf(e);
	return t === Object.prototype || t === null;
}
function t(t) {
	return e(t.config) ? t.config : {};
}
function n(e, ...t) {
	for (let n of t) {
		let t = e[n];
		if (typeof t == "string" && t.length > 0) return t;
	}
}
function r(e, ...t) {
	for (let n of t) {
		let t = e[n];
		if (Array.isArray(t)) {
			let e = t.filter((e) => typeof e == "string" && e.length > 0);
			if (e.length > 0) return e;
		}
	}
	return [];
}
function i(e, ...t) {
	for (let n of t) {
		let t = e[n];
		if (typeof t == "number" && Number.isFinite(t) && t > 0) return Math.floor(t);
	}
}
function a(e, t) {
	let n = e[t];
	return typeof n == "boolean" ? n : void 0;
}
function o(e) {
	let t = (e ?? "").toLowerCase().replace(/[^a-z]/g, "");
	return t === "pie" || t === "donut" || t === "doughnut" || t === "arc" ? "pie" : t === "line" || t === "area" || t === "timeseries" || t === "trend" ? "line" : "bar";
}
function s(e) {
	let t = (e ?? "").toLowerCase().replace(/[^a-z]/g, "");
	return t === "sum" || t === "total" ? "sum" : t === "avg" || t === "average" || t === "mean" ? "avg" : t === "min" ? "min" : t === "max" ? "max" : t === "median" ? "median" : "count";
}
function c(t) {
	let r = e(t.measure) ? t.measure : e(t.metric) ? t.metric : void 0, i = s(r ? n(r, "fn", "function", "aggregate", "metric") : n(t, "measureFn", "aggregate")), a = (r ? n(r, "field", "on", "column") : n(t, "measureField", "measureOn")) ?? "*", o = r ? n(r, "alias", "title", "label") : n(t, "measureAlias");
	return {
		fn: a === "*" && i !== "count" ? "count" : i,
		field: a,
		...o === void 0 ? {} : { alias: o }
	};
}
function l(e, t) {
	return e ? /date|time|year|month|day|timestamp|ts$|_at$/i.test(e) ? !0 : t === "line" : !1;
}
function u(e) {
	let r = t(e), a = o(n(r, "chartType", "chartKind", "type", "mark")), s = n(r, "groupBy", "categoryField", "category", "by", "x", "field"), u = n(r, "binField", "histogramField"), d = i(r, "bins", "maxbins"), f = c(r);
	if (u !== void 0 || d !== void 0 && /hist/i.test(n(r, "chartType", "chartKind", "type") ?? "")) {
		let t = u ?? s;
		return t ? {
			ok: !0,
			config: {
				chartType: "bar",
				field: t,
				...d === void 0 ? {} : { bins: d },
				measure: f,
				temporal: !1
			}
		} : {
			ok: !1,
			reason: `Chart "${e.id}" asks for a histogram but names no field to bin.`
		};
	}
	return s ? {
		ok: !0,
		config: {
			chartType: a,
			groupBy: s,
			...d === void 0 ? {} : { bins: d },
			measure: f,
			temporal: l(s, a)
		}
	} : {
		ok: !1,
		reason: `Chart "${e.id}" has no "groupBy" (nor a field to bin), so there is nothing to plot against.`
	};
}
function d(e) {
	let a = t(e), o = Math.min(i(a, "pageSize", "limit", "rows") ?? 25, 200), s = n(a, "primaryKey", "idField", "objectIdField");
	return {
		ok: !0,
		config: {
			fields: r(a, "fields", "columns", "tableFields"),
			pageSize: o,
			...s === void 0 ? {} : { primaryKey: s }
		}
	};
}
function f(e) {
	let n = t(e);
	return {
		ok: !0,
		config: {
			layerIds: r(n, "layerIds", "layers"),
			showUnrenderable: a(n, "showUnrenderable") ?? !0
		}
	};
}
function p(e) {
	let n = t(e);
	return {
		ok: !0,
		config: {
			layerIds: r(n, "layerIds", "layers"),
			includeHidden: a(n, "includeHidden") ?? !1
		}
	};
}
function m(e) {
	let i = t(e), a = r(i, "layerIds", "layers"), o = n(i, "left", "leftLayerId", "a") ?? a[0], s = n(i, "right", "rightLayerId", "b") ?? a[1];
	if (!o || !s) return {
		ok: !1,
		reason: `Compare "${e.id}" needs two layers — set config.left and config.right (or config.layerIds).`
	};
	let c = n(i, "leftLabel"), l = n(i, "rightLabel");
	return {
		ok: !0,
		config: {
			left: o,
			right: s,
			...c === void 0 ? {} : { leftLabel: c },
			...l === void 0 ? {} : { rightLabel: l }
		}
	};
}
function h(r) {
	let i = t(r), a = Array.isArray(i.steps) ? i.steps : [], o = [];
	for (let t of a) {
		if (!e(t)) continue;
		let r = n(t, "layerId", "layer", "id");
		r && o.push({
			label: n(t, "label", "title", "time") ?? r,
			layerId: r
		});
	}
	let s = n(i, "field", "timeField", "dateField");
	return o.length === 0 ? {
		ok: !1,
		reason: s ? `Time "${r.id}" filters on "${s}", but the composition vocabulary has no filter command yet — author it as config.steps: [{ label, layerId }] to drive layer visibility instead.` : `Time "${r.id}" has no config.steps: [{ label, layerId }] to step through.`
	} : {
		ok: !0,
		config: {
			steps: o,
			...s === void 0 ? {} : { field: s }
		}
	};
}
//#endregion
export { h as a, d as i, m as n, f as o, p as r, u as t };
