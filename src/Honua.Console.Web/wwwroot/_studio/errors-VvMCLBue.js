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
		super(t, "cause" in n ? { cause: n.cause } : void 0), this.name = "HonuaSdkError", this.domain = r, this.sdkCode = e, this.category = i, this.retryable = a, this.operationId = x(n.operationId), this.requestId = x(n.requestId), this.terminalReceipt = n.terminalReceipt, this.context = s(n.context);
	}
	toJSON() {
		return a(this);
	}
};
function i(t) {
	try {
		if (!C(t) || Array.isArray(t)) return !1;
		let n = w(t, "kind"), r = w(t, "sdkCode");
		if (n !== "honua.sdk.error.v1" || typeof r != "string" || !l(r)) return !1;
		let [i, a, o] = e[r], s = w(t, "context"), c = w(t, "name");
		return w(t, "domain") === i && w(t, "retryable") === o && w(t, "category") === a && typeof c == "string" && C(s) && !Array.isArray(s);
	} catch {
		return !1;
	}
}
function a(t) {
	let r = w(t, "sdkCode");
	if (typeof r != "string" || !l(r)) throw TypeError("Cannot serialize an SDK error with an unregistered code");
	let [i, a, c] = e[r], u = ne(w(t, "cause")), d = x(S(w(t, "operationId"))), f = x(S(w(t, "requestId"))), p = w(t, "terminalReceipt"), m = w(t, "context");
	return {
		kind: n,
		name: T(w(t, "name")),
		domain: i,
		code: r,
		category: a,
		retryable: c,
		...d ? { operationId: d } : {},
		...f ? { requestId: f } : {},
		...o(p) ? { receipt: p } : {},
		context: C(m) && !Array.isArray(m) ? s(m) : k(),
		...u ? { cause: u } : {}
	};
}
function o(e) {
	return C(e) && typeof w(e, "kind") == "string" && typeof w(e, "retryable") == "boolean" && Array.isArray(w(e, "fieldErrors")) && C(w(e, "protocolMetadata"));
}
function s(e) {
	if (!e) return k();
	try {
		let t = y(e, /* @__PURE__ */ new WeakSet(), 0);
		return Object.freeze(t);
	} catch {
		return A("value", "[UNSERIALIZABLE]");
	}
}
function c(...e) {
	let t = Object.create(null), n = 0;
	for (let r of e) if (r) {
		for (let [e, i] of Object.entries(Object.getOwnPropertyDescriptors(r))) if (i.enumerable) {
			if (O.test(e)) {
				n += 1;
				continue;
			}
			t[e] = "value" in i ? i.value : "[ACCESSOR]";
		}
	}
	return n > 0 && (t.__redacted_keys__ = n), t;
}
function l(t) {
	return Object.hasOwn(e, t);
}
function u(t) {
	if (!l(t)) throw TypeError(`Unregistered Honua SDK error code: ${t}`);
	return e[t];
}
var d = "[REDACTED]", f = "[TRUNCATED]", p = 6, m = 100, h = 100, g = 2048, _ = /(?:authorization|proxy-authorization|cookie|set-cookie|credential|password|passwd|secret|token|api[-_]?key|access[-_]?key|access[-_]?id|signature|cursor|resume[-_]?token|where|filter|query|sql|cql|body|payload|form|parameters?)/i, ee = /^(?:local[-_]?storage|storage|cache(?:[-_]?file)?|file|filesystem)[-_]?(?:path|directory|url|uri|location)$/i, v = /(?:url|uri|href|endpoint|location)$/i;
function y(e, t, n) {
	if (n >= p) return A("value", f);
	if (t.has(e)) return A("value", "[CIRCULAR]");
	t.add(e);
	let r = Object.create(null), i = Object.entries(Object.getOwnPropertyDescriptors(e)).filter(([, e]) => e.enumerable).slice(0, m), a = 0;
	for (let [e, o] of i) {
		if (O.test(e)) {
			a += 1;
			continue;
		}
		let i = "value" in o ? o.value : "[ACCESSOR]";
		r[e] = _.test(e) || ee.test(e) ? d : b(i, e, t, n + 1);
	}
	return a > 0 && (r.__redacted_keys__ = a), Reflect.ownKeys(e).length > i.length && (r.__truncated__ = f), t.delete(e), Object.freeze(r);
}
function b(e, t, n, r) {
	if (e === null || typeof e == "boolean") return e;
	if (typeof e == "number") return Number.isFinite(e) ? e : String(e);
	if (typeof e == "string") return te(e, v.test(t));
	if (typeof e == "bigint") return e.toString();
	if (e === void 0) return "[UNDEFINED]";
	if (typeof e == "function" || typeof e == "symbol") return "[UNSERIALIZABLE]";
	if (e instanceof Error) return A("name", E(e));
	if (ArrayBuffer.isView(e) || e instanceof ArrayBuffer) return "[BINARY]";
	if (e instanceof Date) {
		let t = Date.prototype.getTime.call(e);
		return Number.isNaN(t) ? "[INVALID_DATE]" : Date.prototype.toISOString.call(e);
	}
	if (Array.isArray(e)) {
		if (r >= p) return f;
		if (n.has(e)) return "[CIRCULAR]";
		n.add(e);
		let i = e.slice(0, h).map((e) => b(e, t, n, r + 1));
		return e.length > i.length && i.push(f), n.delete(e), Object.freeze(i);
	}
	return C(e) ? y(e, n, r) : "[UNSERIALIZABLE]";
}
function te(e, t) {
	let n = e.length > g ? `${e.slice(0, g)}${f}` : e;
	if (t || /^https?:\/\//i.test(n)) try {
		let e = new URL(n);
		e.username = "", e.password = "";
		for (let t of [...e.searchParams.keys()]) D(t) && e.searchParams.set(t, d);
		return e.hash = "", e.toString();
	} catch {
		if (t) return d;
	}
	return n.replace(/\bBearer\s+[^\s,;]+/gi, `Bearer ${d}`).replace(/\b(access_token|refresh_token|api[-_]?key|password|passwd|secret|token|cursor|resume[-_]?token)\s*[:=]\s*([^\s,;&]+)/gi, (e, t) => `${t}=${d}`);
}
function x(e) {
	if (e !== void 0) return /^[A-Za-z0-9][A-Za-z0-9._:/-]{0,255}$/.test(e) ? e : d;
}
function S(e) {
	return typeof e == "string" ? e : void 0;
}
function ne(t) {
	if (t !== void 0) try {
		if (i(t)) {
			let n = w(t, "sdkCode");
			if (typeof n != "string" || !l(n)) return { name: "Error" };
			let [r, i, a] = e[n];
			return {
				name: T(w(t, "name")),
				domain: r,
				code: n,
				category: i,
				retryable: a
			};
		}
		return t instanceof Error ? { name: E(t) } : { name: typeof t };
	} catch {
		return { name: "Error" };
	}
}
function C(e) {
	return typeof e == "object" && !!e;
}
function w(e, t) {
	let n = Object.getOwnPropertyDescriptor(e, t);
	return n && "value" in n ? n.value : void 0;
}
function T(e) {
	return typeof e == "string" && re.has(e) ? e : "Error";
}
function E(e) {
	try {
		let t = w(e, "name");
		if (typeof t == "string") return T(t);
		let n = Object.getPrototypeOf(e);
		return n ? T(w(n, "name")) : "Error";
	} catch {
		return "Error";
	}
}
function D(e) {
	let t = e.toLocaleLowerCase("en-US").replace(/[^a-z0-9]+/g, "");
	return t === "key" || /(?:accesstoken|refreshtoken|securitytoken|accesskeyid|accessid|credential|authorization|apikey|signature|password|passwd|secret|cursor|resumetoken|continuationtoken|filter|where|query|sql|cql|auth|sig)$/.test(t);
}
var O = /^(?:__proto__|prototype|constructor)$/, re = /* @__PURE__ */ new Set(/* @__PURE__ */ "AbortError.AggregateError.ConnectError.DOMException.Error.EvalError.HonuaAbortError.HonuaAgentExecutionError.HonuaAgentSafetyError.HonuaAgentToolError.HonuaAuthError.HonuaAutomaticMapLibreIntegrationError.HonuaAutomaticMapLibreStrategyError.HonuaCapabilityNotSupportedError.HonuaDataToMapBridgeError.HonuaDiscoveryError.HonuaExplorationContextError.HonuaGeneratedAppError.HonuaGrpcError.HonuaHttpError.HonuaJobFailedError.HonuaMapLibreRasterStrategyError.HonuaMapLibreSourceAdapterError.HonuaMapPackageError.HonuaNetworkError.HonuaOfflineRegionError.HonuaOfflineEditQueueError.HonuaPluginRegistryError.HonuaQueryPlanExecutionError.HonuaQueryPlanningError.HonuaRealtimeResumeError.HonuaReplicaSyncError.HonuaRuntimeDiagnosticError.HonuaSdkError.HonuaStacConformanceError.HonuaTemporalPlaybackError.HonuaTimeoutError.HonuaWfsExceptionError.HonuaWfsProtocolError.HonuaWmsCapabilitiesParseError.HonuaWmtsCapabilitiesParseError.NetworkError.QueryTileServerResponseError.RangeError.ReferenceError.SyntaxError.TimeoutError.TypeError.URIError".split("."));
function k() {
	return Object.freeze(Object.create(null));
}
function A(e, t) {
	let n = Object.create(null);
	return n[e] = t, Object.freeze(n);
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/errors.js
var ie = class extends r {
	code;
	detail;
	constructor(e, t, n, r = {}) {
		super(`core.geometry.${e}`, t, {
			...r,
			context: c(n, r.context)
		}), this.code = e, this.detail = n, this.name = "HonuaGeometryError";
	}
}, j = class extends r {
	statusCode;
	body;
	receipt;
	constructor(e, t, n, r = {}) {
		let i = V(e, n, r);
		super(i.retryable ? "core.http.transient" : "core.http.rejected", `HTTP ${e}: ${t}`, {
			...r,
			requestId: r.requestId ?? i.correlationId,
			terminalReceipt: i,
			context: c(r.context, { statusCode: e })
		}), this.name = "HonuaHttpError", this.statusCode = e, this.body = n, this.receipt = i;
	}
}, M = class extends r {
	timeoutMs;
	constructor(e, t = {}) {
		super("core.timeout", `Request timed out after ${e}ms`, {
			...t,
			context: c(t.context, { timeoutMs: e })
		}), this.name = "HonuaTimeoutError", this.timeoutMs = e;
	}
}, N = class extends r {
	cause;
	constructor(e, t, n = {}) {
		super("core.network", e, {
			...n,
			cause: t
		}), this.name = "HonuaNetworkError", this.cause = t;
	}
}, P = class extends r {
	constructor(e = "Request was aborted", t = {}) {
		super("core.cancelled", e, t), this.name = "HonuaAbortError";
	}
}, F = class extends r {
	capability;
	protocol;
	sourceId;
	constructor(e, t, n, r = {}) {
		let i = n ? `Capability "${e}" is not supported by protocol "${t}" on source "${n}"` : `Capability "${e}" is not supported by protocol "${t}"`;
		super("core.capability-not-supported", i, {
			...r,
			context: c(r.context, {
				capability: e,
				protocol: t,
				sourceId: n
			})
		}), this.name = "HonuaCapabilityNotSupportedError", this.capability = e, this.protocol = t, this.sourceId = n;
	}
}, I = class extends r {
	code;
	cause;
	constructor(e, t, n = {}) {
		super(B[e], t, n), this.code = e, this.name = "HonuaAuthError", this.cause = n.cause;
	}
}, L = class extends r {
	code;
	constructor(e, t, n = {}) {
		super("core.exploration-context", t, {
			...n,
			context: c(n.context, { reason: e })
		}), this.code = e, this.name = "HonuaExplorationContextError";
	}
}, R = class extends r {
	exceptionCode;
	locator;
	constructor(e, t, n, r = {}) {
		let i = n ? `WFS ExceptionReport ${e} (${n}): ${t}` : `WFS ExceptionReport ${e}: ${t}`;
		super("core.wfs-exception", i, {
			...r,
			context: c(r.context, {
				exceptionCode: e,
				locator: n
			})
		}), this.name = "HonuaWfsExceptionError", this.exceptionCode = e, this.locator = n;
	}
}, z = /* @__PURE__ */ new Set([
	408,
	429,
	500,
	502,
	503,
	504
]), B = {
	interaction_required: "core.auth.interaction-required",
	refresh_failed: "core.auth.refresh-failed",
	invalid_grant: "core.auth.invalid-grant"
};
function V(e, t, n) {
	let r = Q(t) ? t : void 0, i = r && Q(r.error) ? r.error : void 0, a = i ?? r, o = n.protocolCode ?? ce(i?.code), s = typeof o == "number" ? o : e, c = W(n.responseHeaders), l = Y(a?.kind), u = le(l) ? l : H(s, e), d = Y(a?.machineCode) ?? Y(a?.code) ?? Y(r?.code) ?? U(u), f = X(a?.retryable) ?? X(r?.retryable), p = Z(a?.retryAfterSeconds) ?? Z(r?.retryAfterSeconds), m = J(G(n.responseHeaders)), h = Y(a?.correlationId) ?? Y(r?.correlationId) ?? q(c, "x-correlation-id", "honua-request-id", "x-request-id", "honua-correlation-id"), g = ae(a?.errors ?? r?.errors);
	return oe({
		transportStatus: n.transportStatus ?? e,
		...o === void 0 ? {} : { protocolCode: o },
		kind: u,
		...d ? { code: d } : {},
		retryable: f ?? (z.has(s) || z.has(n.transportStatus ?? e)),
		...p === void 0 ? m === void 0 ? {} : { retryAfterMs: m } : { retryAfterMs: Math.max(0, p * 1e3) },
		...h ? { correlationId: h } : {},
		fieldErrors: g,
		protocolMetadata: {
			initial: c,
			trailing: se()
		}
	});
}
function H(e, t) {
	return e === 401 || e === 498 || e === 499 ? "authentication" : e === 403 ? "authorization" : e === 404 ? "not-found" : e === 400 || e === 422 ? "validation" : e === 409 || e === 412 || e === 428 ? "conflict" : e === 429 ? "throttled" : z.has(e) || t >= 500 ? "unavailable" : "unknown";
}
function U(e) {
	return {
		authentication: "authentication_required",
		authorization: "permission_denied",
		"not-found": "resource_not_found",
		validation: "validation_failed",
		conflict: "resource_conflict",
		throttled: "rate_limit_exceeded",
		unavailable: "service_unavailable",
		unknown: "unknown_failure"
	}[e];
}
function W(e) {
	let t = Object.create(null), n = (e, n) => {
		let r = e.toLowerCase();
		if (ue.has(r)) return;
		let i = Array.isArray(n) ? n.filter((e) => typeof e == "string") : [String(n)];
		i.length > 0 && (t[r] = Object.freeze(i.slice(0, 20)));
	};
	if (e && typeof e.entries == "function") for (let [t, r] of e.entries()) n(t, r);
	else if (Q(e)) for (let [t, r] of Object.entries(e)) n(t, r);
	return Object.freeze(t);
}
function G(e) {
	return e && typeof e.get == "function" ? e : K(W(e));
}
function K(...e) {
	return { get(t) {
		for (let n of e) {
			let e = q(n, t);
			if (e) return e;
		}
		return null;
	} };
}
function q(e, ...t) {
	for (let n of t) {
		let t = e[n.toLowerCase()]?.[0];
		if (t) return t;
	}
}
function J(e) {
	let t = e.get("retry-after");
	if (!t) return;
	let n = Number(t);
	if (Number.isFinite(n) && n >= 0) return n * 1e3;
	let r = Date.parse(t);
	return Number.isFinite(r) ? Math.max(0, r - Date.now()) : void 0;
}
function ae(e) {
	return Object.freeze(Array.isArray(e) ? e.filter(Q).map((e) => {
		let t = Y(e.code), n = Y(e.severity), r = Y(e.path), i = Y(e.fieldId), a = Z(e.itemIndex), o = Y(e.message);
		return Object.freeze({
			...t ? { code: t } : {},
			...n ? { severity: n } : {},
			...r ? { path: r } : {},
			...i ? { fieldId: i } : {},
			...a === void 0 ? {} : { itemIndex: a },
			...o ? { message: o } : {}
		});
	}) : []);
}
function oe(e) {
	return Object.freeze({
		...e,
		fieldErrors: Object.freeze([...e.fieldErrors]),
		protocolMetadata: Object.freeze({
			initial: e.protocolMetadata.initial,
			trailing: e.protocolMetadata.trailing
		})
	});
}
function se() {
	return Object.freeze(Object.create(null));
}
function ce(e) {
	return typeof e == "number" || typeof e == "string" ? e : void 0;
}
function Y(e) {
	return typeof e == "string" && e.length > 0 ? e : void 0;
}
function X(e) {
	return typeof e == "boolean" ? e : void 0;
}
function Z(e) {
	return typeof e == "number" && Number.isFinite(e) ? e : void 0;
}
function le(e) {
	return e !== void 0 && $.has(e);
}
function Q(e) {
	return typeof e == "object" && !!e && !Array.isArray(e);
}
var $ = /* @__PURE__ */ new Set([
	"authentication",
	"authorization",
	"not-found",
	"validation",
	"conflict",
	"throttled",
	"unavailable",
	"unknown"
]), ue = /* @__PURE__ */ new Set([
	"authorization",
	"cookie",
	"set-cookie",
	"x-api-key"
]);
//#endregion
export { ie as a, M as c, c as d, L as i, R as l, I as n, j as o, F as r, N as s, P as t, r as u };
