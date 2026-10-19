import { n as e } from "./palette-BSfbwTvA.js";
//#region src/widgets/chart-render.ts
var t = 320, n = 180, r = 40, i = 8, a = 12, o = 30, s = t - r - i, c = n - a - o, l = 9;
function u(e) {
	return e.replace(/[&<>"']/g, (e) => `&#${e.charCodeAt(0)};`);
}
function d(e) {
	return Math.round(e * 100) / 100;
}
function f(e) {
	if (!Number.isFinite(e)) return "—";
	let t = Math.abs(e);
	return t >= 1e6 ? `${d(e / 1e6)}M` : t >= 1e3 ? `${d(e / 1e3)}k` : String(Number.isInteger(e) ? e : d(e));
}
function p(e) {
	return e.length > l ? `${e.slice(0, l - 1)}…` : e;
}
function m(e) {
	return typeof e.mark == "string" ? e.mark : e.mark.type;
}
function h(e, t) {
	if (e.points.length === 0) return "No rows matched this chart's grouping.";
	let n = e.points.slice(0, 3).map((e) => `${e.label} ${f(e.value)}`).join(", "), r = e.skippedRows > 0 ? `; ${e.skippedRows} row(s) had no value` : "";
	return `${t} chart of ${e.valueTitle} by ${e.categoryTitle}: ${e.points.length} categories, top ${n}${r}.`;
}
function g(e, r, i) {
	return `<svg class="widget-chart-svg" viewBox="0 0 ${t} ${n}" role="img" aria-label="${u(i)}" xmlns="http://www.w3.org/2000/svg"><title>${u(r)}</title><desc>${u(i)}</desc>${e}</svg>`;
}
function _(e) {
	if (e <= 0) return 1;
	let t = 10 ** Math.floor(Math.log10(e));
	for (let n of [
		1,
		2,
		2.5,
		5,
		10
	]) {
		let r = n * t;
		if (r >= e) return r;
	}
	return e;
}
function v(e) {
	return [
		0,
		e / 2,
		e
	].map((n) => {
		let a = 150 - n / e * c;
		return `<line class="widget-chart-grid" x1="${r}" y1="${d(a)}" x2="${t - i}" y2="${d(a)}" /><text class="widget-chart-tick" x="${r - 4}" y="${d(a + 3)}" text-anchor="end">${u(f(n))}</text>`;
	}).join("");
}
function y(e, t) {
	let n = Math.ceil(e.length / 8);
	return e.map((e, r) => r % n === 0 ? `<text class="widget-chart-label" x="${d(t(r))}" y="162" text-anchor="middle">${u(p(e.label))}</text>` : "").join("");
}
function b(e, t) {
	let n = _(Math.max(...e.points.map((e) => e.value), 0)), i = s / e.points.length, a = Math.max(i * .7, 1), o = (e) => r + i * (e + .5), l = e.points.map((e, r) => {
		let i = Math.max(e.value / n * c, +(e.value > 0)), s = o(r) - a / 2, l = 150 - i;
		return `<rect class="widget-chart-bar" x="${d(s)}" y="${d(l)}" width="${d(a)}" height="${d(i)}" fill="${t}"><title>${u(`${e.label}: ${f(e.value)}`)}</title></rect>`;
	}).join("");
	return `${v(n)}${l}${y(e.points, o)}`;
}
function x(e, t) {
	let n = _(Math.max(...e.points.map((e) => e.value), 0)), i = e.points.length > 1 ? s / (e.points.length - 1) : 0, a = (t) => e.points.length > 1 ? r + i * t : 176, o = e.points.map((e, t) => ({
		x: a(t),
		y: 150 - e.value / n * c,
		point: e
	})), l = o.map(({ x: e, y: t }) => `${d(e)},${d(t)}`).join(" "), p = o.map(({ x: e, y: n, point: r }) => `<circle class="widget-chart-vertex" cx="${d(e)}" cy="${d(n)}" r="2.5" fill="${t}"><title>${u(`${r.label}: ${f(r.value)}`)}</title></circle>`).join("");
	return `${v(n)}<polyline class="widget-chart-line" points="${l}" fill="none" stroke="${t}" stroke-width="2" stroke-linejoin="round" stroke-linecap="round" />${p}${y(e.points, a)}`;
}
function S(e, t, n, r, i) {
	if (i - r >= Math.PI * 2 - 1e-9) return `M ${d(e)} ${d(t - n)} A ${n} ${n} 0 1 1 ${d(e)} ${d(t + n)} A ${n} ${n} 0 1 1 ${d(e)} ${d(t - n)} Z`;
	let a = (r) => `${d(e + n * Math.sin(r))} ${d(t - n * Math.cos(r))}`, o = +(i - r > Math.PI);
	return `M ${d(e)} ${d(t)} L ${a(r)} A ${n} ${n} 0 ${o} 1 ${a(i)} Z`;
}
function C(t) {
	let n = t.points.reduce((e, t) => e + Math.max(t.value, 0), 0);
	if (n <= 0) return "";
	let r = c / 2 - 2, i = 0;
	return `${t.points.map((t) => {
		let a = Math.max(t.value, 0) / n * Math.PI * 2, o = S(109, 81, r, i, i + a);
		i += a;
		let s = Math.round(Math.max(t.value, 0) / n * 100);
		return `<path class="widget-chart-slice" d="${o}" fill="${e(t.label)}"><title>${u(`${t.label}: ${f(t.value)} (${s}%)`)}</title></path>`;
	}).join("")}${t.points.slice(0, 8).map((t, n) => {
		let r = 20 + n * 14;
		return `<rect x="188" y="${r - 6}" width="8" height="8" rx="2" fill="${e(t.label)}" /><text class="widget-chart-label" x="200" y="${r + 1}">${u(p(t.label))} ${u(f(t.value))}</text>`;
	}).join("")}`;
}
function w(t, n) {
	let r = m(t), i = t.title ?? `${n.valueTitle} by ${n.categoryTitle}`, a = h(n, r);
	if (n.points.length === 0) return {
		svg: g("", i, a),
		summary: a
	};
	let o = e(t.title ?? n.categoryTitle);
	return {
		svg: g(r === "arc" ? C(n) : r === "line" ? x(n, o) : b(n, o), i, a),
		summary: a
	};
}
//#endregion
export { f as formatValue, w as renderChartSvg, m as specMark };
