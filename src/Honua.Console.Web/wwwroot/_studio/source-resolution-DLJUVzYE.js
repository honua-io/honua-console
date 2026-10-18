import { t as e } from "./rolldown-runtime-Dy4uBu1J.js";
//#region src/map/source-resolution.ts
var t = /* @__PURE__ */ e({
	findCompositionSourceDescriptor: () => n,
	hasCompositionSourceOverride: () => c,
	isCompositionSourceResolution: () => f,
	layerTypeForGeometry: () => l,
	resolveCompositionSource: () => d,
	usesHonuaStyleCatalog: () => r
});
function n(e, t = []) {
	let n = t.find((t) => t.id === e);
	if (n) return n;
	let r = t.filter((t) => t.locator?.serviceId === e);
	return r.length === 1 ? r[0] : void 0;
}
function r(e) {
	return e?.locator?.serviceId !== void 0;
}
var i = "/api", a = 2e3;
function o(e) {
	if (typeof e != "object" || !e) return !1;
	let t = Object.getPrototypeOf(e);
	return t === Object.prototype || t === null;
}
function s(e) {
	let t = e.metadata?.honua;
	return o(t) ? t : void 0;
}
function c(e) {
	return s(e)?.source !== void 0;
}
function l(e) {
	if (!e) return;
	let t = e.toLowerCase().replace(/[^a-z]/g, "").replace(/^esrigeometry/, "").replace(/^multi/, "");
	if (t === "polygon") return "fill";
	if (t === "linestring" || t === "line" || t === "polyline") return "line";
	if (t === "point") return "circle";
	if (t === "raster" || t === "image") return "raster";
}
function u(e) {
	return e.endsWith("/") ? e.slice(0, -1) : e;
}
function d(e, t = {}) {
	let r = u(t.baseUrl ?? i), c = t.featureLimit ?? a, d = s(e), f = d?.source;
	if (o(f)) return {
		source: f,
		layerType: (typeof d?.layerType == "string" ? d.layerType : void 0) ?? l(typeof d?.geometryType == "string" ? d.geometryType : void 0) ?? "fill"
	};
	let p = n(e.sourceId, t.catalog);
	if (!p) return { reason: `Source "${e.sourceId}" is not in the catalog, and the layer carries no metadata.honua.source override.` };
	let m = l(p.geometryType);
	if (m === "raster") return { reason: `Source "${e.sourceId}" is raster (${p.geometryType}); the composition map renders vector sources only so far.` };
	if (!m) return { reason: `Source "${e.sourceId}" has no supported geometry type in the catalog.` };
	let h = p.locator;
	if (h && (typeof h.url != "string" || !/^(?:https?:\/\/|\/)/.test(h.url))) return { reason: `Source "${e.sourceId}" has an invalid catalog locator URL.` };
	let g = p.protocol;
	if (g === "ogc-features") {
		if (h && (typeof h.collectionId != "string" || !h.collectionId)) return { reason: `Source "${e.sourceId}" has no collection id in its catalog locator.` };
		let t = h ? String(h.collectionId) : e.sourceId, n = h ? u(String(h.url)) : `${r}/ogc/features`;
		return {
			binding: {
				sourceId: e.sourceId,
				protocol: "ogc_features",
				locator: {
					url: n,
					collectionId: t
				}
			},
			source: {
				type: "geojson",
				data: `${n}/collections/${encodeURIComponent(t)}/items?f=json&limit=${c}`
			},
			layerType: m
		};
	}
	if (g === "geoservices-feature-service") {
		if (h && (!Number.isSafeInteger(h.layerId) || Number(h.layerId) < 0)) return { reason: `Source "${e.sourceId}" has no valid layer id in its catalog locator.` };
		let t = h ? Number(h.layerId) : 0, n = h ? u(String(h.url)) : void 0, i = n?.match(/\/FeatureServer\/(\d+)$/i);
		if (i && Number(i[1]) !== t) return { reason: `Source "${e.sourceId}" has conflicting layer identities in its catalog locator.` };
		let a = n ? i ? n : `${n}/${t}` : `${r}/rest/services/${encodeURIComponent(e.sourceId)}/FeatureServer/${t}`;
		return {
			binding: {
				sourceId: e.sourceId,
				protocol: "geoservices_feature_service",
				locator: {
					url: a,
					layerId: t
				}
			},
			source: {
				type: "geojson",
				data: `${a}/query?where=1%3D1&outFields=*&f=geojson&resultRecordCount=${c}`
			},
			layerType: m
		};
	}
	return { reason: `Source "${e.sourceId}" advertises protocol "${g ?? "unknown"}", which the composition map cannot render yet.` };
}
function f(e) {
	return e.source !== void 0;
}
//#endregion
export { d as a, l as i, c as n, t as o, f as r, r as s, n as t };
