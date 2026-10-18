//#region src/widgets/chart-data.ts
var e = {
	points: [],
	categoryTitle: "",
	valueTitle: "",
	skippedRows: 0
}, t = 10, n = 24;
function r(e, t) {
	return e?.title ?? e?.field ?? t;
}
function i(e) {
	if (typeof e == "number") return Number.isFinite(e) ? e : void 0;
	if (typeof e == "string" && e.trim() !== "") {
		let t = Number(e);
		return Number.isFinite(t) ? t : void 0;
	}
}
function a(e) {
	if (typeof e == "number") return Number.isFinite(e) ? e : void 0;
	if (typeof e == "string") {
		let t = Date.parse(e);
		if (Number.isFinite(t)) return t;
		let n = Number(e);
		return Number.isFinite(n) ? n : void 0;
	}
}
function o(e, t, n) {
	switch (e) {
		case "sum": return t.reduce((e, t) => e + t, 0);
		case "mean": return t.length === 0 ? 0 : t.reduce((e, t) => e + t, 0) / t.length;
		case "min": return t.length === 0 ? 0 : Math.min(...t);
		case "max": return t.length === 0 ? 0 : Math.max(...t);
		case "median": {
			if (t.length === 0) return 0;
			let e = [...t].sort((e, t) => e - t), n = Math.floor(e.length / 2);
			return e.length % 2 == 0 ? ((e[n - 1] ?? 0) + (e[n] ?? 0)) / 2 : e[n] ?? 0;
		}
		default: return n;
	}
}
function s(e) {
	let t = e.encoding.x;
	return t && t.type !== "quantitative" || t?.bin ? t : e.encoding.color ?? t;
}
function c(e, t) {
	let n = (e) => Number.isInteger(e) ? String(e) : e.toFixed(2);
	return `${n(e)}–${n(t)}`;
}
function l(e) {
	let t = new Date(e);
	return Number.isNaN(t.getTime()) ? String(e) : t.toISOString().slice(0, 10) ?? String(e);
}
function u(t, n) {
	let i = n ?? t.data?.values ?? [], a = s(t), o = t.encoding.y;
	if (!a?.field) return {
		...e,
		reason: "The chart spec has no category encoding to group rows by."
	};
	let c = r(a, a.field), l = r(o, "Count");
	return i.length === 0 ? {
		...e,
		categoryTitle: c,
		valueTitle: l
	} : a.bin ? d(i, a, o, c, l) : f(i, a, o, c, l);
}
function d(e, n, r, a, s) {
	let l = n.field, u = (typeof n.bin == "object" ? n.bin.maxbins : void 0) ?? t, d = [], f = 0;
	for (let t of e) {
		let e = i(t[l]);
		e === void 0 ? f += 1 : d.push({
			bucketValue: e,
			measured: r?.field ? i(t[r.field]) : void 0
		});
	}
	if (d.length === 0) return {
		points: [],
		categoryTitle: a,
		valueTitle: s,
		skippedRows: f
	};
	let p = Math.min(...d.map((e) => e.bucketValue)), m = Math.max(...d.map((e) => e.bucketValue)), h = m === p ? 1 : (m - p) / u, g = /* @__PURE__ */ new Map();
	for (let e of d) {
		let t = m === p ? 0 : Math.min(Math.floor((e.bucketValue - p) / h), u - 1), n = g.get(t) ?? {
			values: [],
			count: 0
		};
		n.count += 1, e.measured !== void 0 && n.values.push(e.measured), g.set(t, n);
	}
	return {
		points: [...g.entries()].sort((e, t) => e[0] - t[0]).map(([e, t]) => ({
			label: c(p + e * h, p + (e + 1) * h),
			value: o(r?.aggregate, t.values, t.count),
			order: p + e * h
		})),
		categoryTitle: a,
		valueTitle: s,
		skippedRows: f
	};
}
function f(e, t, n, r, s) {
	let c = t.field, u = t.type === "temporal", d = /* @__PURE__ */ new Map(), f = 0;
	for (let t of e) {
		let e = t[c];
		if (e == null || e === "") {
			f += 1;
			continue;
		}
		let r = u ? a(e) : void 0;
		if (u && r === void 0) {
			f += 1;
			continue;
		}
		let o = u && r !== void 0 ? l(r) : String(e), s = d.get(o) ?? {
			values: [],
			count: 0,
			order: r ?? o
		};
		if (s.count += 1, n?.field) {
			let e = i(t[n.field]);
			e !== void 0 && s.values.push(e);
		}
		d.set(o, s);
	}
	let m = [...d.entries()].map(([e, t]) => ({
		label: e,
		value: o(n?.aggregate, t.values, t.count),
		order: t.order
	}));
	return m.sort((e, t) => u ? Number(e.order) - Number(t.order) : t.value - e.value || String(e.label).localeCompare(String(t.label))), {
		points: p(m, u),
		categoryTitle: r,
		valueTitle: s,
		skippedRows: f
	};
}
function p(e, t) {
	if (t || e.length <= n) return e;
	let r = e.slice(0, 23), i = e.slice(23), a = i.reduce((e, t) => e + t.value, 0);
	return [...r, {
		label: `Other (${i.length})`,
		value: a,
		order: "￿"
	}];
}
//#endregion
export { u as chartSeriesFromSpec };
