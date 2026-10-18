//#region node_modules/@honua/sdk-js/dist/src/core/error-classifications.js
var e = Object.freeze({
	"core.http.transient": t("core", "protocol", !0),
	"core.http.rejected": t("core", "protocol", !1),
	"core.timeout": t("core", "timeout", !0),
	"core.network": t("core", "network", !0),
	"core.cancelled": t("core", "cancellation", !1),
	"core.grpc.transient": t("core", "protocol", !0),
	"core.grpc.rejected": t("core", "protocol", !1),
	"pmtiles.lifecycle.invalid-request": t("pmtiles", "validation", !1),
	"pmtiles.lifecycle.invalid-response": t("pmtiles", "validation", !1),
	"pmtiles.lifecycle.response-too-large": t("pmtiles", "validation", !1),
	"pmtiles.lifecycle.job-poll-timeout": t("pmtiles", "timeout", !0),
	"pmtiles.lifecycle.job-failed": t("pmtiles", "protocol", !1),
	"pmtiles.lifecycle.job-cancelled": t("pmtiles", "cancellation", !1),
	"pmtiles.lifecycle.access-url-expired": t("pmtiles", "validation", !1),
	"pmtiles.lifecycle.manual-cleanup-unsupported": t("pmtiles", "capability", !1),
	"core.geometry.unknown-geometry": t("core", "validation", !1),
	"core.geometry.malformed-geometry": t("core", "validation", !1),
	"core.auth.interaction-required": t("core", "authentication", !1),
	"core.auth.refresh-failed": t("core", "authentication", !0),
	"core.auth.invalid-grant": t("core", "authentication", !1),
	"core.capability-not-supported": t("core", "capability", !1),
	"core.exploration-context": t("core", "validation", !1),
	"core.wfs-exception": t("core", "protocol", !1),
	"core.job-failed": t("core", "protocol", !1),
	"core.wms-capabilities-parse": t("core", "protocol", !1),
	"core.wmts-capabilities-parse": t("core", "protocol", !1),
	"core.coverage.invalid-request": t("core", "validation", !1),
	"core.coverage.invalid-response": t("core", "protocol", !1),
	"core.coverage.response-too-large": t("core", "validation", !1),
	"core.coverage.unsupported-format": t("core", "capability", !1),
	"core.coverage.service-error": t("core", "protocol", !1),
	"core.coverage.wcs-exception": t("core", "protocol", !1),
	"core.zarr.invalid-request": t("core", "validation", !1),
	"core.zarr.invalid-response": t("core", "protocol", !1),
	"core.zarr.response-too-large": t("core", "validation", !1),
	"core.zarr.metadata-pending": t("core", "capability", !1),
	"core.zarr.missing-spatial-extent": t("core", "capability", !1),
	"core.zarr.no-tileable-variable": t("core", "capability", !1),
	"core.zarr.missing-spatial-reference": t("core", "capability", !1),
	"core.zarr.spatial-reference-mismatch": t("core", "capability", !1),
	"core.zarr.unsupported-version": t("core", "capability", !1),
	"core.zarr.unsupported-codec": t("core", "capability", !1),
	"core.zarr.unsupported-dtype": t("core", "capability", !1),
	"core.zarr.ambiguous-dimensions": t("core", "validation", !1),
	"core.zarr.service-error": t("core", "protocol", !1),
	"discovery.ambiguous-protocol": t("discovery", "validation", !1),
	"discovery.ambiguous-source": t("discovery", "validation", !1),
	"discovery.invalid-cloud-native-input": t("discovery", "validation", !1),
	"discovery.invalid-cloud-native-manifest": t("discovery", "validation", !1),
	"discovery.invalid-endpoint": t("discovery", "validation", !1),
	"discovery.invalid-cache-identity": t("discovery", "validation", !1),
	"discovery.invalid-discovery-cache": t("discovery", "validation", !1),
	"discovery.invalid-capability": t("discovery", "validation", !1),
	"discovery.cloud-native-operation-unavailable": t("discovery", "capability", !1),
	"discovery.unsupported-protocol": t("discovery", "capability", !1),
	"discovery.protocol-mismatch": t("discovery", "validation", !1),
	"query.planning.invalid-query": t("query", "validation", !1),
	"query.planning.unsupported-compiler": t("query", "capability", !1),
	"query.planning.unsupported-query": t("query", "capability", !1),
	"query.planning.capability-not-supported": t("query", "capability", !1),
	"query.planning.fallback-disabled": t("query", "capability", !1),
	"query.planning.unsafe-materialization": t("query", "validation", !1),
	"query.execution.invalid-plan": t("query", "validation", !1),
	"query.execution.wfs-protocol": t("query", "protocol", !1),
	"query.execution.plan-context-mismatch": t("query", "validation", !1),
	"query.execution.unsafe-materialization": t("query", "validation", !1),
	"query.execution.invalid-resource-handle": t("query", "validation", !1),
	"query.execution.resource-unavailable": t("query", "authentication", !1),
	"query.execution.resource-expired": t("query", "authentication", !1),
	"query.execution.resource-resolution-failed": t("query", "internal", !1),
	"query.execution.resource-execution-failed": t("query", "internal", !1),
	"map.source-adapter.disposed": t("map", "validation", !1),
	"map.source-adapter.source-conflict": t("map", "validation", !1),
	"map.source-adapter.layer-conflict": t("map", "validation", !1),
	"map.source-adapter.unsupported-plan": t("map", "capability", !1),
	"map.source-adapter.invalid-option": t("map", "validation", !1),
	"map.source-adapter.map-mutation-failed": t("map", "internal", !1),
	"map.data-bridge.invalid-option": t("map", "validation", !1),
	"map.data-bridge.disposed": t("map", "validation", !1),
	"map.data-bridge.source-conflict": t("map", "validation", !1),
	"map.data-bridge.layer-conflict": t("map", "validation", !1),
	"map.data-bridge.map-mutation-failed": t("map", "internal", !1),
	"map.data-bridge.interaction-unsupported": t("map", "capability", !1),
	"map.data-bridge.filter-unsupported": t("map", "capability", !1),
	"map.automatic-strategy.no-eligible-strategy": t("map", "capability", !1),
	"map.automatic-strategy.stale-plan": t("map", "validation", !1),
	"map.automatic-strategy.source-conflict": t("map", "validation", !1),
	"map.automatic-strategy.layer-conflict": t("map", "validation", !1),
	"map.automatic-strategy.map-mutation-failed": t("map", "internal", !1),
	"map.automatic-strategy.cancelled": t("map", "cancellation", !1),
	"map.automatic-strategy.disposed": t("map", "validation", !1),
	"map.raster-strategy.unsupported-strategy": t("map", "capability", !1),
	"map.raster-strategy.capability-mismatch": t("map", "capability", !1),
	"map.raster-strategy.missing-metadata": t("map", "validation", !1),
	"map.raster-strategy.invalid-option": t("map", "validation", !1),
	"map.raster-strategy.source-conflict": t("map", "validation", !1),
	"map.raster-strategy.layer-conflict": t("map", "validation", !1),
	"map.raster-strategy.map-mutation-failed": t("map", "internal", !1),
	"map.automatic-integration.disposed": t("map", "validation", !1),
	"map.automatic-integration.invalid-target": t("map", "validation", !1),
	"map.temporal-playback.invalid-option": t("map", "validation", !1),
	"runtime.map-package.fetch": t("runtime", "network", !0),
	"runtime.map-package.load": t("runtime", "internal", !1),
	"runtime.map-package.validate": t("runtime", "validation", !1),
	"runtime.map-package.export": t("runtime", "validation", !1),
	"runtime.map-package.import": t("runtime", "validation", !1),
	"runtime.map-package.update": t("runtime", "internal", !1),
	"runtime.map-package.style-compose": t("runtime", "validation", !1),
	"runtime.map-package.source-bind": t("runtime", "internal", !1),
	"runtime.map-package.view": t("runtime", "internal", !1),
	"runtime.map-package.popup": t("runtime", "validation", !1),
	"runtime.map-package.dispose": t("runtime", "internal", !0),
	"runtime.diagnostic": t("runtime", "validation", !1),
	"runtime.query-tiles.transient": t("runtime", "protocol", !0),
	"runtime.query-tiles.rejected": t("runtime", "protocol", !1),
	"realtime.cancelled": t("realtime", "cancellation", !1),
	"realtime.transport.reconnectable": t("realtime", "network", !0),
	"realtime.checkpoint.invalid": t("realtime", "validation", !1),
	"realtime.sequence.gap": t("realtime", "protocol", !0),
	"realtime.protocol.terminal": t("realtime", "protocol", !1),
	"realtime.reconciliation.disposed": t("realtime", "validation", !1),
	"realtime.reconciliation.invalid-option": t("realtime", "validation", !1),
	"offline.region.validation": t("offline", "validation", !1),
	"offline.region.quota": t("offline", "validation", !1),
	"offline.region.integrity": t("offline", "protocol", !1),
	"offline.region.miss": t("offline", "validation", !1),
	"offline.cancelled": t("offline", "cancellation", !1),
	"offline.transport.failure": t("offline", "network", !1),
	"offline.transport.transient": t("offline", "network", !0),
	"offline.storage.concurrent": t("offline", "internal", !0),
	"offline.storage.failure": t("offline", "internal", !1),
	"offline.replica-sync.capability": t("offline", "capability", !1),
	"offline.replica-sync.validation": t("offline", "validation", !1),
	"offline.replica-sync.permission-denied": t("offline", "authentication", !1),
	"plugin.registry.validation": t("plugin", "validation", !1),
	"plugin.compatibility": t("plugin", "capability", !1),
	"plugin.execution.policy-denied": t("plugin", "capability", !1),
	"plugin.capability-unavailable": t("plugin", "capability", !1),
	"plugin.lifecycle.activation": t("plugin", "internal", !1),
	"plugin.execution.validation": t("plugin", "validation", !1),
	"plugin.lifecycle.cleanup": t("plugin", "internal", !1),
	"plugin.cancelled": t("plugin", "cancellation", !1),
	"plugin.internal": t("plugin", "internal", !1),
	"agent.tool.unknown-tool": t("agent", "validation", !1),
	"agent.tool.missing-runtime": t("agent", "validation", !1),
	"agent.tool.unqualified-selection": t("agent", "validation", !1),
	"agent.tool.missing-runtime-method": t("agent", "capability", !1),
	"agent.tool.internal": t("agent", "internal", !1),
	"agent.safety.aborted": t("agent", "cancellation", !1),
	"agent.safety.invalid-input": t("agent", "validation", !1),
	"agent.safety.policy-denied": t("agent", "capability", !1),
	"agent.safety.integrity-failed": t("agent", "protocol", !1),
	"agent.safety.approval-expired": t("agent", "authentication", !1),
	"agent.safety.context-mismatch": t("agent", "validation", !1),
	"agent.safety.signature-invalid": t("agent", "validation", !1),
	"agent.safety.execution-failed": t("agent", "internal", !1),
	"agent.safety.audit-failed": t("agent", "internal", !1),
	"agent.safety.receipt-failed": t("agent", "internal", !1),
	"app.unsupported-profile": t("app", "capability", !1),
	"app.unsupported-widget": t("app", "capability", !1),
	"app.missing-manifest": t("app", "validation", !1),
	"app.missing-manifest-artifact": t("app", "validation", !1),
	"app.missing-map-package": t("app", "validation", !1),
	"app.map-package-mismatch": t("app", "validation", !1),
	"app.missing-widget": t("app", "validation", !1),
	"app.missing-binding": t("app", "validation", !1),
	"app.map-load-failed": t("app", "internal", !1),
	"app.data-load-failed": t("app", "internal", !1),
	"app.render-failed": t("app", "internal", !1),
	"app.disposed": t("app", "validation", !1),
	"app.export-unsafe": t("app", "validation", !1),
	"app.export-failed": t("app", "internal", !1)
});
function t(e, t, n) {
	return Object.freeze([
		e,
		t,
		n
	]);
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/error-envelope.js
var n = "honua.sdk.error.v1", r = class extends Error {
	kind = n;
	domain;
	sdkCode;
	category;
	retryable;
	operationId;
	requestId;
	terminalReceipt;
	context;
	constructor(e, t, n = {}) {
		let [r, i, a] = u(e);
		super(t, "cause" in n ? { cause: n.cause } : void 0), this.name = "HonuaSdkError", this.domain = r, this.sdkCode = e, this.category = i, this.retryable = a, this.operationId = C(n.operationId), this.requestId = C(n.requestId), this.terminalReceipt = n.terminalReceipt, this.context = s(n.context);
	}
	toJSON() {
		return a(this);
	}
};
function i(t) {
	try {
		if (!E(t) || Array.isArray(t)) return !1;
		let n = D(t, "kind"), r = D(t, "sdkCode");
		if (n !== "honua.sdk.error.v1" || typeof r != "string" || !l(r)) return !1;
		let [i, a, o] = e[r], s = D(t, "context"), c = D(t, "name");
		return D(t, "domain") === i && D(t, "retryable") === o && D(t, "category") === a && typeof c == "string" && E(s) && !Array.isArray(s);
	} catch {
		return !1;
	}
}
function a(t) {
	let r = D(t, "sdkCode");
	if (typeof r != "string" || !l(r)) throw TypeError("Cannot serialize an SDK error with an unregistered code");
	let [i, a, c] = e[r], u = T(D(t, "cause")), d = C(w(D(t, "operationId"))), f = C(w(D(t, "requestId"))), p = D(t, "terminalReceipt"), m = D(t, "context");
	return {
		kind: n,
		name: O(D(t, "name")),
		domain: i,
		code: r,
		category: a,
		retryable: c,
		...d ? { operationId: d } : {},
		...f ? { requestId: f } : {},
		...o(p) ? { receipt: p } : {},
		context: E(m) && !Array.isArray(m) ? s(m) : N(),
		...u ? { cause: u } : {}
	};
}
function o(e) {
	return E(e) && typeof D(e, "kind") == "string" && typeof D(e, "retryable") == "boolean" && Array.isArray(D(e, "fieldErrors")) && E(D(e, "protocolMetadata"));
}
function s(e) {
	if (!e) return N();
	try {
		let t = b(e, /* @__PURE__ */ new WeakSet(), 0);
		return Object.freeze(t);
	} catch {
		return P("value", "[UNSERIALIZABLE]");
	}
}
function c(...e) {
	let t = Object.create(null), n = 0;
	for (let r of e) if (r) for (let [e, i] of Object.entries(Object.getOwnPropertyDescriptors(r))) i.enumerable && (j.test(e) ? n += 1 : t[e] = "value" in i ? i.value : "[ACCESSOR]");
	return n > 0 && (t.__redacted_keys__ = n), t;
}
function l(t) {
	return Object.hasOwn(e, t);
}
function u(t) {
	if (!l(t)) throw TypeError(`Unregistered Honua SDK error code: ${t}`);
	return e[t];
}
var d = "[REDACTED]", f = "[TRUNCATED]", p = 6, m = 100, h = 100, g = 2048, _ = /(?:authorization|proxy-authorization|cookie|set-cookie|credential|password|passwd|secret|token|api[-_]?key|access[-_]?key|access[-_]?id|signature|cursor|resume[-_]?token|where|filter|query|sql|cql|body|payload|form|parameters?)/i, v = /^(?:local[-_]?storage|storage|cache(?:[-_]?file)?|file|filesystem)[-_]?(?:path|directory|url|uri|location)$/i, y = /(?:url|uri|href|endpoint|location)$/i;
function b(e, t, n) {
	if (n >= p) return P("value", f);
	if (t.has(e)) return P("value", "[CIRCULAR]");
	t.add(e);
	let r = Object.create(null), i = Object.entries(Object.getOwnPropertyDescriptors(e)).filter(([, e]) => e.enumerable).slice(0, m), a = 0;
	for (let [e, o] of i) {
		if (j.test(e)) {
			a += 1;
			continue;
		}
		let i = "value" in o ? o.value : "[ACCESSOR]";
		r[e] = _.test(e) || v.test(e) ? d : x(i, e, t, n + 1);
	}
	return a > 0 && (r.__redacted_keys__ = a), Reflect.ownKeys(e).length > i.length && (r.__truncated__ = f), t.delete(e), Object.freeze(r);
}
function x(e, t, n, r) {
	if (e === null || typeof e == "boolean") return e;
	if (typeof e == "number") return Number.isFinite(e) ? e : String(e);
	if (typeof e == "string") return S(e, y.test(t));
	if (typeof e == "bigint") return e.toString();
	if (e === void 0) return "[UNDEFINED]";
	if (typeof e == "function" || typeof e == "symbol") return "[UNSERIALIZABLE]";
	if (e instanceof Error) return P("name", k(e));
	if (ArrayBuffer.isView(e) || e instanceof ArrayBuffer) return "[BINARY]";
	if (e instanceof Date) {
		let t = Date.prototype.getTime.call(e);
		return Number.isNaN(t) ? "[INVALID_DATE]" : Date.prototype.toISOString.call(e);
	}
	if (Array.isArray(e)) {
		if (r >= p) return f;
		if (n.has(e)) return "[CIRCULAR]";
		n.add(e);
		let i = e.slice(0, h).map((e) => x(e, t, n, r + 1));
		return e.length > i.length && i.push(f), n.delete(e), Object.freeze(i);
	}
	return E(e) ? b(e, n, r) : "[UNSERIALIZABLE]";
}
function S(e, t) {
	let n = e.length > g ? `${e.slice(0, g)}${f}` : e;
	if (t || /^https?:\/\//i.test(n)) try {
		let e = new URL(n);
		e.username = "", e.password = "";
		for (let t of [...e.searchParams.keys()]) A(t) && e.searchParams.set(t, d);
		return e.hash = "", e.toString();
	} catch {
		if (t) return d;
	}
	return n.replace(/\bBearer\s+[^\s,;]+/gi, `Bearer ${d}`).replace(/\b(access_token|refresh_token|api[-_]?key|password|passwd|secret|token|cursor|resume[-_]?token)\s*[:=]\s*([^\s,;&]+)/gi, (e, t) => `${t}=${d}`);
}
function C(e) {
	if (e !== void 0) return /^[A-Za-z0-9][A-Za-z0-9._:/-]{0,255}$/.test(e) ? e : d;
}
function w(e) {
	return typeof e == "string" ? e : void 0;
}
function T(t) {
	if (t !== void 0) try {
		if (i(t)) {
			let n = D(t, "sdkCode");
			if (typeof n != "string" || !l(n)) return { name: "Error" };
			let [r, i, a] = e[n];
			return {
				name: O(D(t, "name")),
				domain: r,
				code: n,
				category: i,
				retryable: a
			};
		}
		return t instanceof Error ? { name: k(t) } : { name: typeof t };
	} catch {
		return { name: "Error" };
	}
}
function E(e) {
	return typeof e == "object" && !!e;
}
function D(e, t) {
	let n = Object.getOwnPropertyDescriptor(e, t);
	return n && "value" in n ? n.value : void 0;
}
function O(e) {
	return typeof e == "string" && M.has(e) ? e : "Error";
}
function k(e) {
	try {
		let t = D(e, "name");
		if (typeof t == "string") return O(t);
		let n = Object.getPrototypeOf(e);
		return n ? O(D(n, "name")) : "Error";
	} catch {
		return "Error";
	}
}
function A(e) {
	let t = e.toLocaleLowerCase("en-US").replace(/[^a-z0-9]+/g, "");
	return t === "key" || /(?:accesstoken|refreshtoken|securitytoken|accesskeyid|accessid|credential|authorization|apikey|signature|password|passwd|secret|cursor|resumetoken|continuationtoken|filter|where|query|sql|cql|auth|sig)$/.test(t);
}
var j = /^(?:__proto__|prototype|constructor)$/, M = /* @__PURE__ */ new Set(/* @__PURE__ */ "AbortError.AggregateError.ConnectError.DOMException.Error.EvalError.HonuaAbortError.HonuaAgentExecutionError.HonuaAgentSafetyError.HonuaAgentToolError.HonuaAuthError.HonuaAutomaticMapLibreIntegrationError.HonuaAutomaticMapLibreStrategyError.HonuaCapabilityNotSupportedError.HonuaDataToMapBridgeError.HonuaDiscoveryError.HonuaExplorationContextError.HonuaGeneratedAppError.HonuaGrpcError.HonuaHttpError.HonuaJobFailedError.HonuaMapLibreRasterStrategyError.HonuaMapLibreSourceAdapterError.HonuaMapPackageError.HonuaNetworkError.HonuaOfflineRegionError.HonuaOfflineEditQueueError.HonuaPluginRegistryError.HonuaQueryPlanExecutionError.HonuaQueryPlanningError.HonuaRealtimeResumeError.HonuaReplicaSyncError.HonuaRuntimeDiagnosticError.HonuaSdkError.HonuaTemporalPlaybackError.HonuaTimeoutError.HonuaWfsExceptionError.HonuaWfsProtocolError.HonuaWmsCapabilitiesParseError.HonuaWmtsCapabilitiesParseError.NetworkError.QueryTileServerResponseError.RangeError.ReferenceError.SyntaxError.TimeoutError.TypeError.URIError".split("."));
function N() {
	return Object.freeze(Object.create(null));
}
function P(e, t) {
	let n = Object.create(null);
	return n[e] = t, Object.freeze(n);
}
//#endregion
export { c as n, r as t };
