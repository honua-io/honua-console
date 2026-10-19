import { t as e } from "./widget-config-BnNjdjF6.js";
//#region node_modules/@honua/sdk-js/dist/src/studio/chart-spec.js
var t = "https://vega.github.io/schema/vega-lite/v6.json", n = {
	categories: "bar",
	histogram: "bar",
	"time-series": "line"
};
function r(e, r) {
	let a = e.chartKind ?? "categories", o = n[a], s;
	if (a === "categories") {
		let t = e.groupBy ?? e.field;
		if (!t) return;
		s = {
			x: {
				field: t,
				type: "nominal",
				title: e.title
			},
			y: {
				field: "count",
				type: "quantitative",
				aggregate: "count",
				title: "Count"
			}
		};
	} else if (a === "histogram") {
		if (!e.field) return;
		s = {
			x: {
				field: e.field,
				type: "quantitative",
				bin: !e.bins || { maxbins: e.bins },
				title: e.title ?? e.field
			},
			y: {
				field: "count",
				type: "quantitative",
				aggregate: "count",
				title: "Count"
			}
		};
	} else {
		let t = e.field ?? e.groupBy;
		if (!t) return;
		let n = e.metric ? {
			field: e.metric.field === "*" ? "count" : e.metric.field,
			type: "quantitative",
			aggregate: i(e.metric.fn),
			title: e.metric.alias ?? e.metric.field
		} : {
			field: "count",
			type: "quantitative",
			aggregate: "count",
			title: "Count"
		};
		s = {
			x: {
				field: t,
				type: "temporal",
				title: e.title ?? t
			},
			y: n
		};
	}
	return {
		$schema: t,
		...e.title ? { title: e.title } : {},
		mark: {
			type: o,
			tooltip: !0
		},
		encoding: s,
		width: "container",
		data: r ? { values: r } : { name: e.id }
	};
}
function i(e) {
	switch (e) {
		case "sum": return "sum";
		case "avg":
		case "mean": return "mean";
		case "min": return "min";
		case "max": return "max";
		default: return "count";
	}
}
//#endregion
//#region src/widgets/chart-spec.ts
function a(e) {
	return e.fn === "avg" ? "mean" : e.fn;
}
function o(e) {
	switch (e.fn) {
		case "sum": return "sum";
		case "avg": return "mean";
		case "min": return "min";
		case "max": return "max";
		case "median": return "median";
		default: return "count";
	}
}
function s(e) {
	let t = o(e), n = e.field === "*" ? "count" : e.field;
	return {
		field: n,
		type: "quantitative",
		aggregate: t,
		title: e.alias ?? (t === "count" ? "Count" : `${t}(${n})`)
	};
}
function c(e, t) {
	let n = t.field === void 0 ? t.temporal ? "time-series" : "categories" : "histogram";
	return {
		id: e.id,
		kind: "chart",
		chartKind: n,
		...e.title === void 0 ? {} : { title: e.title },
		...e.sourceId === void 0 ? {} : { sourceId: e.sourceId },
		...t.groupBy === void 0 ? {} : { groupBy: t.groupBy },
		...t.field === void 0 ? {} : { field: t.field },
		...t.temporal && t.groupBy !== void 0 ? { field: t.groupBy } : {},
		...t.bins === void 0 ? {} : { bins: t.bins },
		...t.measure.fn === "count" ? {} : { metric: {
			fn: a(t.measure),
			field: t.measure.field
		} }
	};
}
function l(t, n) {
	let i = e(t);
	if (!i.ok) return {
		ok: !1,
		reason: i.reason
	};
	let a = i.config, o = c(t, a), s = r(o, n);
	return s ? {
		ok: !0,
		spec: u(s, a),
		config: a
	} : {
		ok: !1,
		reason: `Chart "${t.id}" does not carry the field its "${o.chartKind}" encoding needs.`
	};
}
function u(e, t) {
	let n = e.encoding.x?.type === "nominal" || e.encoding.x?.type === "ordinal", r = { ...e.encoding };
	n && t.measure.fn !== "count" && (r.y = s(t.measure));
	let i = r.x;
	if (i && e.title !== void 0 && i.title === e.title) {
		let { title: e, ...t } = i;
		r.x = t;
	}
	if (t.chartType === "pie") {
		let t = r.x;
		return {
			...e,
			mark: {
				type: "arc",
				tooltip: !0
			},
			encoding: {
				...r.y === void 0 ? {} : { y: r.y },
				...t === void 0 ? {} : { color: t }
			}
		};
	}
	return t.chartType === "line" && typeof e.mark == "object" && e.mark.type !== "line" ? {
		...e,
		mark: {
			...e.mark,
			type: "line"
		},
		encoding: r
	} : {
		...e,
		encoding: r
	};
}
//#endregion
export { l as compositionChartSpec, c as compositionChartToGeneratedAppWidget };
