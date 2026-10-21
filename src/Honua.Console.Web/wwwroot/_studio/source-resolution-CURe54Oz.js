import { t as e } from "./rolldown-runtime-Dy4uBu1J.js";
//#region src/map/source-resolution.ts
var t = /* @__PURE__ */ e({
	findCompositionSourceDescriptor: () => n,
	hasCompositionSourceOverride: () => c,
	isAnonymousMapAssetSource: () => u,
	isCompositionSourceResolution: () => m,
	isStaticInlineCompositionSource: () => l,
	layerTypeForGeometry: () => d,
	resolveCompositionSource: () => p,
	usesHonuaStyleCatalog: () => r
});
function n(e, t = []) {
	let n = t.filter((t) => t.id === e);
	if (n.length > 0) return n.length === 1 ? n[0] : void 0;
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
	return !o(e) || e.type !== "geojson" || !o(e.data) || ![
		"FeatureCollection",
		"Feature",
		"GeometryCollection",
		"Point",
		"MultiPoint",
		"LineString",
		"MultiLineString",
		"Polygon",
		"MultiPolygon"
	].includes(String(e.data.type)) ? !1 : ![
		"url",
		"urls",
		"tiles"
	].some((t) => t in e);
}
function u(e) {
	if (!o(e) || ![
		"raster",
		"vector",
		"raster-dem",
		"image",
		"video"
	].includes(String(e.type)) || e.url !== void 0 && typeof e.url != "string" || e.tiles !== void 0 && (!Array.isArray(e.tiles) || !e.tiles.every((e) => typeof e == "string")) || e.urls !== void 0 && (!Array.isArray(e.urls) || !e.urls.every((e) => typeof e == "string"))) return !1;
	let t = [
		...typeof e.url == "string" ? [e.url] : [],
		...Array.isArray(e.tiles) ? e.tiles : [],
		...Array.isArray(e.urls) ? e.urls : []
	];
	return t.length === 0 || !t.every((e) => typeof e == "string") ? !1 : t.every((e) => {
		try {
			let t = new URL(e, "https://audience.invalid/");
			return /^https?:$/.test(t.protocol) && !t.username && !t.password;
		} catch {
			return !1;
		}
	});
}
function d(e) {
	if (!e) return;
	let t = e.toLowerCase().replace(/[^a-z]/g, "").replace(/^esrigeometry/, "").replace(/^multi/, "");
	if (t === "polygon") return "fill";
	if (t === "linestring" || t === "line" || t === "polyline") return "line";
	if (t === "point") return "circle";
	if (t === "raster" || t === "image") return "raster";
}
function f(e) {
	return e.endsWith("/") ? e.slice(0, -1) : e;
}
function p(e, t = {}) {
	let r = f(t.baseUrl ?? i), c = t.featureLimit ?? a, l = e.type, u = s(e), p = u?.source;
	if (l !== void 0 && l !== "fill" && l !== "line" && l !== "circle" && !(l === "raster" && o(p))) return {
		reason: `Source "${e.sourceId}" requests layer type "${l}", which the composition map cannot render yet.`,
		scope: "layer"
	};
	if (o(p)) {
		let e = typeof u?.layerType == "string" ? u.layerType : void 0;
		return {
			source: p,
			layerType: l ?? e ?? d(typeof u?.geometryType == "string" ? u.geometryType : void 0) ?? "fill"
		};
	}
	let m = n(e.sourceId, t.catalog);
	if (!m) return { reason: `Source "${e.sourceId}" is not in the catalog, and the layer carries no metadata.honua.source override.` };
	let h = d(m.geometryType);
	if (h === "raster") return {
		reason: `Source "${e.sourceId}" is raster (${m.geometryType}); the composition map renders vector sources only so far.`,
		scope: "layer"
	};
	let g = l ?? h;
	if (!g) return {
		reason: `Source "${e.sourceId}" has no supported geometry type in the catalog.`,
		scope: "layer"
	};
	let _ = m.locator;
	if (_ && (typeof _.url != "string" || !/^(?:https?:\/\/|\/)/.test(_.url))) return { reason: `Source "${e.sourceId}" has an invalid catalog locator URL.` };
	let v = m.protocol;
	if (v === "ogc-features") {
		if (_ && (typeof _.collectionId != "string" || !_.collectionId)) return { reason: `Source "${e.sourceId}" has no collection id in its catalog locator.` };
		let t = _ ? String(_.collectionId) : e.sourceId, n = _ ? f(String(_.url)) : `${r}/ogc/features`;
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
			layerType: g
		};
	}
	if (v === "geoservices-feature-service") {
		if (_ && (!Number.isSafeInteger(_.layerId) || Number(_.layerId) < 0)) return { reason: `Source "${e.sourceId}" has no valid layer id in its catalog locator.` };
		let t = _ ? Number(_.layerId) : 0, n = _ ? f(String(_.url)) : void 0, i = n?.match(/\/FeatureServer\/(\d+)$/i);
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
			layerType: g
		};
	}
	return { reason: `Source "${e.sourceId}" advertises protocol "${v ?? "unknown"}", which the composition map cannot render yet.` };
}
function m(e) {
	return e.source !== void 0;
}
//#endregion
export { l as a, t as c, m as i, r as l, c as n, d as o, u as r, p as s, n as t };
