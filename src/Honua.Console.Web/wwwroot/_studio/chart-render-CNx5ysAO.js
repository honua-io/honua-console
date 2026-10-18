import { n as e } from "./palette-BSfbwTvA.js";
//#region src/widgets/chart-render.ts
var t = 320, n = 180, r = 40, i = 272, a = 138, o = 9;
function s(e) {
	return e.replace(/[&<>"']/g, (e) => `&#${e.charCodeAt(0)};`);
}
function c(e) {
	return Math.round(e * 100) / 100;
}
function l(e) {
	if (!Number.isFinite(e)) return "—";
	let t = Math.abs(e);
	return t >= 1e6 ? `${c(e / 1e6)}M` : t >= 1e3 ? `${c(e / 1e3)}k` : String(Number.isInteger(e) ? e : c(e));
}
function u(e) {
	return e.length > o ? `${e.slice(0, 8)}…` : e;
}
function d(e) {
	return typeof e.mark == "string" ? e.mark : e.mark.type;
}
function f(e, t) {
	if (e.points.length === 0) return "No rows matched this chart's grouping.";
	let n = e.points.slice(0, 3).map((e) => `${e.label} ${l(e.value)}`).join(", "), r = e.skippedRows > 0 ? `; ${e.skippedRows} row(s) had no value` : "";
	return `${t} chart of ${e.valueTitle} by ${e.categoryTitle}: ${e.points.length} categories, top ${n}${r}.`;
}
function p(e, r, i) {
	return `<svg class="widget-chart-svg" viewBox="0 0 ${t} ${n}" role="img" aria-label="${s(i)}" xmlns="http://www.w3.org/2000/svg"><title>${s(r)}</title><desc>${s(i)}</desc>${e}</svg>`;
}
function m(e) {
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
function h(e) {
	return [
		0,
		e / 2,
		e
	].map((t) => {
		let n = 150 - t / e * a;
		return `<line class="widget-chart-grid" x1="${r}" y1="${c(n)}" x2="312" y2="${c(n)}" /><text class="widget-chart-tick" x="36" y="${c(n + 3)}" text-anchor="end">${s(l(t))}</text>`;
	}).join("");
}
function g(e, t) {
	let n = Math.ceil(e.length / 8);
	return e.map((e, r) => r % n === 0 ? `<text class="widget-chart-label" x="${c(t(r))}" y="162" text-anchor="middle">${s(u(e.label))}</text>` : "").join("");
}
function _(e, t) {
	let n = m(Math.max(...e.points.map((e) => e.value), 0)), o = i / e.points.length, u = Math.max(o * .7, 1), d = (e) => r + o * (e + .5), f = e.points.map((e, r) => {
		let i = Math.max(e.value / n * a, +(e.value > 0)), o = d(r) - u / 2, f = 150 - i;
		return `<rect class="widget-chart-bar" x="${c(o)}" y="${c(f)}" width="${c(u)}" height="${c(i)}" fill="${t}"><title>${s(`${e.label}: ${l(e.value)}`)}</title></rect>`;
	}).join("");
	return `${h(n)}${f}${g(e.points, d)}`;
}
function v(e, t) {
	let n = m(Math.max(...e.points.map((e) => e.value), 0)), o = e.points.length > 1 ? i / (e.points.length - 1) : 0, u = (t) => e.points.length > 1 ? r + o * t : 176, d = e.points.map((e, t) => ({
		x: u(t),
		y: 150 - e.value / n * a,
		point: e
	})), f = d.map(({ x: e, y: t }) => `${c(e)},${c(t)}`).join(" "), p = d.map(({ x: e, y: n, point: r }) => `<circle class="widget-chart-vertex" cx="${c(e)}" cy="${c(n)}" r="2.5" fill="${t}"><title>${s(`${r.label}: ${l(r.value)}`)}</title></circle>`).join("");
	return `${h(n)}<polyline class="widget-chart-line" points="${f}" fill="none" stroke="${t}" stroke-width="2" stroke-linejoin="round" stroke-linecap="round" />${p}${g(e.points, u)}`;
}
function y(e, t, n, r, i) {
	if (i - r >= Math.PI * 2 - 1e-9) return `M ${c(e)} ${c(t - n)} A ${n} ${n} 0 1 1 ${c(e)} ${c(t + n)} A ${n} ${n} 0 1 1 ${c(e)} ${c(t - n)} Z`;
	let a = (r) => `${c(e + n * Math.sin(r))} ${c(t - n * Math.cos(r))}`, o = +(i - r > Math.PI);
	return `M ${c(e)} ${c(t)} L ${a(r)} A ${n} ${n} 0 ${o} 1 ${a(i)} Z`;
}
function b(t) {
	let n = t.points.reduce((e, t) => e + Math.max(t.value, 0), 0);
	if (n <= 0) return "";
	let r = a / 2 - 2, i = 0;
	return `${t.points.map((t) => {
		let a = Math.max(t.value, 0) / n * Math.PI * 2, o = y(109, 81, r, i, i + a);
		i += a;
		let c = Math.round(Math.max(t.value, 0) / n * 100);
		return `<path class="widget-chart-slice" d="${o}" fill="${e(t.label)}"><title>${s(`${t.label}: ${l(t.value)} (${c}%)`)}</title></path>`;
	}).join("")}${t.points.slice(0, 8).map((t, n) => {
		let r = 20 + n * 14;
		return `<rect x="188" y="${r - 6}" width="8" height="8" rx="2" fill="${e(t.label)}" /><text class="widget-chart-label" x="200" y="${r + 1}">${s(u(t.label))} ${s(l(t.value))}</text>`;
	}).join("")}`;
}
function x(t, n) {
	let r = d(t), i = t.title ?? `${n.valueTitle} by ${n.categoryTitle}`, a = f(n, r);
	if (n.points.length === 0) return {
		svg: p("", i, a),
		summary: a
	};
	let o = e(t.title ?? n.categoryTitle);
	return {
		svg: p(r === "arc" ? b(n) : r === "line" ? v(n, o) : _(n, o), i, a),
		summary: a
	};
}
//#endregion
export { l as formatValue, x as renderChartSvg, d as specMark };
