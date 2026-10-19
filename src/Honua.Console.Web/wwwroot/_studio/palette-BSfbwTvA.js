//#region src/composition/palette.ts
var e = [
	"#0b6b4d",
	"#b4531b",
	"#3a5fa8",
	"#8a2f6b",
	"#1f7a8c",
	"#94741a",
	"#5c3b8f",
	"#a03a3a"
];
function t(e) {
	let t = 2166136261;
	for (let n = 0; n < e.length; n += 1) t ^= e.charCodeAt(n), t = Math.imul(t, 16777619) >>> 0;
	return t >>> 0;
}
function n(n) {
	let r = e;
	return r[t(n) % r.length] ?? r[0] ?? "#0b6b4d";
}
var r = {
	neutral: "#5f6e66",
	accent: "#0b6b4d",
	warning: "#b4531b",
	critical: "#a03a3a",
	cool: "#3a5fa8"
};
function i(e) {
	return r[e.toLowerCase()] ?? n(`style:${e}`);
}
function a(e) {
	return e.styleRef ? i(e.styleRef.styleId) : n(e.id);
}
//#endregion
export { n, i as r, a as t };
