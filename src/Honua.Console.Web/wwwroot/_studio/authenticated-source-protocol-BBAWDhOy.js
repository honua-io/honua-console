import { t as e } from "./rolldown-runtime-Dy4uBu1J.js";
import { a as t, c as n, d as r, l as i, o as a, r as o, s, t as c, u as l } from "./errors-VvMCLBue.js";
function u(e) {
	return {
		scope: e.scope,
		status: e.status,
		keyFingerprint: e.keyFingerprint,
		...e.ageMs === void 0 ? {} : { ageMs: Math.max(0, Math.trunc(e.ageMs)) },
		...e.ttlMs === void 0 ? {} : { ttlMs: Math.max(0, Math.trunc(e.ttlMs)) },
		...e.staleIfErrorMs === void 0 ? {} : { staleIfErrorMs: Math.max(0, Math.trunc(e.staleIfErrorMs)) },
		...e.revalidatedAt ? { revalidatedAt: e.revalidatedAt } : {},
		...e.sourceUpdatedAt ? { sourceUpdatedAt: e.sourceUpdatedAt } : {},
		...te(e.validator) ? { validator: e.validator } : {},
		...e.invalidationReason ? { invalidationReason: e.invalidationReason } : {},
		...e.refreshErrorId ? { refreshErrorId: e.refreshErrorId } : {}
	};
}
function d(e = {}) {
	let t = g(e.ttlMs) ?? 3e5, n = g(e.staleIfErrorMs) ?? 36e5, r = ne(e.maxResponseBytes);
	return {
		...e.signal ? { signal: e.signal } : {},
		refresh: e.refresh === !0,
		cache: e.cache === "bypass" ? "bypass" : "default",
		staleIfError: e.staleIfError !== !1,
		ttlMs: t,
		staleIfErrorMs: n,
		...r === void 0 ? {} : { maxResponseBytes: r }
	};
}
function f(e, t, n) {
	return n === void 0 || Math.max(0, t - e) < n;
}
function p(e) {
	let t = e.get("etag") ?? void 0, n = e.get("last-modified") ?? void 0;
	return te({
		etag: t,
		lastModified: n
	}) ? {
		...t ? { etag: t } : {},
		...n ? { lastModified: n } : {}
	} : void 0;
}
function m(e) {
	let t = { Accept: e.accept ?? "application/json" };
	return e.bypass ? (t["Cache-Control"] = "no-store", t.Pragma = "no-cache", t) : (e.refresh && (t["Cache-Control"] = "no-cache", e.validator?.etag && (t["If-None-Match"] = e.validator.etag), e.validator?.lastModified && (t["If-Modified-Since"] = e.validator.lastModified)), t);
}
function h(e, t) {
	return typeof e != "object" || !e || Array.isArray(e) ? e : {
		...e,
		cache: t
	};
}
function ee(e) {
	if (typeof e != "object" || !e || Array.isArray(e)) return e;
	let { cache: t, ...n } = e;
	return n;
}
function te(e) {
	return !!(e?.etag || e?.lastModified);
}
function g(e) {
	if (!(typeof e != "number" || !Number.isFinite(e))) return Math.max(0, Math.trunc(e));
}
function ne(e) {
	if (e !== void 0) {
		if (!Number.isSafeInteger(e) || e <= 0) throw TypeError("maxResponseBytes must be a positive safe integer.");
		return e;
	}
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/odata-key-path.js
function re(e) {
	let t = String(e), n = "", r = !1;
	for (let e of t) {
		if (e === "'") {
			r = !r, n += "'";
			continue;
		}
		if (!r && (e === "," || e === "=")) {
			n += e;
			continue;
		}
		n += /[A-Za-z0-9\-._~]/.test(e) ? e : encodeURIComponent(e);
	}
	return n;
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/path-utils.js
function ie(e) {
	let t = e.length;
	for (; t > 0 && e.charCodeAt(t - 1) === 47;) --t;
	return t === e.length ? e : e.slice(0, t);
}
function ae(e) {
	let t = e.indexOf("?");
	return t < 0 ? e : e.slice(0, t);
}
function _(e) {
	return e.includes("/") ? e.split("/").filter((e) => e.length > 0).map((e) => encodeURIComponent(e)).join("/") : encodeURIComponent(e);
}
function oe(e) {
	return e.includes("/") ? e.split("/").map((e) => encodeURIComponent(e)).join("/") : encodeURIComponent(e);
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/odata.js
var se = 4 * 1024 * 1024, ce = Symbol("honua.odata.source-schema-projection-safety"), le = Symbol("honua.odata.source-schema-projection-details"), ue = class {
	client;
	entitySet;
	entitySetName;
	basePath;
	cachedMetadata;
	inflightMetadata;
	constructor(e) {
		this.client = e.client, this.entitySet = e.entitySet, this.entitySetName = me(e.entitySet), this.basePath = pe(e.basePath);
	}
	async metadata(e = {}) {
		let t = d(e), n = t.cache === "bypass", r = Date.now(), i = this.cachedMetadata ? f(this.cachedMetadata.cachedAtMs, r, t.ttlMs) : !1;
		if (!n && !t.refresh && this.cachedMetadata && i) return de(this.cachedMetadata, "hit", {
			now: r,
			ttlMs: t.ttlMs,
			staleIfErrorMs: t.staleIfErrorMs
		});
		if (!n && !t.refresh && this.inflightMetadata) return this.inflightMetadata;
		let a = this.cachedMetadata, o = this.fetchMetadata(t, a).then((e) => {
			n || (this.cachedMetadata = e);
			let r = n ? "bypass" : e.status;
			return de(e, r, {
				now: Date.now(),
				ttlMs: t.ttlMs,
				staleIfErrorMs: t.staleIfErrorMs,
				...r === "refreshed" ? { revalidatedAt: (/* @__PURE__ */ new Date()).toISOString() } : {}
			});
		}), s = (e) => {
			if (!n && t.staleIfError && a) return de(a, "stale", {
				now: Date.now(),
				ttlMs: t.ttlMs,
				staleIfErrorMs: t.staleIfErrorMs,
				refreshErrorId: fe(e)
			});
			throw e;
		};
		if (!n && !t.refresh) {
			let e = o.catch(s).finally(() => {
				this.inflightMetadata === e && (this.inflightMetadata = void 0);
			});
			return this.inflightMetadata = e, e;
		}
		try {
			return await o;
		} catch (e) {
			return s(e);
		}
	}
	async query(e = {}) {
		let t = this.entitySetPath(), n = _e(e), r = await this.requestJson("GET", t, n, e.signal);
		return {
			rows: r.value ?? [],
			...typeof r["@odata.count"] == "number" ? { totalCount: r["@odata.count"] } : {},
			...typeof r["@odata.nextLink"] == "string" ? { nextLink: r["@odata.nextLink"] } : {},
			...typeof r["@odata.deltaLink"] == "string" ? { deltaLink: r["@odata.deltaLink"] } : {}
		};
	}
	async queryAll(e = {}) {
		let t = typeof e.top == "number" && e.top >= 0 ? e.top : void 0, n = [], r, i, a = e, o = /* @__PURE__ */ new Set();
		for (;;) {
			let s = i ? await this.followNextLink(i, e.signal) : await this.query(a ?? {});
			a = void 0, r === void 0 && typeof s.totalCount == "number" && (r = s.totalCount);
			for (let e of s.rows) if (t !== void 0 && n.length >= t || (n.push(e), t !== void 0 && n.length >= t)) break;
			if (t !== void 0 && n.length >= t || !s.nextLink) break;
			this.checkNextLink(s.nextLink, o), i = s.nextLink;
		}
		return r === void 0 ? { rows: n } : {
			rows: n,
			totalCount: r
		};
	}
	async *queryStream(e = {}) {
		let t = await this.query(e);
		yield t;
		let n = /* @__PURE__ */ new Set();
		for (; t.nextLink;) this.checkNextLink(t.nextLink, n), t = await this.followNextLink(t.nextLink, e.signal), yield t;
	}
	async add(e, t = {}) {
		let n = this.entitySetPath();
		return this.requestJson("POST", n, void 0, t.signal, JSON.stringify(e), { "Content-Type": t.contentType ?? "application/json" });
	}
	async update(e, t, n = {}) {
		let r = `${this.entitySetPath()}(${re(e)})`;
		return this.requestJson("PATCH", r, void 0, n.signal, JSON.stringify(t), { "Content-Type": n.contentType ?? "application/json" });
	}
	async delete(e, t = {}) {
		let n = `${this.entitySetPath()}(${re(e)})`;
		await this.requestJson("DELETE", n, void 0, t.signal);
	}
	async batch(e, t = {}) {
		if (e.length === 0) return { responses: [] };
		let n = t.atomicity === "all" ? "g1" : void 0, r = e.map((e, t) => ({
			id: e.id ?? String(t + 1),
			method: e.method,
			url: ge(e.url),
			...e.headers ? { headers: {
				"Content-Type": "application/json",
				...e.headers
			} } : { headers: { "Content-Type": "application/json" } },
			...e.body === void 0 ? {} : { body: e.body },
			...n === void 0 ? {} : { atomicityGroup: n }
		}));
		return { responses: (await this.requestJson("POST", `${this.basePath}/$batch`, void 0, t.signal, JSON.stringify({ requests: r }), { "Content-Type": "application/json" })).responses ?? [] };
	}
	async apply(e, t = {}) {
		let n = this.entitySetPath(), r = _e({
			apply: e,
			filter: t.filter
		}), i = await this.requestJson("GET", n, r, t.signal);
		return {
			rows: i.value ?? [],
			...typeof i["@odata.count"] == "number" ? { totalCount: i["@odata.count"] } : {}
		};
	}
	async search(e, t = {}) {
		return this.query({
			...t,
			search: e
		});
	}
	async *delta(e = {}) {
		let t = e.since ? { deltatoken: e.since } : {};
		e.signal && (t.signal = e.signal);
		let n = await this.query(t);
		yield {
			rows: n.rows,
			...n.nextLink ? { nextLink: n.nextLink } : {},
			...n.deltaLink ? { deltaLink: n.deltaLink } : {}
		};
		let r = /* @__PURE__ */ new Set();
		for (; n.nextLink;) this.checkNextLink(n.nextLink, r), n = await this.followNextLink(n.nextLink, e.signal), yield {
			rows: n.rows,
			...n.nextLink ? { nextLink: n.nextLink } : {},
			...n.deltaLink ? { deltaLink: n.deltaLink } : {}
		};
	}
	async raw(e, t, n = {}) {
		let r = e.toUpperCase();
		return this.client.pipelineFetch(r, he(t), n);
	}
	entitySetPath() {
		return `${this.basePath}/${this.entitySet}`;
	}
	async fetchMetadata(e, t) {
		let n = await this.client.pipelineFetch("GET", `${this.basePath}/$metadata`, { headers: m({
			accept: "application/xml",
			refresh: e.refresh || !!t,
			bypass: e.cache === "bypass",
			validator: t?.validator
		}) }, e.signal, { okStatuses: [304] });
		if (n.status === 304 && t) {
			let e = p(n.headers) ?? t.validator;
			return {
				...t,
				cachedAtMs: Date.now(),
				...e ? { validator: e } : {},
				status: "refreshed"
			};
		}
		let r = await n.text(), i = p(n.headers), a = n.headers.get("last-modified") ?? void 0;
		return {
			metadata: ee(be(r)),
			cachedAtMs: Date.now(),
			keyFingerprint: `metadata:odata:${this.basePath}:$metadata`,
			status: e.cache === "bypass" ? "bypass" : t ? "refreshed" : "miss",
			...i ? { validator: i } : {},
			...a ? { sourceUpdatedAt: a } : {}
		};
	}
	checkNextLink(e, t) {
		let { path: n, query: r } = ve(e, this.client.serverBaseUrl, this.basePath), i = JSON.stringify([n, Object.entries(r ?? {}).sort(([e], [t]) => e.localeCompare(t))]);
		if (t.has(i)) throw new a(200, "OData paging returned a repeated nextLink.", { reason: "repeated-next-link" });
		t.add(i);
	}
	async followNextLink(e, t) {
		let { path: n, query: r } = ve(e, this.client.serverBaseUrl, this.basePath), i = await this.requestJson("GET", n, r, t);
		return {
			rows: i.value ?? [],
			...typeof i["@odata.count"] == "number" ? { totalCount: i["@odata.count"] } : {},
			...typeof i["@odata.nextLink"] == "string" ? { nextLink: i["@odata.nextLink"] } : {},
			...typeof i["@odata.deltaLink"] == "string" ? { deltaLink: i["@odata.deltaLink"] } : {}
		};
	}
	async requestJson(e, t, n, r, i, a) {
		let o = new URLSearchParams();
		if (n) for (let [e, t] of Object.entries(n)) o.set(e, String(t));
		let s = o.size > 0 ? `${t}?${o.toString()}` : t;
		return this.client.pipelineRequestJson(e, s, {
			...a ? { headers: a } : {},
			...i === void 0 ? {} : { body: i }
		}, r);
	}
};
function de(e, t, n) {
	return h(e.metadata, u({
		scope: "metadata",
		status: t,
		keyFingerprint: e.keyFingerprint,
		ageMs: Math.max(0, n.now - e.cachedAtMs),
		...n.ttlMs === void 0 ? {} : { ttlMs: n.ttlMs },
		...n.staleIfErrorMs === void 0 ? {} : { staleIfErrorMs: n.staleIfErrorMs },
		...n.revalidatedAt ? { revalidatedAt: n.revalidatedAt } : {},
		...e.sourceUpdatedAt ? { sourceUpdatedAt: e.sourceUpdatedAt } : {},
		...e.validator ? { validator: e.validator } : {},
		...n.refreshErrorId ? { refreshErrorId: n.refreshErrorId } : {}
	}));
}
function fe(e) {
	return e instanceof Error && e.name ? e.name : "unknown";
}
function pe(e) {
	return e === void 0 || e === "" ? "/odata" : e.startsWith("/") ? ie(e) || "/" : `/${ie(e)}`;
}
function me(e) {
	let t = e.lastIndexOf("/");
	return t === -1 ? e : e.slice(t + 1);
}
function he(e) {
	return e.startsWith("/") ? e : `/${e}`;
}
function ge(e) {
	return e.startsWith("/") ? e.slice(1) : e;
}
function _e(e) {
	let t = {};
	return e.filter !== void 0 && e.filter !== "" && (t.$filter = e.filter), e.select && e.select.length > 0 && (t.$select = e.select.join(",")), e.orderBy && e.orderBy.length > 0 && (t.$orderby = e.orderBy.join(",")), typeof e.top == "number" && Number.isFinite(e.top) && (t.$top = e.top), typeof e.skip == "number" && Number.isFinite(e.skip) && e.skip > 0 && (t.$skip = e.skip), e.expand && e.expand.length > 0 && (t.$expand = e.expand.join(",")), e.count && (t.$count = "true"), e.skiptoken !== void 0 && e.skiptoken !== "" && (t.$skiptoken = e.skiptoken), e.search !== void 0 && e.search !== "" && (t.$search = e.search), e.apply !== void 0 && e.apply !== "" && (t.$apply = e.apply), e.deltatoken !== void 0 && e.deltatoken !== "" && (t.$deltatoken = e.deltatoken), e.extra && Object.assign(t, e.extra), t;
}
function ve(e, t, n) {
	let r = e.startsWith("http://") || e.startsWith("https://") ? new URL(e) : new URL(e, `${ye(t)}${ge(n)}/`), i = ie(new URL(t).pathname), a = i && r.pathname.startsWith(`${i}/`) ? r.pathname.slice(i.length) : r.pathname, o = {};
	for (let [e, t] of r.searchParams) o[e] = t;
	return {
		path: a,
		query: o
	};
}
function ye(e) {
	return e.endsWith("/") ? e : `${e}/`;
}
function be(e) {
	if (e.length > se) throw RangeError(`OData metadata exceeds ${se} characters`);
	let t = Object.create(null), n = Object.create(null), r = Object.create(null), i = Object.create(null), a = Object.create(null), o = Object.create(null), s = Object.create(null), c = Object.create(null), l = /* @__PURE__ */ new Map();
	for (let t of y(e, "Annotations")) {
		let e = t.attributes, n = v(e, "Target");
		if (!n) continue;
		let r = n.lastIndexOf("/");
		if (r === -1) continue;
		let i = n.slice(r + 1);
		if (!i) continue;
		let a = t.body, o = l.get(i);
		l.set(i, o === void 0 ? a : `${o}\n${a}`);
	}
	for (let n of y(e, "EntitySet")) {
		let e = n.attributes, r = v(e, "Name"), i = v(e, "EntityType");
		if (!r || !i) continue;
		t[r] = Me(i);
		let a = n.body;
		c[r] = Ee(`${a}\n${l.get(r) ?? ""}`), l.delete(r);
	}
	for (let [e, t] of l) c[e] = Ee(t);
	for (let t of y(e, "EntityType")) {
		let e = t.attributes, a = t.body, o = v(e, "Name");
		if (!o) continue;
		v(e, "OpenType")?.toLowerCase() === "true" && (s[o] = !0);
		let c = [], l = y(a, "Key").next();
		if (!l.done) for (let e of De(l.value.body, "PropertyRef")) {
			let t = v(e, "Name");
			t && c.push(t);
		}
		n[o] = c;
		let u = we(a);
		i[o] = u, r[o] = u.map(Te);
	}
	for (let t of y(e, "ComplexType")) {
		let e = v(t.attributes, "Name");
		e && (a[e] = we(t.body));
	}
	for (let t of y(e, "EnumType")) {
		let e = t.attributes, n = v(e, "Name");
		if (!n) continue;
		let r = v(e, "UnderlyingType") ?? "Edm.Int32", i = v(e, "IsFlags")?.toLowerCase() === "true", a = [...De(t.body, "Member")].map((e) => ({
			name: v(e, "Name"),
			rawValue: v(e, "Value")
		})), s = a.some((e) => e.rawValue !== void 0), c = a.some((e) => e.rawValue === void 0), l = s ? c ? "mixed" : "explicit" : "implicit", u, d = (e) => {
			u ??= e;
		}, f = Se(r);
		a.length === 0 && d("empty-declaration"), f || d("invalid-underlying-type"), i && c && d("flags-require-explicit-values");
		let p = [], m = /* @__PURE__ */ new Set(), h;
		for (let [e, t] of a.entries()) {
			let n;
			if (t.rawValue === void 0) i || (n = e === 0 ? 0n : h === void 0 ? void 0 : h + 1n);
			else if (t.rawValue.length <= 20 && /^[+-]?\d+$/.test(t.rawValue)) try {
				n = BigInt(t.rawValue);
			} catch {
				n = void 0;
			}
			if (n === void 0 ? d("invalid-member-value") : i && n < 0n ? d("negative-flags-value") : f && (n < f.minimum || n > f.maximum) && d("out-of-range"), h = n, !t.name || !Ce(t.name)) {
				d("invalid-member-name");
				continue;
			}
			if (m.has(t.name) && d("duplicate-member-name"), m.add(t.name), n === void 0) continue;
			let a = r === "Edm.Int64" || !Number.isSafeInteger(Number(n)) ? n.toString() : Number(n);
			p.push({
				name: t.name,
				value: a
			});
		}
		o[n] = {
			underlyingType: r,
			isFlags: i,
			members: p,
			declaration: u ? {
				state: "invalid",
				reason: u
			} : {
				state: "valid",
				valueMode: l
			}
		};
	}
	let u = {
		entitySets: t,
		keys: n,
		fields: r,
		capabilities: c
	};
	return Object.defineProperty(u, le, {
		value: {
			fields: i,
			complexTypes: a,
			enumTypes: o,
			openTypes: s
		},
		enumerable: !0
	}), Object.defineProperty(u, ce, {
		value: xe(e),
		enumerable: !0
	}), u;
}
function xe(e) {
	let t = Oe(e, "Edmx"), n = t === void 0 ? void 0 : v(t, "Version"), r = /* @__PURE__ */ new Map(), i = /* @__PURE__ */ new Set(), a = /* @__PURE__ */ new Set(), o = /* @__PURE__ */ new Set();
	for (let t of y(e, "Schema")) {
		let e = v(t.attributes, "Namespace");
		for (let n of [
			"EntityType",
			"ComplexType",
			"EnumType"
		]) for (let s of De(t.body, n)) {
			let t = v(s, "Name");
			if (!t) continue;
			let c = `${n}:${e ? `${e}.${t}` : t}`, l = r.get(t) ?? /* @__PURE__ */ new Map();
			l.set(c, (l.get(c) ?? 0) + 1), r.set(t, l), e || o.add(t), n !== "EnumType" && v(s, "BaseType") && i.add(t), n === "ComplexType" && v(s, "OpenType")?.toLowerCase() === "true" && a.add(t);
		}
	}
	let s = [...r].filter(([, e]) => e.size > 1 || [...e.values()].some((e) => e > 1)).map(([e]) => e).sort();
	return Object.freeze({
		...n === void 0 ? {} : { csdlVersion: n },
		ambiguousTypeNames: Object.freeze(s),
		inheritedTypeNames: Object.freeze([...i].sort()),
		openComplexTypeNames: Object.freeze([...a].sort()),
		unqualifiedTypeNames: Object.freeze([...o].sort())
	});
}
function Se(e) {
	switch (e) {
		case "Edm.Byte": return {
			minimum: 0n,
			maximum: 255n
		};
		case "Edm.SByte": return {
			minimum: -128n,
			maximum: 127n
		};
		case "Edm.Int16": return {
			minimum: -32768n,
			maximum: 32767n
		};
		case "Edm.Int32": return {
			minimum: -2147483648n,
			maximum: 2147483647n
		};
		case "Edm.Int64": return {
			minimum: -(1n << 63n),
			maximum: (1n << 63n) - 1n
		};
		default: return;
	}
}
function Ce(e) {
	if (e.length > 256) return !1;
	let t = 0;
	for (let n of e) if (t += 1, t > 128) return !1;
	return /^[\p{L}\p{Nl}_][\p{L}\p{Nl}\p{Nd}\p{Mn}\p{Mc}\p{Pc}\p{Cf}]*$/u.test(e);
}
function we(e) {
	let t = [];
	for (let n of De(e, "Property")) {
		let e = v(n, "Name"), r = v(n, "Type");
		if (!e || !r) continue;
		let i = v(n, "Nullable"), a = v(n, "MaxLength"), o = v(n, "Precision"), s = v(n, "Scale"), c = v(n, "SRID"), l = r.startsWith("Edm.Geography") || r.startsWith("Edm.Geometry"), u = {
			name: e,
			type: r
		};
		if (i !== void 0 && (u.nullable = i !== "false"), a?.toLowerCase() === "max") u.maxLength = "max";
		else if (a !== void 0) {
			let e = Number(a);
			Number.isSafeInteger(e) && e >= 0 && (u.maxLength = e);
		}
		if (o !== void 0) {
			let e = Number(o);
			Number.isSafeInteger(e) && e >= 0 && (u.precision = e);
		}
		if (s?.toLowerCase() === "variable" || s?.toLowerCase() === "floating") u.scale = s.toLowerCase();
		else if (s !== void 0) {
			let e = Number(s);
			Number.isSafeInteger(e) && e >= 0 && (u.scale = e);
		}
		if (l && (u.isSpatial = !0), c?.toLowerCase() === "variable") u.srid = "variable";
		else if (c !== void 0) {
			let e = Number(c);
			Number.isSafeInteger(e) && e >= 0 && (u.srid = e);
		}
		t.push(u);
	}
	return t;
}
function Te(e) {
	return {
		name: e.name,
		type: e.type,
		...e.nullable === void 0 ? {} : { nullable: e.nullable },
		...e.isSpatial === !0 ? { isSpatial: !0 } : {},
		...typeof e.srid == "number" ? { srid: e.srid } : {}
	};
}
function Ee(e) {
	let t = {};
	for (let n of y(e, "Annotation")) {
		let e = n.attributes, r = v(e, "Term");
		if (!r || !r.includes("Capabilities.")) continue;
		let i = n.body.trim(), a = v(e, "Bool") ?? Ne(i) ?? x(i, "Insertable") ?? x(i, "Updatable") ?? x(i, "Deletable") ?? x(i, "Searchable") ?? x(i, "Filterable") ?? x(i, "Expandable") ?? x(i, "Selectable") ?? x(i, "Countable") ?? x(i, "Supported");
		r.endsWith("InsertRestrictions") ? t.insert = x(i, "Insertable") !== "false" : r.endsWith("UpdateRestrictions") ? t.update = x(i, "Updatable") !== "false" : r.endsWith("DeleteRestrictions") ? t.delete = x(i, "Deletable") !== "false" : r.endsWith("BatchSupported") || r.endsWith("BatchSupport") ? t.batch = a !== "false" : r.endsWith("FilterRestrictions") || r.endsWith("FilterFunctions") ? t.filter = a !== "false" : r.endsWith("ExpandRestrictions") ? t.expand = a !== "false" : r.endsWith("SelectSupport") ? t.select = a !== "false" : r.endsWith("SearchRestrictions") ? t.search = a !== "false" : r.endsWith("CountRestrictions") ? t.count = a !== "false" : r.endsWith("ChangeTracking") ? t.delta = a !== "false" : (r.endsWith("ApplySupported") || r.endsWith("ApplyRestrictions")) && (t.apply = a !== "false");
	}
	return t;
}
function v(e, t) {
	let n = 0;
	for (; n < e.length;) {
		for (; n < e.length && b(e.charCodeAt(n));) n += 1;
		if (n >= e.length || e.charCodeAt(n) === 47) return;
		let r = n;
		for (; n < e.length;) {
			let t = e.charCodeAt(n);
			if (b(t) || t === 61 || t === 47 || t === 62) break;
			n += 1;
		}
		let i = e.slice(r, n);
		for (; n < e.length && b(e.charCodeAt(n));) n += 1;
		if (e.charCodeAt(n) !== 61) {
			n += 1;
			continue;
		}
		for (n += 1; n < e.length && b(e.charCodeAt(n));) n += 1;
		let a = e.charCodeAt(n);
		if (a !== 34 && a !== 39) {
			n += 1;
			continue;
		}
		let o = ++n;
		for (; n < e.length && e.charCodeAt(n) !== a;) n += 1;
		if (n >= e.length) return;
		if (i === t) return e.slice(o, n);
		n += 1;
	}
}
function* y(e, t) {
	let n = `<${t}`, r = `</${t}>`, i = 0;
	for (; i < e.length;) {
		let t = ke(e, n, i);
		if (t === -1) return;
		let a = Ae(e, t + n.length);
		if (a === -1) return;
		let o = e.slice(t + n.length, a);
		if (je(o)) {
			yield {
				attributes: o,
				body: ""
			}, i = a + 1;
			continue;
		}
		let s = e.indexOf(r, a + 1);
		if (s === -1) return;
		yield {
			attributes: o,
			body: e.slice(a + 1, s)
		}, i = s + r.length;
	}
}
function* De(e, t) {
	let n = `<${t}`, r = 0;
	for (; r < e.length;) {
		let t = ke(e, n, r);
		if (t === -1) return;
		let i = Ae(e, t + n.length);
		if (i === -1) return;
		yield e.slice(t + n.length, i), r = i + 1;
	}
}
function Oe(e, t) {
	let n = 0;
	for (; n < e.length;) {
		let r = e.indexOf("<", n);
		if (r === -1) return;
		let i = r + 1, a = e.charCodeAt(i);
		if (a === 33 || a === 47 || a === 63) {
			n = i + 1;
			continue;
		}
		let o = i;
		for (; o < e.length;) {
			let t = e.charCodeAt(o);
			if (b(t) || t === 47 || t === 62) break;
			o += 1;
		}
		let s = e.slice(i, o), c = s.lastIndexOf(":");
		if (s.slice(c + 1) === t) {
			let t = Ae(e, o);
			return t === -1 ? void 0 : e.slice(o, t);
		}
		n = o > i ? o : i + 1;
	}
}
function ke(e, t, n) {
	let r = n;
	for (; r < e.length;) {
		let n = e.indexOf(t, r);
		if (n === -1) return -1;
		let i = e.charCodeAt(n + t.length);
		if (b(i) || i === 47 || i === 62) return n;
		r = n + t.length;
	}
	return -1;
}
function Ae(e, t) {
	let n = 0;
	for (let r = t; r < e.length; r += 1) {
		let t = e.charCodeAt(r);
		if (n !== 0) t === n && (n = 0);
		else if (t === 34 || t === 39) n = t;
		else if (t === 62) return r;
	}
	return -1;
}
function je(e) {
	let t = e.length - 1;
	for (; t >= 0 && b(e.charCodeAt(t));) --t;
	return t >= 0 && e.charCodeAt(t) === 47;
}
function b(e) {
	return e === 9 || e === 10 || e === 13 || e === 32;
}
function Me(e) {
	let t = e.lastIndexOf(".");
	return t === -1 ? e : e.slice(t + 1);
}
function Ne(e) {
	let t = y(e, "Bool").next();
	if (!t.done) return t.value.body === "true" || t.value.body === "false" ? t.value.body : void 0;
}
function x(e, t) {
	for (let n of De(e, "PropertyValue")) {
		if (v(n, "Property") !== t) continue;
		let e = v(n, "Bool");
		if (e === "true" || e === "false") return e;
	}
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/contract/spatial-aggregation.js
var Pe = "honua.spatial-aggregation.v1", Fe = "honua.spatial-aggregation.metadata.v1";
function Ie(e) {
	return e !== "count";
}
function Le(e) {
	let t = [];
	We(e.sourceId) || t.push({
		path: "sourceId",
		message: "sourceId is required"
	}), e.summaries.length === 0 && t.push({
		path: "summaries",
		message: "at least one summary is required"
	});
	let n = /* @__PURE__ */ new Set();
	e.summaries.forEach((e, r) => {
		let i = `summaries[${r}]`;
		We(e.id) ? n.has(e.id) ? t.push({
			path: `${i}.id`,
			message: `duplicate summary id "${e.id}"`
		}) : n.add(e.id) : t.push({
			path: `${i}.id`,
			message: "summary id is required"
		}), Ie(e.kind) && !We(e.field) && t.push({
			path: `${i}.field`,
			message: `${e.kind} summaries require a field`
		}), e.kind === "histogram" && (e.bins !== void 0 && !qe(e.bins) && t.push({
			path: `${i}.bins`,
			message: "histogram bins must be a positive integer"
		}), e.min !== void 0 && e.max !== void 0 && e.min >= e.max && t.push({
			path: `${i}.max`,
			message: "histogram max must be greater than min"
		})), e.kind === "range" && (e.ranges.length === 0 && t.push({
			path: `${i}.ranges`,
			message: "range summaries require at least one range"
		}), e.ranges.forEach((e, n) => {
			We(e.id) || t.push({
				path: `${i}.ranges[${n}].id`,
				message: "range id is required"
			}), e.min !== void 0 && e.max !== void 0 && e.min >= e.max && t.push({
				path: `${i}.ranges[${n}].max`,
				message: "range max must be greater than min"
			});
		}));
	}), e.groupBy?.forEach((e, n) => {
		We(e.field) || t.push({
			path: `groupBy[${n}].field`,
			message: "group field is required"
		}), e.limit !== void 0 && !qe(e.limit) && t.push({
			path: `groupBy[${n}].limit`,
			message: "group limit must be a positive integer"
		});
	});
	let r = e.viewport;
	r && (r.zoom !== void 0 && !Ke(r.zoom) && t.push({
		path: "viewport.zoom",
		message: "viewport zoom must be a non-negative number"
	}), r.width !== void 0 && !Ge(r.width) && t.push({
		path: "viewport.width",
		message: "viewport width must be greater than zero"
	}), r.height !== void 0 && !Ge(r.height) && t.push({
		path: "viewport.height",
		message: "viewport height must be greater than zero"
	}));
	let i = e.resolution;
	return i && (i.zoom !== void 0 && !Ke(i.zoom) && t.push({
		path: "resolution.zoom",
		message: "resolution zoom must be a non-negative number"
	}), i.indexResolution !== void 0 && !Ke(i.indexResolution) && t.push({
		path: "resolution.indexResolution",
		message: "index resolution must be a non-negative number"
	}), i.targetCellCount !== void 0 && !qe(i.targetCellCount) && t.push({
		path: "resolution.targetCellCount",
		message: "target cell count must be a positive integer"
	}), i.maxCellCount !== void 0 && !qe(i.maxCellCount) && t.push({
		path: "resolution.maxCellCount",
		message: "max cell count must be a positive integer"
	})), e.page?.limit !== void 0 && !qe(e.page.limit) && t.push({
		path: "page.limit",
		message: "page limit must be a positive integer"
	}), t;
}
function Re(e) {
	return e === "count" || e === "sum" || e === "avg" || e === "min" || e === "max";
}
function ze(e) {
	let t = [...Le(e)], n = e.resolution?.indexResolution;
	n === void 0 ? t.push({
		path: "resolution.indexResolution",
		message: "FeatureServer queryH3 requires an explicit indexResolution"
	}) : (!Number.isInteger(n) || n < 0 || n > 15) && t.push({
		path: "resolution.indexResolution",
		message: "FeatureServer queryH3 indexResolution must be an integer between 0 and 15"
	}), e.index?.modelId !== void 0 && e.index.modelId !== "h3" && t.push({
		path: "index.modelId",
		message: "FeatureServer queryH3 only supports the H3 index model"
	}), e.index?.geometry === "centroid" && t.push({
		path: "index.geometry",
		message: "FeatureServer queryH3 does not currently return cell centroids"
	}), e.spatialFilter !== void 0 && t.push({
		path: "spatialFilter",
		message: "FeatureServer queryH3 does not currently accept spatialFilter input"
	}), e.viewport !== void 0 && t.push({
		path: "viewport",
		message: "FeatureServer queryH3 does not currently accept viewport input"
	}), e.groupBy !== void 0 && e.groupBy.length > 0 && t.push({
		path: "groupBy",
		message: "FeatureServer queryH3 does not currently support grouped summaries"
	}), e.page !== void 0 && t.push({
		path: "page",
		message: "FeatureServer queryH3 does not currently support cursor paging"
	});
	let r = e.summaries.filter((e) => Re(e.kind));
	return e.summaries.forEach((e, n) => {
		let i = `summaries[${n}]`;
		if (!Re(e.kind)) {
			t.push({
				path: `${i}.kind`,
				message: `${e.kind} summaries are not supported by FeatureServer queryH3`
			});
			return;
		}
		e.kind === "count" && e.countDistinct === !0 && t.push({
			path: `${i}.countDistinct`,
			message: "FeatureServer queryH3 does not currently support countDistinct summaries"
		}), e.kind === "count" && e.field === void 0 && r.length > 1 && t.push({
			path: `${i}.field`,
			message: "count summaries require a field when combined with other queryH3 metrics"
		});
	}), t;
}
function Be(e) {
	let t = ze(e);
	if (t.length > 0) {
		let e = t.map((e) => `${e.path}: ${e.message}`).join("; ");
		throw Error(`Invalid FeatureServer queryH3 spatial aggregation request: ${e}`);
	}
}
function Ve(e) {
	let t = "metadata" in e ? e.metadata : e;
	if (t.widgets && t.widgets.length > 0) return t.widgets;
	let n = t.summaries.map(He);
	return t.groupBy && t.groupBy.length > 0 && n.push({
		id: "grouped-summaries",
		kind: "grouped-table",
		title: "Grouped summaries",
		summaryIds: t.summaries.map((e) => e.id),
		groupBy: t.groupBy.map((e) => e.alias ?? e.field),
		interactions: ["filter", "drilldown"],
		progressive: {
			stableAcrossPages: !1,
			partialValueSemantics: "refine"
		}
	}), n;
}
function He(e) {
	return {
		id: `${e.id}-widget`,
		kind: Ue(e.kind),
		title: e.title,
		summaryId: e.id,
		field: e.field,
		valueType: e.valueType,
		unit: e.unit,
		interactions: e.kind === "count" ? ["highlight"] : ["filter", "highlight"],
		progressive: {
			stableAcrossPages: e.kind !== "histogram" && e.kind !== "range",
			partialValueSemantics: "refine"
		}
	};
}
function Ue(e) {
	switch (e) {
		case "category": return "category-list";
		case "histogram": return "histogram";
		case "range": return "range-list";
		default: return "stat";
	}
}
function We(e) {
	return typeof e == "string" && e.trim().length > 0;
}
function Ge(e) {
	return Number.isFinite(e) && e > 0;
}
function Ke(e) {
	return Number.isFinite(e) && e >= 0;
}
function qe(e) {
	return Number.isInteger(e) && e > 0;
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/esri-geojson.js
function Je(e) {
	return Array.isArray(e) && e.length >= 2 && typeof e[0] == "number" && Number.isFinite(e[0]) && typeof e[1] == "number" && Number.isFinite(e[1]);
}
function Ye(e) {
	return Array.isArray(e) ? e.filter(Je) : [];
}
function Xe(e) {
	return Array.isArray(e) ? e.map((e) => Ye(e)).filter((e) => e.length > 0) : [];
}
function Ze(e) {
	if (e.length < 3) return 0;
	let t = 0;
	for (let n = 0; n < e.length; n += 1) {
		let r = e[n], i = e[(n + 1) % e.length];
		!r || !i || (t += r[0] * i[1] - i[0] * r[1]);
	}
	return t / 2;
}
function Qe(e) {
	return Ze(e) < 0;
}
function $e(e, t, n) {
	let r = 1e-9, i = n[0] - t[0], a = n[1] - t[1], o = i * i + a * a;
	if (o <= r * r) {
		let n = e[0] - t[0], i = e[1] - t[1];
		return n * n + i * i <= r * r;
	}
	let s = (e[0] - t[0]) * a - (e[1] - t[1]) * i;
	if (Math.abs(s) > r) return !1;
	let c = (e[0] - t[0]) * i + (e[1] - t[1]) * a;
	return c < -1e-9 ? !1 : c - o <= r;
}
function et(e, t) {
	if (t.length < 3) return !1;
	let n = !1;
	for (let r = 0, i = t.length - 1; r < t.length; i = r, r += 1) {
		let a = t[r], o = t[i];
		if (!(!a || !o)) {
			if ($e(e, o, a)) return !0;
			a[1] > e[1] != o[1] > e[1] && e[0] < (o[0] - a[0]) * (e[1] - a[1]) / (o[1] - a[1]) + a[0] && (n = !n);
		}
	}
	return n;
}
function tt(e, t, n) {
	let r = n[0];
	if (!r) return -1;
	let i = -1, a = Infinity;
	for (let n = 0; n < e.length; n += 1) {
		let o = e[n]?.[0], s = t[n];
		!o || s === void 0 || !et(r, o) || s < a && (a = s, i = n);
	}
	return i;
}
function nt(e, t) {
	let n = e.map((e) => [...e]), r = Ze(n);
	return r === 0 || (t ? r > 0 : r < 0) ? n : n.reverse();
}
function rt(e) {
	return e.map((e, t) => nt(e, t === 0));
}
function it(e) {
	let t = [], n = [], r = [];
	for (let i of e) {
		if (Qe(i)) {
			t.push([i]), n.push(Math.abs(Ze(i)));
			continue;
		}
		r.push(i);
	}
	for (let e of r) {
		let r = tt(t, n, e);
		if (r < 0) {
			t.push([e]), n.push(Math.abs(Ze(e)));
			continue;
		}
		t[r]?.push(e);
	}
	return t.length === 1 ? {
		type: "Polygon",
		coordinates: rt(t[0] ?? [])
	} : {
		type: "MultiPolygon",
		coordinates: t.map(rt)
	};
}
function at(e, t) {
	return typeof e[t] == "number" && Number.isFinite(e[t]);
}
function ot(e) {
	if (!e || typeof e != "object") return null;
	let t = e;
	if (at(t, "x") && at(t, "y")) return t.z !== void 0 && !at(t, "z") ? null : {
		type: "Point",
		coordinates: [
			t.x,
			t.y,
			...t.z === void 0 ? [] : [t.z]
		]
	};
	if (Array.isArray(t.points)) {
		let e = Ye(t.points);
		return e.length < 1 ? null : {
			type: "MultiPoint",
			coordinates: e
		};
	}
	if (Array.isArray(t.paths)) {
		let e = Xe(t.paths);
		return e.length < 1 ? null : e.length === 1 ? {
			type: "LineString",
			coordinates: e[0] ?? []
		} : {
			type: "MultiLineString",
			coordinates: e
		};
	}
	if (Array.isArray(t.rings)) {
		let e = Xe(t.rings);
		return e.length < 1 ? null : it(e);
	}
	if (at(t, "xmin") && at(t, "ymin") && at(t, "xmax") && at(t, "ymax")) {
		let e = t.xmin, n = t.ymin, r = t.xmax, i = t.ymax;
		return {
			type: "Polygon",
			coordinates: [[
				[e, n],
				[r, n],
				[r, i],
				[e, i],
				[e, n]
			]]
		};
	}
	return null;
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/wire-shared.js
function S(...e) {
	let t = {};
	for (let n of e) if (n) {
		if (n instanceof Headers) {
			for (let [e, r] of n.entries()) t[e] = r;
			continue;
		}
		if (Array.isArray(n)) {
			for (let [e, r] of n) t[e] = r;
			continue;
		}
		for (let [e, r] of Object.entries(n)) r != null && (t[e] = String(r));
	}
	return t;
}
function C(e) {
	let t = new URLSearchParams();
	if (t.set("f", e.responseFormat ?? "json"), e.extraParams) for (let [n, r] of Object.entries(e.extraParams)) t.set(n, String(r));
	return t;
}
function st(e) {
	return typeof e == "string" ? e : Array.from(e).join(",");
}
function ct(e) {
	return typeof e == "string" ? e : e.join(",");
}
function lt(e) {
	return typeof e == "object" && !!e && e.exceededTransferLimit === !0;
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/ogc-maps.js
var ut = "/ogc/maps";
function dt(e) {
	if (e.basePath === void 0) return ut;
	let t = e.basePath, n = t.length;
	for (; n > 0 && t.charCodeAt(n - 1) === 47;) n--;
	return t.slice(0, n);
}
function ft(e) {
	let t = dt(e);
	return t === ut ? "" : `${t}:`;
}
var pt = class {
	client;
	basePath;
	constructor(e) {
		this.client = e.client, this.basePath = e.basePath;
	}
	withBase(e) {
		return this.basePath === void 0 ? e : {
			...e,
			basePath: this.basePath
		};
	}
	collection(e, t) {
		return new mt({
			client: this.client,
			collectionId: e,
			styleId: t,
			...this.basePath === void 0 ? {} : { basePath: this.basePath }
		});
	}
	async landing(e = {}) {
		return this.client.getOgcMapsLanding(this.withBase(e));
	}
	async conformance(e = {}) {
		return this.client.getOgcMapsConformance(this.withBase(e));
	}
	async map(e = {}) {
		return this.client.getOgcMapImage(this.withBase(e));
	}
}, mt = class {
	client;
	collectionId;
	styleId;
	basePath;
	constructor(e) {
		this.client = e.client, this.collectionId = e.collectionId, this.styleId = e.styleId, this.basePath = e.basePath;
	}
	async map(e = {}) {
		return this.client.getOgcMapImage({
			...e,
			collectionId: this.collectionId,
			styleId: e.styleId ?? this.styleId,
			...this.basePath === void 0 ? {} : { basePath: this.basePath }
		});
	}
};
async function ht(e, t = {}) {
	let n = C(t), r = dt(t);
	return e.requestCachedMetadataJson(`ogc-maps:landing:${ft(t)}${n.toString()}`, `${r}?${n.toString()}`, t);
}
async function gt(e, t = {}) {
	let n = C(t), r = dt(t);
	return e.requestCachedMetadataJson(`ogc-maps:conformance:${ft(t)}${n.toString()}`, `${r}/conformance?${n.toString()}`, t);
}
async function _t(e, t) {
	let n = vt(t), r = t.collectionId === void 0 ? "" : `/collections/${encodeURIComponent(String(t.collectionId))}`, i = t.styleId ? `/styles/${encodeURIComponent(t.styleId)}` : "", a = `${dt(t)}${r}${i}/map${n.size > 0 ? `?${n.toString()}` : ""}`, o = St(t.format) ?? "image/png", s = await e.requestBytes("GET", a, o, void 0, t.signal);
	return {
		bytes: s.bytes,
		contentType: s.contentType
	};
}
function vt(e) {
	let t = new URLSearchParams(), n = xt(e.format);
	if (n !== void 0 && t.set("f", n), e.width !== void 0 && t.set("width", String(e.width)), e.height !== void 0 && t.set("height", String(e.height)), e.bbox !== void 0 && t.set("bbox", typeof e.bbox == "string" ? e.bbox : e.bbox.join(",")), e.bboxCrs !== void 0 && t.set("bbox-crs", e.bboxCrs), e.crs !== void 0 && t.set("crs", e.crs), e.collections !== void 0 && e.collections.length > 0 && t.set("collections", e.collections.join(",")), e.transparent !== void 0 && t.set("transparent", String(e.transparent)), e.extraParams) for (let [n, r] of Object.entries(e.extraParams)) t.set(n, String(r));
	return t;
}
var yt = /* @__PURE__ */ new Map([
	["image/png", "png"],
	["image/jpeg", "jpeg"],
	["image/jpg", "jpg"],
	["image/tiff", "tiff"],
	["image/tif", "tif"]
]), bt = /* @__PURE__ */ new Map([
	["png", "image/png"],
	["jpeg", "image/jpeg"],
	["jpg", "image/jpeg"],
	["tiff", "image/tiff"],
	["tif", "image/tiff"]
]);
function xt(e) {
	if (e === void 0) return;
	let t = e.toLowerCase();
	return yt.get(t) ?? t;
}
function St(e) {
	if (e === void 0) return;
	let t = e.toLowerCase();
	return bt.get(t) ?? e;
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/ogc-records.js
var Ct = "/ogc/records";
function wt(e) {
	if (e.basePath === void 0) return Ct;
	let t = e.basePath, n = t.length;
	for (; n > 0 && t.charCodeAt(n - 1) === 47;) n--;
	return t.slice(0, n);
}
function Tt(e) {
	let t = wt(e);
	return t === Ct ? "" : `${t}:`;
}
var Et = 100, Dt = 100, Ot = class {
	client;
	basePath;
	constructor(e) {
		this.client = e.client, this.basePath = e.basePath;
	}
	withBase(e) {
		return this.basePath === void 0 ? e : {
			...e,
			basePath: this.basePath
		};
	}
	collection(e) {
		return new kt({
			client: this.client,
			collectionId: e,
			...this.basePath === void 0 ? {} : { basePath: this.basePath }
		});
	}
	async landing(e = {}) {
		return this.client.getOgcRecordsLanding(this.withBase(e));
	}
	async conformance(e = {}) {
		return this.client.getOgcRecordsConformance(this.withBase(e));
	}
	async collections(e = {}) {
		return this.client.listOgcRecordCollections(this.withBase(e));
	}
	async collectionMetadata(e) {
		return this.client.getOgcRecordCollection(this.withBase(e));
	}
	async search(e) {
		return this.client.searchOgcRecords(this.withBase(e));
	}
	async record(e) {
		return this.client.getOgcRecord(this.withBase(e));
	}
	async rawSearch(e) {
		return this.client.fetchOgcRecordsRaw(this.withBase(e));
	}
	async rawRecord(e) {
		return this.client.fetchOgcRecordRaw(this.withBase(e));
	}
	async searchAll(e) {
		let t = e.pageSize ?? e.limit ?? Et, n = e.maxPages ?? Dt, r = [], i = { offset: e.offset };
		for (let a = 0; a < n; a += 1) {
			let n = await this.client.searchOgcRecords(this.withBase({
				...e,
				limit: t,
				offset: i.offset
			})), a = n.features ?? [];
			if (a.length === 0 || (r.push(...a), i = jt(n.links), i.offset === void 0)) break;
		}
		return r;
	}
	async *searchStream(e) {
		let t = e.pageSize ?? e.limit ?? Et, n = e.maxPages ?? Dt, r = { offset: e.offset };
		for (let i = 0; i < n; i += 1) {
			let n = await this.client.searchOgcRecords(this.withBase({
				...e,
				limit: t,
				offset: r.offset
			})), i = n.features ?? [];
			if (i.length === 0 || (yield i, r = jt(n.links), r.offset === void 0)) break;
		}
	}
}, kt = class {
	client;
	collectionId;
	basePath;
	constructor(e) {
		this.client = e.client, this.collectionId = e.collectionId, this.basePath = e.basePath;
	}
	bind(e) {
		return {
			...e,
			collectionId: this.collectionId,
			...this.basePath === void 0 ? {} : { basePath: this.basePath }
		};
	}
	rootHandle() {
		return new Ot({
			client: this.client,
			...this.basePath === void 0 ? {} : { basePath: this.basePath }
		});
	}
	async metadata(e = {}) {
		return this.client.getOgcRecordCollection(this.bind(e));
	}
	async search(e = {}) {
		return this.client.searchOgcRecords(this.bind(e));
	}
	async record(e) {
		return this.client.getOgcRecord(this.bind(e));
	}
	async rawSearch(e = {}) {
		return this.client.fetchOgcRecordsRaw(this.bind(e));
	}
	async rawRecord(e) {
		return this.client.fetchOgcRecordRaw(this.bind(e));
	}
	async searchAll(e = {}) {
		return this.rootHandle().searchAll({
			...e,
			collectionId: this.collectionId
		});
	}
	searchStream(e = {}) {
		return this.rootHandle().searchStream({
			...e,
			collectionId: this.collectionId
		});
	}
}, At = [
	"offset",
	"startindex",
	"startIndex"
];
function jt(e) {
	if (!e) return {};
	for (let t of e) if (!(t.rel !== "next" || typeof t.href != "string")) try {
		let e = Mt(new URL(t.href, "https://placeholder.test").searchParams, At);
		if (e === null) continue;
		let n = Number(e);
		if (Number.isFinite(n)) return { offset: n };
	} catch {}
	return {};
}
function Mt(e, t) {
	for (let n of t) {
		let t = e.get(n);
		if (t !== null) return t;
	}
	return null;
}
async function Nt(e, t = {}) {
	let n = C(t), r = wt(t);
	return e.requestCachedMetadataJson(`ogc-records:landing:${Tt(t)}${n.toString()}`, `${r}?${n.toString()}`, t);
}
async function Pt(e, t = {}) {
	let n = C(t), r = wt(t);
	return e.requestCachedMetadataJson(`ogc-records:conformance:${Tt(t)}${n.toString()}`, `${r}/conformance?${n.toString()}`, t);
}
async function Ft(e, t = {}) {
	let n = C(t), r = wt(t);
	return e.requestCachedMetadataJson(`ogc-records:collections:${Tt(t)}${n.toString()}`, `${r}/collections?${n.toString()}`, t);
}
async function It(e, t) {
	let n = C(t), r = `${wt(t)}/collections/${encodeURIComponent(String(t.collectionId))}`;
	return e.requestCachedMetadataJson(`ogc-records:collection:${Tt(t)}${t.collectionId}:${n.toString()}`, `${r}?${n.toString()}`, t);
}
async function Lt(e, t) {
	return e.requestJson("GET", Ht(t), void 0, t.signal);
}
async function Rt(e, t) {
	return e.requestJson("GET", Ut(t), void 0, t.signal);
}
async function zt(e, t) {
	return e.pipelineFetch("GET", Ht(t), { headers: S({ Accept: t.accept ?? "application/geo+json, application/json;q=0.9" }, t.headers) }, t.signal);
}
async function Bt(e, t) {
	return e.pipelineFetch("GET", Ut(t), { headers: S({ Accept: t.accept ?? "application/geo+json, application/json;q=0.9" }, t.headers) }, t.signal);
}
function Vt(e) {
	return Array.isArray(e) ? e.join(",") : String(e);
}
function Ht(e) {
	let t = encodeURIComponent(String(e.collectionId)), n = Wt(e);
	return `${wt(e)}/collections/${t}/items?${n.toString()}`;
}
function Ut(e) {
	let t = encodeURIComponent(String(e.collectionId)), n = encodeURIComponent(String(e.recordId)), r = C(e);
	return e.profile !== void 0 && r.set("profile", ct(e.profile)), `${wt(e)}/collections/${t}/items/${n}?${r.toString()}`;
}
function Wt(e) {
	let t = C(e);
	return e.limit !== void 0 && t.set("limit", String(e.limit)), e.offset !== void 0 && t.set("offset", String(e.offset)), e.bbox !== void 0 && t.set("bbox", Vt(e.bbox)), e.datetime !== void 0 && t.set("datetime", e.datetime), e.q !== void 0 && t.set("q", ct(e.q)), e.ids !== void 0 && t.set("ids", st(e.ids)), e.type !== void 0 && t.set("type", ct(e.type)), e.externalIds !== void 0 && t.set("externalIds", ct(e.externalIds)), e.filter !== void 0 && t.set("filter", e.filter), e.filterLang !== void 0 && t.set("filter-lang", e.filterLang), e.filterCrs !== void 0 && t.set("filter-crs", e.filterCrs), e.properties !== void 0 && t.set("properties", ct(e.properties)), e.sortby !== void 0 && t.set("sortby", e.sortby), e.profile !== void 0 && t.set("profile", ct(e.profile)), t;
}
var Gt = "/ogc/tiles";
function w(e) {
	if (e.basePath === void 0) return Gt;
	let t = e.basePath, n = t.length;
	for (; n > 0 && t.charCodeAt(n - 1) === 47;) n--;
	return t.slice(0, n);
}
function Kt(e) {
	let t = w(e);
	return t === Gt ? "" : `${t}:`;
}
var qt = class {
	client;
	basePath;
	constructor(e) {
		this.client = e.client, this.basePath = e.basePath;
	}
	withBase(e) {
		return this.basePath === void 0 ? e : {
			...e,
			basePath: this.basePath
		};
	}
	tileset(e, t) {
		return new Jt({
			client: this.client,
			collectionId: e,
			tileMatrixSetId: t,
			...this.basePath === void 0 ? {} : { basePath: this.basePath }
		});
	}
	async landing(e = {}) {
		return this.client.getOgcTilesLanding(this.withBase(e));
	}
	async conformance(e = {}) {
		return this.client.getOgcTilesConformance(this.withBase(e));
	}
	async tileMatrixSets(e = {}) {
		return this.client.listOgcTileMatrixSets(this.withBase(e));
	}
	async tileMatrixSet(e, t = {}) {
		return this.client.getOgcTileMatrixSet(this.withBase({
			...t,
			tileMatrixSetId: e
		}));
	}
	async tilesets(e) {
		return this.client.listOgcCollectionTilesets(this.withBase(e));
	}
	async tilesetMetadata(e) {
		return this.client.getOgcCollectionTileset(this.withBase(e));
	}
	async tile(e) {
		return this.client.fetchOgcTile(this.withBase(e));
	}
	getMapLibreVectorSource(e, t = {}) {
		let n = t.tileMatrixSetId ?? "WebMercatorQuad", r = ie(this.client.serverBaseUrl), i = oe(e), a = encodeURIComponent(n), o = {
			type: "vector",
			tiles: [`${r}${w({ ...this.basePath === void 0 ? {} : { basePath: this.basePath } })}/collections/${i}/tiles/${a}/{z}/{y}/{x}`],
			scheme: "xyz"
		};
		return t.minzoom !== void 0 && (o.minzoom = t.minzoom), t.maxzoom !== void 0 && (o.maxzoom = t.maxzoom), o;
	}
	getDefaultSourceLayer(e) {
		let t = e.split("/");
		return t[t.length - 1] ?? e;
	}
	getMapLibreConfig(e, t = {}) {
		return {
			source: this.getMapLibreVectorSource(e, t),
			sourceLayer: this.getDefaultSourceLayer(e)
		};
	}
}, Jt = class {
	client;
	collectionId;
	tileMatrixSetId;
	basePath;
	constructor(e) {
		this.client = e.client, this.collectionId = e.collectionId, this.tileMatrixSetId = e.tileMatrixSetId, this.basePath = e.basePath;
	}
	async metadata(e = {}) {
		return this.client.getOgcCollectionTileset({
			...e,
			collectionId: this.collectionId,
			tileMatrixSetId: this.tileMatrixSetId,
			...this.basePath === void 0 ? {} : { basePath: this.basePath }
		});
	}
	async tile(e) {
		return this.client.fetchOgcTile({
			...e,
			collectionId: this.collectionId,
			tileMatrixSetId: this.tileMatrixSetId,
			...this.basePath === void 0 ? {} : { basePath: this.basePath }
		});
	}
};
async function Yt(e, t = {}) {
	let n = C(t), r = w(t);
	return e.requestCachedMetadataJson(`ogc-tiles:landing:${Kt(t)}${n.toString()}`, `${r}?${n.toString()}`, t);
}
async function Xt(e, t = {}) {
	let n = C(t), r = w(t);
	return e.requestCachedMetadataJson(`ogc-tiles:conformance:${Kt(t)}${n.toString()}`, `${r}/conformance?${n.toString()}`, t);
}
async function Zt(e, t = {}) {
	let n = C(t), r = w(t);
	return e.requestCachedMetadataJson(`ogc-tiles:tile-matrix-sets:${Kt(t)}${n.toString()}`, `${r}/tileMatrixSets?${n.toString()}`, t);
}
async function Qt(e, t) {
	let n = C(t), r = w(t);
	return e.requestCachedMetadataJson(`ogc-tiles:tile-matrix-set:${Kt(t)}${t.tileMatrixSetId}:${n.toString()}`, `${r}/tileMatrixSets/${encodeURIComponent(t.tileMatrixSetId)}?${n.toString()}`, t);
}
async function $t(e, t) {
	let n = C(t), r = `${w(t)}/collections/${encodeURIComponent(String(t.collectionId))}/tiles`;
	return e.requestCachedMetadataJson(`ogc-tiles:tilesets:${Kt(t)}${t.collectionId}:${n.toString()}`, `${r}?${n.toString()}`, t);
}
async function en(e, t) {
	let n = C(t), r = `${w(t)}/collections/${encodeURIComponent(String(t.collectionId))}/tiles/${encodeURIComponent(t.tileMatrixSetId)}`;
	return e.requestCachedMetadataJson(`ogc-tiles:tileset:${Kt(t)}${t.collectionId}:${t.tileMatrixSetId}:${n.toString()}`, `${r}?${n.toString()}`, t);
}
async function tn(e, t) {
	let n = new URLSearchParams();
	if (t.extraParams) for (let [e, r] of Object.entries(t.extraParams)) n.set(e, String(r));
	let r = encodeURIComponent(String(t.collectionId)), i = encodeURIComponent(t.tileMatrixSetId), a = encodeURIComponent(String(t.tileMatrix)), o = n.size > 0 ? `?${n.toString()}` : "", s = `${w(t)}/collections/${r}/tiles/${i}/${a}/${t.tileRow}/${t.tileCol}${o}`;
	return e.requestBytes("GET", s, t.accept, void 0, t.signal);
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/stac-conformance.js
var nn = "https://api.stacspec.org", rn = "/v1.0.0/item-search", an = `${nn}${rn}`, on = "http://www.opengis.net/spec/cql2/1.0/conf/", sn = Object.freeze({
	core: `${nn}/v1.0.0/core`,
	"item-search": an,
	filter: `${an}#filter`,
	"cql2-text": `${on}cql2-text`,
	"cql2-json": `${on}cql2-json`,
	sort: `${an}#sort`,
	fields: `${an}#fields`,
	query: `${an}#query`
}), cn = "/v1.0.0/ogcapi-features", ln = {
	filter: "#filter",
	sort: "#sort",
	fields: "#fields",
	query: "#query"
}, un = class extends l {
	conformanceClass;
	missing;
	unread;
	constructor(e, t = {}) {
		let { unread: n = !1, ...i } = t, a = e[0] ?? sn["item-search"], o = e.map((e) => `"${e}"`).join(", "), s = n ? `STAC search uses conformance class ${o} but the catalog conformsTo list was not read.` : `STAC catalog does not declare conformance class ${o}.`;
		super("core.capability-not-supported", s, {
			...i,
			context: r(i.context, {
				conformanceClass: a,
				missing: [...e],
				unread: n
			})
		}), this.name = "HonuaStacConformanceError", this.conformanceClass = a, this.missing = Object.freeze([...e]), this.unread = n;
	}
};
function dn(e) {
	return e ? e.filter((e) => typeof e == "string") : [];
}
function fn(e, t) {
	if (!e) return !1;
	for (let n of e) if (typeof n == "string" && hn(n, t)) return !0;
	return !1;
}
function pn(e) {
	let t = e.filter !== void 0 || e.filterLang !== void 0, n = e.sortby !== void 0, r = e.fields !== void 0;
	if (!t && !n && !r) return [];
	let i = ["item-search"];
	if (t) {
		i.push("filter");
		let t = e.filterLang ?? (e.usePost === !0 ? "cql2-json" : "cql2-text");
		t === "cql2-json" ? i.push("cql2-json") : t === "cql2-text" ? i.push("cql2-text") : i.push("cql2-text", "cql2-json");
	}
	return n && i.push("sort"), r && i.push("fields"), i;
}
function mn(e) {
	let t = pn(e);
	if (t.length === 0) return;
	let n = t.filter((t) => !fn(e.conformsTo, t)).map((e) => sn[e]);
	if (n.length !== 0) throw new un(n, { unread: e.conformsTo === void 0 });
}
function hn(e, t) {
	if (t === "cql2-text" || t === "cql2-json") return _n(e, t);
	let n = gn(e);
	if (!n) return !1;
	let r = n.pathname.replace(/\/+$/, "");
	if (t === "core" || t === "item-search") return n.hash === "" && r === `/v1.0.0/${t}`;
	let i = ln[t];
	return i !== void 0 && n.hash === i && (r === rn || r === cn);
}
function gn(e) {
	try {
		let t = new URL(e);
		return t.protocol !== "https:" || t.origin !== nn || t.username !== "" || t.password !== "" || t.search !== "" ? void 0 : t;
	} catch {
		return;
	}
}
function _n(e, t) {
	try {
		let n = new URL(e);
		return n.protocol !== "http:" && n.protocol !== "https:" || n.hostname !== "www.opengis.net" || n.port !== "" || n.username !== "" || n.password !== "" || n.search !== "" || n.hash !== "" ? !1 : n.pathname.replace(/\/+$/, "") === `/spec/cql2/1.0/conf/${t}`;
	} catch {
		return !1;
	}
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/stac.js
var vn = 100, yn = 100, bn = class {
	client;
	basePath;
	postSearchProbe;
	postSearchSupported;
	conformance = /* @__PURE__ */ new Map();
	postSearchUsable;
	constructor(e) {
		this.client = e.client, this.basePath = e.basePath;
	}
	withBase(e) {
		return this.basePath === void 0 || e.stacBasePath !== void 0 ? e : {
			...e,
			stacBasePath: this.basePath
		};
	}
	async landing(e = {}) {
		return this.client.getStacLanding(this.withBase(e));
	}
	async collections(e = {}) {
		return this.client.listStacCollections(this.withBase(e));
	}
	async collection(e) {
		return this.client.getStacCollection(this.withBase(e));
	}
	async item(e) {
		return this.client.getStacItem(this.withBase(e));
	}
	async search(e = {}) {
		return this.dispatchSearch(e);
	}
	async dispatchSearch(e) {
		let t = await this.prepareSearch(e), n = this.withBase(t);
		if (n.usePost !== !0) return this.client.searchStac(n);
		let r = t.allowPostFallback !== !1, i = {
			...n,
			usePost: !1,
			filterLang: n.filterLang ?? (n.filter === void 0 ? void 0 : "cql2-json")
		};
		if (this.postSearchUsable === !1 && r) return this.client.searchStac(i);
		try {
			let e = await this.client.searchStac(n);
			return this.postSearchUsable = !0, e;
		} catch (e) {
			if (e instanceof un || !r || this.postSearchUsable !== void 0 || t.signal?.aborted === !0) throw e;
			return this.postSearchUsable = !1, this.client.searchStac(i);
		}
	}
	async prepareSearch(e) {
		let t = pn(e);
		if (e.conformsTo !== void 0 || t.length === 0) return e;
		let n = this.conformance.get(T(this.withBase(e)));
		if (n !== void 0) return {
			...e,
			conformsTo: n
		};
		try {
			return {
				...e,
				conformsTo: await this.readConformance(e)
			};
		} catch (n) {
			throw e.signal?.aborted ? n : new un(t.map((e) => sn[e]), {
				unread: !0,
				cause: n
			});
		}
	}
	async readConformance(e) {
		let t = T(this.withBase(e)), n = this.conformance.get(t);
		if (n !== void 0) return n;
		let r = dn((await this.landing(e)).conformsTo);
		return this.conformance.set(t, r), r;
	}
	async supportsPostSearch(e) {
		if (this.postSearchSupported !== void 0) return this.postSearchSupported;
		if (e) {
			let t = await this.probePostSearch(e);
			return this.postSearchSupported = t, t;
		}
		this.postSearchProbe ??= this.probePostSearch();
		let t = await this.postSearchProbe;
		return this.postSearchSupported = t, t;
	}
	async probePostSearch(e) {
		try {
			let t = await this.landing({ signal: e }), n = T(this.withBase({})), r = this.conformance.get(n) ?? dn(t.conformsTo);
			if (this.conformance.set(n, r), !fn(r, "item-search")) return !1;
			for (let e of t.links ?? []) if (e.rel === "search" && typeof e.method == "string" && e.method.toUpperCase() === "POST") return !0;
			return !1;
		} catch (t) {
			if (e?.aborted) throw t;
			return !1;
		}
	}
	async searchAll(e = {}) {
		let t = e.pageSize ?? e.limit ?? vn, n = e.maxPages ?? yn, r = [], i = {
			offset: e.offset,
			next: e.next
		};
		for (let a = 0; a < n; a += 1) {
			let n = await this.dispatchSearch({
				...e,
				limit: t,
				...xn(i)
			}), a = n.features ?? [];
			if (a.length === 0) break;
			r.push(...a);
			let o = wn(n.links);
			if (!Sn(o) || Cn(o) === Cn(i)) break;
			i = o;
		}
		return r;
	}
	async *searchStream(e = {}) {
		let t = e.pageSize ?? e.limit ?? vn, n = e.maxPages ?? yn, r = {
			offset: e.offset,
			next: e.next
		};
		for (let i = 0; i < n; i += 1) {
			let n = await this.dispatchSearch({
				...e,
				limit: t,
				...xn(r)
			}), i = n.features ?? [];
			if (i.length === 0) break;
			yield i;
			let a = wn(n.links);
			if (!Sn(a) || Cn(a) === Cn(r)) break;
			r = a;
		}
	}
};
function xn(e) {
	return {
		offset: e.offset,
		next: e.next,
		nextParams: e.params,
		nextBody: e.body
	};
}
function Sn(e) {
	return e.offset !== void 0 || e.next !== void 0 || e.params !== void 0 || e.body !== void 0;
}
function Cn(e) {
	return JSON.stringify([
		e.offset ?? null,
		e.next ?? null,
		e.params ?? null,
		e.body ?? null
	]);
}
function wn(e) {
	if (!e) return {};
	for (let t of e) {
		if (t.rel !== "next") continue;
		let e = t.body;
		if (e && typeof e == "object" && !Array.isArray(e) && Object.keys(e).length > 0) return { body: { ...e } };
		if (typeof t.href == "string") try {
			let e = new URL(t.href, "https://placeholder.test"), n = e.searchParams.get("offset");
			if (n !== null) {
				let e = Number(n);
				if (Number.isFinite(e)) return { offset: e };
			}
			let r = e.searchParams.get("next");
			if (r !== null) return { next: r };
			let i = {};
			for (let [t, n] of e.searchParams) i[t] = n;
			if (Object.keys(i).length > 0) return { params: i };
		} catch {}
	}
	return {};
}
function T(e) {
	let t = e.stacBasePath;
	if (t === void 0) return "/stac";
	let n = t.length;
	for (; n > 0 && t.charCodeAt(n - 1) === 47;) n--;
	return t.slice(0, n);
}
async function Tn(e, t = {}) {
	let n = C(t), r = T(t);
	return e.requestCachedMetadataJson(`stac:landing:${r}:${n.toString()}`, `${r}?${n.toString()}`, t);
}
async function En(e, t = {}) {
	let n = C(t), r = T(t);
	return e.requestCachedMetadataJson(`stac:collections:${r}:${n.toString()}`, `${r}/collections?${n.toString()}`, t);
}
async function Dn(e, t) {
	let n = C(t), r = T(t);
	return e.requestCachedMetadataJson(`stac:collection:${r}:${t.collectionId}:${n.toString()}`, `${r}/collections/${encodeURIComponent(String(t.collectionId))}?${n.toString()}`, t);
}
async function On(e, t) {
	let n = C(t), r = `${T(t)}/collections/${encodeURIComponent(String(t.collectionId))}/items/${encodeURIComponent(String(t.itemId))}`;
	return e.requestJson("GET", `${r}?${n.toString()}`, void 0, t.signal);
}
async function kn(e, t = {}) {
	mn(t);
	let n = T(t);
	if (t.usePost) {
		let r = t.nextParams === void 0 ? "" : `?${new URLSearchParams({ ...t.nextParams }).toString()}`;
		return e.requestJson("POST", `${n}/search${r}`, {
			headers: S({
				"Content-Type": "application/json",
				Accept: "application/json"
			}),
			body: JSON.stringify(jn(t))
		}, t.signal);
	}
	let r = An(t);
	return e.requestJson("GET", `${n}/search?${r.toString()}`, void 0, t.signal);
}
function An(e) {
	let t = new URLSearchParams();
	e.bbox !== void 0 && t.set("bbox", e.bbox.join(",")), e.datetime !== void 0 && t.set("datetime", e.datetime), e.ids !== void 0 && e.ids.length > 0 && t.set("ids", e.ids.join(",")), e.collections !== void 0 && e.collections.length > 0 && t.set("collections", e.collections.join(",")), e.intersects !== void 0 && t.set("intersects", JSON.stringify(e.intersects));
	let n = Mn(e.fields);
	if (n !== void 0 && t.set("fields", n), e.filter !== void 0 && t.set("filter", typeof e.filter == "string" ? e.filter : JSON.stringify(e.filter)), e.filterLang !== void 0 && t.set("filter-lang", e.filterLang), e.limit !== void 0 && t.set("limit", String(e.limit)), e.offset !== void 0 && t.set("offset", String(e.offset)), e.next !== void 0 && t.set("next", e.next), e.sortby !== void 0 && t.set("sortby", typeof e.sortby == "string" ? e.sortby : e.sortby.map((e) => `${e.direction === "desc" ? "-" : "+"}${e.field}`).join(",")), e.nextParams !== void 0) for (let [n, r] of Object.entries(e.nextParams)) t.set(n, r);
	return t;
}
function jn(e) {
	let t = {};
	return e.bbox !== void 0 && (t.bbox = e.bbox), e.datetime !== void 0 && (t.datetime = e.datetime), e.intersects !== void 0 && (t.intersects = e.intersects), e.ids !== void 0 && (t.ids = e.ids), e.collections !== void 0 && (t.collections = e.collections), e.filter !== void 0 && (t.filter = e.filter), e.filterLang !== void 0 && (t["filter-lang"] = e.filterLang), e.limit !== void 0 && (t.limit = e.limit), e.offset !== void 0 && (t.offset = e.offset), e.next !== void 0 && (t.next = e.next), e.sortby !== void 0 && (t.sortby = e.sortby), e.fields !== void 0 && (t.fields = e.fields), e.nextBody !== void 0 && Object.assign(t, e.nextBody), t;
}
function Mn(e) {
	if (!e) return;
	let t = [];
	if (e.include) for (let n of e.include) typeof n == "string" && n.length > 0 && t.push(n);
	if (e.exclude) for (let n of e.exclude) typeof n == "string" && n.length > 0 && t.push(`-${n}`);
	return t.length > 0 ? t.join(",") : void 0;
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/contract/jobs.js
function Nn(e) {
	return e === "successful" || e === "failed" || e === "dismissed";
}
var Pn = class extends Error {
	reason;
	jobId;
	lastStatus;
	constructor(e, t, n, r) {
		super(e), this.name = "HonuaJobPollTimeoutError", this.reason = t, this.jobId = n, this.lastStatus = r;
	}
}, Fn = class extends l {
	status;
	errorCode;
	details;
	constructor(e, t, n, r) {
		super("core.job-failed", e, { context: {
			status: t,
			errorCode: n
		} }), this.name = "HonuaJobFailedError", this.status = t, this.errorCode = n, this.details = r;
	}
}, In = 6e5, Ln = class {
	options;
	currentStatus;
	currentProgress;
	terminalSnapshot;
	terminalPromise;
	cancelPromise;
	listeners = /* @__PURE__ */ new Set();
	constructor(e) {
		this.options = e, this.currentStatus = e.initialStatus, this.currentProgress = e.initialProgress;
	}
	get status() {
		return this.currentStatus;
	}
	get progress() {
		return this.currentProgress;
	}
	get terminal() {
		return this.terminalSnapshot;
	}
	watch(e) {
		return this.listeners.add(e), () => this.listeners.delete(e);
	}
	async poll() {
		return this.terminalSnapshot ? this.terminalSnapshot : this.observe(await this.options.poll());
	}
	observe(e) {
		if (this.terminalSnapshot) return this.terminalSnapshot;
		this.currentStatus = e.status, this.currentProgress = e.progress, Nn(e.status) && (this.terminalSnapshot = e);
		for (let t of this.listeners) try {
			t(e);
		} catch {}
		return e;
	}
	async cancel(e) {
		return this.terminalSnapshot ? this.currentStatus : (this.cancelPromise ||= e().then((e) => {
			if (this.terminalSnapshot) return this.currentStatus;
			let t = this.observe(e).status;
			return Nn(t) || (this.cancelPromise = void 0), t;
		}).catch((e) => {
			throw this.cancelPromise = void 0, e;
		}), this.cancelPromise);
	}
	results(e = {}) {
		if (!this.terminalPromise) {
			let t = this.resolveBudget(e);
			this.terminalPromise = this.runUntilTerminal(t).catch((e) => {
				throw this.terminalPromise = void 0, e;
			});
		}
		return this.terminalPromise;
	}
	resolveBudget(e) {
		let t = {
			...this.options.pollBudget,
			...e
		};
		return t.deadlineMs !== void 0 || t.maxAttempts !== void 0 ? t : {
			...t,
			deadlineMs: In
		};
	}
	async runUntilTerminal(e) {
		let t = typeof this.options.pollIntervalMs == "function" ? this.options.pollIntervalMs() : this.options.pollIntervalMs, n = e.pollIntervalMs ?? t, r = e.maxPollIntervalMs ?? Math.max(n, 3e4), i = Date.now(), a = 0;
		for (; !this.terminalSnapshot;) {
			this.assertWithinBudget(e, a, i);
			let t;
			try {
				t = await this.pollWithinBudget(e, i);
			} catch (t) {
				throw e.signal?.aborted && this.throwTimeout("aborted", e), t;
			}
			if (a += 1, this.observe(t), this.terminalSnapshot) break;
			this.assertWithinBudget(e, a, i);
			let o = Math.min(r, n * 2 ** (a - 1));
			o > 0 && await zn(o, e.signal);
		}
		let o = this.terminalSnapshot;
		if (o.status === "successful" && o.result) return o.result;
		let s = o.error;
		throw new Fn(s?.message ?? `Job ended in non-success terminal state: ${o.status}`, o.status, s?.code, s?.details);
	}
	async pollWithinBudget(e, t) {
		if (e.signal === void 0 && e.deadlineMs === void 0) return this.options.poll();
		let n = new AbortController(), r, i, a = new Promise((e, t) => {
			i = t;
		}), o = (e) => {
			n.abort(), i(new Rn(e));
		}, s = () => o("aborted");
		if (e.signal?.addEventListener("abort", s, { once: !0 }), e.deadlineMs !== void 0) {
			let n = Math.max(0, e.deadlineMs - (Date.now() - t));
			r = setTimeout(() => o("deadline"), n);
		}
		try {
			return await Promise.race([this.options.poll(n.signal), a]);
		} catch (t) {
			throw t instanceof Rn && this.throwTimeout(t.reason, e), t;
		} finally {
			r !== void 0 && clearTimeout(r), e.signal?.removeEventListener("abort", s);
		}
	}
	assertWithinBudget(e, t, n) {
		e.signal?.aborted && this.throwTimeout("aborted", e), e.maxAttempts !== void 0 && t >= e.maxAttempts && this.throwTimeout("max-attempts", e), e.deadlineMs !== void 0 && Date.now() - n >= e.deadlineMs && this.throwTimeout("deadline", e);
	}
	throwTimeout(e, t) {
		let n = e === "aborted" ? "poll aborted" : e === "deadline" ? `did not reach a terminal state within ${t.deadlineMs}ms` : `did not reach a terminal state within ${t.maxAttempts} poll attempt(s)`;
		throw new Pn(`Job ${this.options.id} ${n}`, e, this.options.id, this.currentStatus);
	}
}, Rn = class extends Error {
	reason;
	constructor(e) {
		super(e), this.reason = e;
	}
};
function zn(e, t) {
	return new Promise((n) => {
		if (t?.aborted) return n();
		let r = () => {
			clearTimeout(i), n();
		}, i = setTimeout(() => {
			t?.removeEventListener("abort", r), n();
		}, e);
		t?.addEventListener("abort", r, { once: !0 });
	});
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/surfaces.js
var Bn = class {
	client;
	serviceId;
	constructor(e) {
		this.client = e.client, this.serviceId = e.serviceId;
	}
	featureLayer(e) {
		return new Vn({
			client: this.client,
			serviceId: this.serviceId,
			layerId: e
		});
	}
	layer(e) {
		return this.featureLayer(e);
	}
	async featureServiceMetadata(e = {}) {
		return this.client.getFeatureServiceMetadata(this.serviceId, e);
	}
	async mapServiceMetadata(e = {}) {
		return this.client.getMapServiceMetadata(this.serviceId, e);
	}
	async featureLayerIds() {
		return ei(await this.featureServiceMetadata());
	}
	async featureLayers() {
		return (await this.featureLayerIds()).map((e) => new Vn({
			client: this.client,
			serviceId: this.serviceId,
			layerId: e
		}));
	}
	async mapLayerIds() {
		return ei(await this.mapServiceMetadata());
	}
	async mapLayers() {
		return (await this.mapLayerIds()).map((e) => new Un({
			client: this.client,
			serviceId: this.serviceId,
			layerId: e
		}));
	}
	async request(e) {
		return this.client.request({
			...e,
			path: `/rest/services/${_(this.serviceId)}/${Pr(e.path)}`
		});
	}
	mapService() {
		return new Hn({
			client: this.client,
			serviceId: this.serviceId
		});
	}
	mapLayer(e) {
		return new Un({
			client: this.client,
			serviceId: this.serviceId,
			layerId: e
		});
	}
}, Vn = class {
	client;
	serviceId;
	layerId;
	constructor(e) {
		this.client = e.client, this.serviceId = e.serviceId, this.layerId = e.layerId;
	}
	async metadata(e = {}) {
		return this.client.getLayerMetadata(this.serviceId, this.layerId, e);
	}
	createQuery() {
		return {
			where: "1=1",
			outFields: ["*"],
			returnGeometry: !0
		};
	}
	async queryFeatures(e = {}) {
		return this.client.queryFeatures({
			serviceId: this.serviceId,
			layerId: this.layerId,
			...e
		});
	}
	async queryFeaturesAll(e = {}) {
		let t = typeof e.pageSize == "number" && Number.isFinite(e.pageSize) ? Math.max(1, Math.trunc(e.pageSize)) : 2e3, n = typeof e.maxPages == "number" && Number.isFinite(e.maxPages) ? Math.max(1, Math.trunc(e.maxPages)) : 100, r = O(e.resultOffset), i = [], a = r, s;
		for (let r = 0; r < n; r += 1) {
			let n = await this.queryFeatures({
				...e,
				extraParams: Lr(e.extraParams),
				resultOffset: a,
				resultRecordCount: t
			}), r = n.features ?? [];
			if (r.length === 0) break;
			if (this.client.isGrpcWeb) {
				let e = Rr(n, r);
				if (s !== void 0 && e === s) throw new o("queryAll", "grpc", `${this.serviceId}/${this.layerId}`, { context: {
					reason: "gRPC transport returned an identical page after resultOffset advanced; gRPC-aware pagination cannot be honored for this request.",
					resultOffset: a
				} });
				s = e;
			}
			if (i.push(...r), a += r.length, !lt(n) && r.length < t) break;
		}
		return i;
	}
	async *queryFeaturesStream(e = {}) {
		let t = typeof e.pageSize == "number" && Number.isFinite(e.pageSize) ? Math.max(1, Math.trunc(e.pageSize)) : 2e3, n = typeof e.maxPages == "number" && Number.isFinite(e.maxPages) ? Math.max(1, Math.trunc(e.maxPages)) : 100, r = O(e.resultOffset), i, a = r;
		for (let r = 0; r < n; r += 1) {
			let n = await this.queryFeatures({
				...e,
				extraParams: Lr(e.extraParams),
				resultOffset: a,
				resultRecordCount: t
			}), r = n.features ?? [];
			if (r.length === 0) break;
			if (this.client.isGrpcWeb) {
				let e = Rr(n, r);
				if (i !== void 0 && e === i) throw new o("stream", "grpc", `${this.serviceId}/${this.layerId}`, { context: {
					reason: "gRPC transport repeated a page after resultOffset advanced.",
					resultOffset: a
				} });
				i = e;
			}
			if (yield r, a += r.length, !lt(n) && r.length < t) break;
		}
	}
	async queryFeatureCount(e = {}) {
		let t = await this.client.queryFeatures({
			serviceId: this.serviceId,
			layerId: this.layerId,
			where: e.where ?? "1=1",
			returnGeometry: !1,
			method: e.method,
			extraParams: {
				returnCountOnly: !0,
				...e.extraParams
			}
		});
		return E(t) && typeof t.count == "number" && Number.isFinite(t.count) ? t.count : E(t) && Array.isArray(t.features) ? t.features.length : 0;
	}
	async queryObjectIds(e = {}) {
		let t = await this.client.queryFeatures({
			...e,
			serviceId: this.serviceId,
			layerId: this.layerId,
			where: e.where ?? "1=1",
			returnGeometry: !1,
			extraParams: {
				returnIdsOnly: !0,
				...e.extraParams
			}
		});
		return E(t) && Array.isArray(t.objectIds) ? t.objectIds.map((e) => Number(e)).filter((e) => Number.isFinite(e)) : [];
	}
	async queryExtent(e = {}) {
		return Ar(await this.client.queryFeatures({
			serviceId: this.serviceId,
			layerId: this.layerId,
			where: e.where ?? "1=1",
			returnGeometry: !1,
			method: e.method,
			extraParams: {
				returnExtentOnly: !0,
				...e.extraParams
			}
		}));
	}
	async querySpatialAggregation(e) {
		let t = e.sourceId ?? dr(this.serviceId, this.layerId), n = {
			...e,
			sourceId: t
		};
		Be(n);
		let r = fr(n, e.kRingDistance), i = `/rest/services/${_(this.serviceId)}/FeatureServer/${this.layerId}/queryH3`, a = e.method ?? "POST";
		return pr(a === "GET" ? await this.client.request({
			method: a,
			path: i,
			responseFormat: e.responseFormat ?? "json",
			query: r.params,
			signal: e.signal
		}) : await this.client.request({
			method: a,
			path: i,
			responseFormat: e.responseFormat ?? "json",
			headers: { "Content-Type": "application/x-www-form-urlencoded" },
			body: Mr(r.params),
			signal: e.signal
		}), n, r);
	}
	async queryRelatedRecords(e) {
		return this.client.queryRelatedRecords({
			serviceId: this.serviceId,
			layerId: this.layerId,
			...e
		});
	}
	async queryRelatedFeatures(e) {
		return this.queryRelatedRecords(e);
	}
	async applyEdits(e) {
		return this.client.applyEdits({
			serviceId: this.serviceId,
			layerId: this.layerId,
			...e
		});
	}
	async queryAttachments(e = {}) {
		let t = e.method ?? "GET", n = `/rest/services/${_(this.serviceId)}/FeatureServer/${this.layerId}/queryAttachments`, r = {
			...e.objectIds === void 0 ? {} : { objectIds: jr(e.objectIds) },
			...e.where === void 0 ? {} : { where: e.where },
			...e.extraParams ?? {}
		};
		if (t === "GET") return this.client.request({
			method: "GET",
			path: n,
			responseFormat: e.responseFormat ?? "json",
			query: r,
			signal: e.signal
		});
		let i = Mr({
			f: e.responseFormat ?? "json",
			...r
		});
		return this.client.request({
			method: "POST",
			path: n,
			headers: { "Content-Type": "application/x-www-form-urlencoded" },
			body: i,
			signal: e.signal
		});
	}
	async listAttachments(e) {
		return this.client.request({
			method: "GET",
			path: `/rest/services/${_(this.serviceId)}/FeatureServer/${this.layerId}/${e.objectId}/attachments`,
			responseFormat: e.responseFormat ?? "json",
			query: e.extraParams,
			signal: e.signal
		});
	}
	async deleteAttachments(e) {
		let t = Mr({
			f: e.responseFormat ?? "json",
			attachmentIds: jr(e.attachmentIds),
			...e.extraParams ?? {}
		});
		return this.client.request({
			method: "POST",
			path: `/rest/services/${_(this.serviceId)}/FeatureServer/${this.layerId}/${e.objectId}/deleteAttachments`,
			headers: { "Content-Type": "application/x-www-form-urlencoded" },
			body: t,
			signal: e.signal
		});
	}
	async addAttachment(e) {
		let t = Zr(e);
		return this.client.request({
			method: "POST",
			path: `/rest/services/${_(this.serviceId)}/FeatureServer/${this.layerId}/${e.objectId}/addAttachment`,
			responseFormat: e.responseFormat ?? "json",
			query: e.extraParams,
			body: t,
			signal: e.signal
		});
	}
	async updateAttachment(e) {
		let t = Zr(e);
		return t.set("attachmentId", String(e.attachmentId)), this.client.request({
			method: "POST",
			path: `/rest/services/${_(this.serviceId)}/FeatureServer/${this.layerId}/${e.objectId}/updateAttachment`,
			responseFormat: e.responseFormat ?? "json",
			query: e.extraParams,
			body: t,
			signal: e.signal
		});
	}
	async request(e) {
		return this.client.request({
			...e,
			path: `/rest/services/${_(this.serviceId)}/FeatureServer/${this.layerId}/${Nr(e.path)}`
		});
	}
}, Hn = class {
	client;
	serviceId;
	constructor(e) {
		this.client = e.client, this.serviceId = e.serviceId;
	}
	async metadata(e = {}) {
		return this.client.getMapServiceMetadata(this.serviceId, e);
	}
	layer(e) {
		return new Un({
			client: this.client,
			serviceId: this.serviceId,
			layerId: e
		});
	}
	async layerIds() {
		return ei(await this.metadata());
	}
	async layers() {
		return (await this.layerIds()).map((e) => new Un({
			client: this.client,
			serviceId: this.serviceId,
			layerId: e
		}));
	}
	async exportMap(e) {
		return this.client.exportMap({
			serviceId: this.serviceId,
			...e
		});
	}
	async legend(e = {}) {
		return this.client.getMapLegend({
			serviceId: this.serviceId,
			...e
		});
	}
	async getLegend(e = {}) {
		return this.legend(e);
	}
	async identify(e) {
		return this.client.identifyMap({
			serviceId: this.serviceId,
			...e
		});
	}
	async find(e) {
		return this.client.findMap({
			serviceId: this.serviceId,
			...e
		});
	}
	async queryLayer(e) {
		return this.client.queryMapLayer({
			serviceId: this.serviceId,
			...e
		});
	}
	async queryLayerRelatedRecords(e) {
		return this.client.queryMapRelatedRecords({
			serviceId: this.serviceId,
			...e
		});
	}
	async queryLayerRelatedFeatures(e) {
		return this.queryLayerRelatedRecords(e);
	}
	async queryLayerFeaturesAll(e) {
		let t = typeof e.pageSize == "number" && Number.isFinite(e.pageSize) ? Math.max(1, Math.trunc(e.pageSize)) : 2e3, n = typeof e.maxPages == "number" && Number.isFinite(e.maxPages) ? Math.max(1, Math.trunc(e.maxPages)) : 100, r = [], i = 0;
		for (let a = 0; a < n; a += 1) {
			let n = await this.queryLayer({
				...e,
				extraParams: {
					...e.extraParams ?? {},
					resultOffset: i,
					resultRecordCount: t
				}
			}), a = Or(n);
			if (a.length === 0 || (r.push(...a), i += a.length, !lt(n) && a.length < t)) break;
		}
		return r;
	}
	async *queryLayerFeaturesStream(e) {
		let t = typeof e.pageSize == "number" && Number.isFinite(e.pageSize) ? Math.max(1, Math.trunc(e.pageSize)) : 2e3, n = typeof e.maxPages == "number" && Number.isFinite(e.maxPages) ? Math.max(1, Math.trunc(e.maxPages)) : 100, r = 0;
		for (let i = 0; i < n; i += 1) {
			let n = await this.queryLayer({
				...e,
				extraParams: {
					...e.extraParams ?? {},
					resultOffset: r,
					resultRecordCount: t
				}
			}), i = Or(n);
			if (i.length === 0 || (yield i, r += i.length, !lt(n) && i.length < t)) break;
		}
	}
	async queryLayerFeatureCount(e) {
		return Dr(await this.queryLayer({
			layerId: e.layerId,
			where: e.where ?? "1=1",
			returnGeometry: !1,
			outFields: "OBJECTID",
			method: e.method,
			extraParams: {
				returnCountOnly: !0,
				...e.extraParams
			}
		}));
	}
	async queryLayerObjectIds(e) {
		return kr(await this.queryLayer({
			layerId: e.layerId,
			where: e.where ?? "1=1",
			returnGeometry: !1,
			outFields: "OBJECTID",
			method: e.method,
			extraParams: {
				returnIdsOnly: !0,
				...e.extraParams
			}
		}));
	}
	async queryLayerExtent(e) {
		return Ar(await this.queryLayer({
			layerId: e.layerId,
			where: e.where ?? "1=1",
			returnGeometry: !1,
			method: e.method,
			extraParams: {
				returnExtentOnly: !0,
				...e.extraParams
			}
		}));
	}
	async exportImage(e) {
		return this.exportMap(e);
	}
	async request(e) {
		return this.client.request({
			...e,
			path: `/rest/services/${_(this.serviceId)}/MapServer/${Pr(e.path)}`
		});
	}
}, Un = class {
	client;
	serviceId;
	layerId;
	constructor(e) {
		this.client = e.client, this.serviceId = e.serviceId, this.layerId = e.layerId;
	}
	async metadata(e = {}) {
		return this.client.getMapLayerMetadata(this.serviceId, this.layerId, e);
	}
	createQuery() {
		return {
			where: "1=1",
			outFields: ["*"],
			returnGeometry: !0
		};
	}
	async queryFeatures(e = {}) {
		return this.client.queryMapLayer({
			serviceId: this.serviceId,
			layerId: this.layerId,
			...e
		});
	}
	async queryRelatedRecords(e) {
		return this.client.queryMapRelatedRecords({
			serviceId: this.serviceId,
			layerId: this.layerId,
			...e
		});
	}
	async queryRelatedFeatures(e) {
		return this.queryRelatedRecords(e);
	}
	async queryFeaturesAll(e = {}) {
		let t = typeof e.pageSize == "number" && Number.isFinite(e.pageSize) ? Math.max(1, Math.trunc(e.pageSize)) : 2e3, n = typeof e.maxPages == "number" && Number.isFinite(e.maxPages) ? Math.max(1, Math.trunc(e.maxPages)) : 100, r = O(e.resultOffset), i = [], a = r;
		for (let r = 0; r < n; r += 1) {
			let n = await this.queryFeatures({
				...e,
				extraParams: {
					...e.extraParams ?? {},
					resultOffset: a,
					resultRecordCount: t
				}
			}), r = Or(n);
			if (r.length === 0 || (i.push(...r), a += r.length, !lt(n) && r.length < t)) break;
		}
		return i;
	}
	async *queryFeaturesStream(e = {}) {
		let t = typeof e.pageSize == "number" && Number.isFinite(e.pageSize) ? Math.max(1, Math.trunc(e.pageSize)) : 2e3, n = typeof e.maxPages == "number" && Number.isFinite(e.maxPages) ? Math.max(1, Math.trunc(e.maxPages)) : 100, r = O(e.resultOffset);
		for (let i = 0; i < n; i += 1) {
			let n = await this.queryFeatures({
				...e,
				extraParams: {
					...e.extraParams ?? {},
					resultOffset: r,
					resultRecordCount: t
				}
			}), i = Or(n);
			if (i.length === 0 || (yield i, r += i.length, !lt(n) && i.length < t)) break;
		}
	}
	async queryFeatureCount(e = {}) {
		return Dr(await this.queryFeatures({
			where: e.where ?? "1=1",
			returnGeometry: !1,
			outFields: "OBJECTID",
			method: e.method,
			extraParams: {
				returnCountOnly: !0,
				...e.extraParams
			}
		}));
	}
	async queryObjectIds(e = {}) {
		return kr(await this.queryFeatures({
			...e,
			where: e.where ?? "1=1",
			returnGeometry: !1,
			outFields: e.outFields ?? "OBJECTID",
			extraParams: {
				returnIdsOnly: !0,
				...e.extraParams
			}
		}));
	}
	async queryExtent(e = {}) {
		return Ar(await this.queryFeatures({
			where: e.where ?? "1=1",
			returnGeometry: !1,
			method: e.method,
			extraParams: {
				returnExtentOnly: !0,
				...e.extraParams
			}
		}));
	}
	async request(e) {
		return this.client.request({
			...e,
			path: `/rest/services/${_(this.serviceId)}/MapServer/${this.layerId}/${Nr(e.path)}`
		});
	}
};
async function Wn(e, t) {
	if (!(t === void 0 || t === "honua-facade")) return e.resolveOgcFeaturesLayout(t);
}
var Gn = class {
	client;
	layoutMode;
	constructor(e) {
		this.client = e.client, this.layoutMode = e.layout;
	}
	layout() {
		return Wn(this.client, this.layoutMode);
	}
	collection(e) {
		return new Kn({
			client: this.client,
			collectionId: e,
			...this.layoutMode ? { layout: this.layoutMode } : {}
		});
	}
	async landing(e = {}) {
		let t = await this.layout();
		return this.client.getOgcFeaturesLanding({
			...e,
			...t ? { layout: t } : {}
		});
	}
	async conformance(e = {}) {
		let t = await this.layout();
		return this.client.getOgcFeaturesConformance({
			...e,
			...t ? { layout: t } : {}
		});
	}
	async collections(e = {}) {
		let t = await this.layout();
		return this.client.listOgcCollections({
			...e,
			...t ? { layout: t } : {}
		});
	}
	async getCollection(e) {
		let t = await this.layout();
		return this.client.getOgcCollection({
			...e,
			...t ? { layout: t } : {}
		});
	}
	async queryables(e) {
		let t = await this.layout();
		return this.client.getOgcQueryables({
			...e,
			...t ? { layout: t } : {}
		});
	}
	async items(e) {
		let t = await this.layout();
		return this.client.listOgcItems({
			...e,
			...t ? { layout: t } : {}
		});
	}
	async itemsAll(e) {
		let t = zr(e.limit), n = [];
		for await (let r of Wr((t) => this.items(Gr(e, t)), {
			pageSize: Fr(e.pageSize, e.limit),
			maxPages: Ir(e.maxPages),
			startOffset: O(e.offset),
			...t === void 0 ? {} : { totalLimit: t }
		})) n.push(...r);
		return t !== void 0 && n.length > t ? n.slice(0, t) : n;
	}
	async item(e) {
		let t = await this.layout();
		return this.client.getOgcItem({
			...e,
			...t ? { layout: t } : {}
		});
	}
	async createItem(e) {
		let t = await this.layout();
		return this.client.createOgcItem({
			...e,
			...t ? { layout: t } : {}
		});
	}
	async replaceItem(e) {
		let t = await this.layout();
		return this.client.replaceOgcItem({
			...e,
			...t ? { layout: t } : {}
		});
	}
	async patchItem(e) {
		let t = await this.layout();
		return this.client.patchOgcItem({
			...e,
			...t ? { layout: t } : {}
		});
	}
	async deleteItem(e) {
		let t = await this.layout();
		return this.client.deleteOgcItem({
			...e,
			...t ? { layout: t } : {}
		});
	}
}, Kn = class {
	client;
	collectionId;
	layoutMode;
	constructor(e) {
		this.client = e.client, this.collectionId = e.collectionId, this.layoutMode = e.layout;
	}
	layout() {
		return Wn(this.client, this.layoutMode);
	}
	async metadata(e = {}) {
		let t = await this.layout();
		return this.client.getOgcCollection({
			...e,
			collectionId: this.collectionId,
			...t ? { layout: t } : {}
		});
	}
	async queryables(e = {}) {
		let t = await this.layout();
		return this.client.getOgcQueryables({
			...e,
			collectionId: this.collectionId,
			...t ? { layout: t } : {}
		});
	}
	async items(e = {}) {
		let t = await this.layout();
		return this.client.listOgcItems({
			...e,
			collectionId: this.collectionId,
			...t ? { layout: t } : {}
		});
	}
	async itemsAll(e = {}) {
		let t = zr(e.limit), n = [];
		for await (let r of Wr((t) => this.items(Gr(e, t)), {
			pageSize: Fr(e.pageSize, e.limit),
			maxPages: Ir(e.maxPages),
			startOffset: O(e.offset),
			...t === void 0 ? {} : { totalLimit: t }
		})) n.push(...r);
		return t !== void 0 && n.length > t ? n.slice(0, t) : n;
	}
	async *itemsStream(e = {}) {
		yield* Wr((t) => this.items(Gr(e, t)), {
			pageSize: Fr(e.pageSize, e.limit),
			maxPages: Ir(e.maxPages),
			startOffset: O(e.offset)
		});
	}
	async item(e) {
		let t = await this.layout();
		return this.client.getOgcItem({
			...e,
			collectionId: this.collectionId,
			...t ? { layout: t } : {}
		});
	}
	async createItem(e) {
		let t = await this.layout();
		return this.client.createOgcItem({
			...e,
			collectionId: this.collectionId,
			...t ? { layout: t } : {}
		});
	}
	async replaceItem(e) {
		let t = await this.layout();
		return this.client.replaceOgcItem({
			...e,
			collectionId: this.collectionId,
			...t ? { layout: t } : {}
		});
	}
	async patchItem(e) {
		let t = await this.layout();
		return this.client.patchOgcItem({
			...e,
			collectionId: this.collectionId,
			...t ? { layout: t } : {}
		});
	}
	async deleteItem(e) {
		let t = await this.layout();
		return this.client.deleteOgcItem({
			...e,
			collectionId: this.collectionId,
			...t ? { layout: t } : {}
		});
	}
}, qn = class {
	client;
	serviceId;
	constructor(e) {
		this.client = e.client, this.serviceId = e.serviceId;
	}
	async metadata() {
		return this.client.request({
			method: "GET",
			path: `/rest/services/${_(this.serviceId)}/ImageServer`,
			responseFormat: "json"
		});
	}
	async queryRasterCatalog(e = {}) {
		return this.dispatch("query", e, Jn(e));
	}
	async queryRasterCatalogObjectIds(e = {}) {
		let t = await this.dispatch("query", e, {
			...Jn(e),
			where: e.where ?? "1=1",
			returnIdsOnly: !0
		});
		return Array.isArray(t.objectIds) ? t.objectIds.map((e) => Number(e)).filter((e) => Number.isFinite(e)) : [];
	}
	async exportImage(e) {
		return this.dispatch("exportImage", e, Yn(e));
	}
	async identify(e) {
		return this.dispatch("identify", e, Xn(e));
	}
	async dispatch(e, t, n) {
		let r = t.method ?? "GET", i = `/rest/services/${_(this.serviceId)}/ImageServer/${e}`, a = t.responseFormat ?? "json";
		return r === "GET" ? this.client.request({
			method: "GET",
			path: i,
			responseFormat: a,
			query: n,
			signal: t.signal
		}) : this.client.request({
			method: "POST",
			path: i,
			headers: { "Content-Type": "application/x-www-form-urlencoded" },
			body: Mr({
				f: a,
				...n
			}),
			signal: t.signal
		});
	}
	tileUrl(e, t, n, r = "png") {
		let i = `/rest/services/${_(this.serviceId)}/ImageServer/tile/${e}/${t}/${n}`;
		return `${this.client.serverBaseUrl}${i}?f=${r}`;
	}
	async legend() {
		return this.client.request({
			method: "GET",
			path: `/rest/services/${_(this.serviceId)}/ImageServer/legend`,
			responseFormat: "json"
		});
	}
};
function Jn(e) {
	let t = {};
	return e.where !== void 0 && (t.where = e.where), e.outFields !== void 0 && (t.outFields = Array.isArray(e.outFields) ? e.outFields.join(",") : String(e.outFields)), e.objectIds !== void 0 && (t.objectIds = Array.isArray(e.objectIds) ? e.objectIds.join(",") : String(e.objectIds)), e.returnGeometry !== void 0 && (t.returnGeometry = e.returnGeometry), e.outSr !== void 0 && (t.outSR = String(e.outSr)), e.resultOffset !== void 0 && (t.resultOffset = e.resultOffset), e.resultRecordCount !== void 0 && (t.resultRecordCount = e.resultRecordCount), e.geometry !== void 0 && (t.geometry = JSON.stringify(e.geometry)), e.geometryType !== void 0 && (t.geometryType = String(e.geometryType)), e.spatialRel !== void 0 && (t.spatialRel = String(e.spatialRel)), Object.assign(t, e.extraParams ?? {}), t;
}
function Yn(e) {
	let t = {};
	return e.bbox !== void 0 && (t.bbox = Array.isArray(e.bbox) ? e.bbox.join(",") : e.bbox), e.size !== void 0 && (t.size = Array.isArray(e.size) ? e.size.join(",") : e.size), e.format !== void 0 && (t.format = e.format), e.pixelType !== void 0 && (t.pixelType = e.pixelType), e.noData !== void 0 && (t.noData = e.noData), e.interpolation !== void 0 && (t.interpolation = e.interpolation), e.compressionQuality !== void 0 && (t.compressionQuality = e.compressionQuality), e.bandIds !== void 0 && (t.bandIds = Array.isArray(e.bandIds) ? e.bandIds.join(",") : String(e.bandIds)), e.mosaicRule !== void 0 && (t.mosaicRule = typeof e.mosaicRule == "string" ? e.mosaicRule : JSON.stringify(e.mosaicRule)), e.renderingRule !== void 0 && (t.renderingRule = typeof e.renderingRule == "string" ? e.renderingRule : JSON.stringify(e.renderingRule)), e.imageSr !== void 0 && (t.imageSR = String(e.imageSr)), e.bboxSr !== void 0 && (t.bboxSR = String(e.bboxSr)), Object.assign(t, e.extraParams ?? {}), t;
}
function Xn(e) {
	let t = {};
	return t.geometry = typeof e.geometry == "string" ? e.geometry : JSON.stringify(e.geometry), e.geometryType !== void 0 && (t.geometryType = e.geometryType), e.sr !== void 0 && (t.sr = String(e.sr)), e.mosaicRule !== void 0 && (t.mosaicRule = typeof e.mosaicRule == "string" ? e.mosaicRule : JSON.stringify(e.mosaicRule)), e.renderingRule !== void 0 && (t.renderingRule = typeof e.renderingRule == "string" ? e.renderingRule : JSON.stringify(e.renderingRule)), e.pixelSize !== void 0 && (t.pixelSize = Array.isArray(e.pixelSize) ? e.pixelSize.join(",") : e.pixelSize), Object.assign(t, e.extraParams ?? {}), t;
}
var Zn = "/rest/services/Utilities/Geometry/GeometryServer", Qn = class {
	client;
	constructor(e) {
		this.client = e.client;
	}
	async project(e) {
		return this.dispatch("project", e, $n(e));
	}
	async buffer(e) {
		return this.dispatch("buffer", e, er(e));
	}
	async simplify(e) {
		return this.dispatch("simplify", e, tr(e));
	}
	async intersect(e) {
		return this.dispatch("intersect", e, nr(e));
	}
	async union(e) {
		return this.dispatch("union", e, rr(e));
	}
	async clip(e) {
		return this.dispatch("clip", e, nr(e));
	}
	async difference(e) {
		return this.dispatch("difference", e, nr(e));
	}
	async dispatch(e, t, n) {
		let r = t.method ?? "POST", i = `${Zn}/${e}`, a = t.responseFormat ?? "json";
		return r === "GET" ? this.client.request({
			method: "GET",
			path: i,
			responseFormat: a,
			query: n,
			signal: t.signal
		}) : this.client.request({
			method: "POST",
			path: i,
			headers: { "Content-Type": "application/x-www-form-urlencoded" },
			body: Mr({
				f: a,
				...n
			}),
			signal: t.signal
		});
	}
};
function $n(e) {
	let t = {
		geometries: typeof e.geometries == "string" ? e.geometries : JSON.stringify(e.geometries),
		inSR: typeof e.inSr == "string" || typeof e.inSr == "number" ? String(e.inSr) : JSON.stringify(e.inSr),
		outSR: typeof e.outSr == "string" || typeof e.outSr == "number" ? String(e.outSr) : JSON.stringify(e.outSr)
	};
	return Object.assign(t, e.extraParams ?? {}), t;
}
function er(e) {
	let t = {
		geometries: typeof e.geometries == "string" ? e.geometries : JSON.stringify(e.geometries),
		distances: Array.isArray(e.distances) ? e.distances.join(",") : String(e.distances)
	};
	return e.unit !== void 0 && (t.unit = e.unit), e.inSr !== void 0 && (t.inSR = typeof e.inSr == "object" ? JSON.stringify(e.inSr) : String(e.inSr)), e.outSr !== void 0 && (t.outSR = typeof e.outSr == "object" ? JSON.stringify(e.outSr) : String(e.outSr)), e.bufferSr !== void 0 && (t.bufferSR = typeof e.bufferSr == "object" ? JSON.stringify(e.bufferSr) : String(e.bufferSr)), e.unionResults !== void 0 && (t.unionResults = e.unionResults), e.geodesic !== void 0 && (t.geodesic = e.geodesic), Object.assign(t, e.extraParams ?? {}), t;
}
function tr(e) {
	let t = { geometries: typeof e.geometries == "string" ? e.geometries : JSON.stringify(e.geometries) };
	return e.sr !== void 0 && (t.sr = typeof e.sr == "object" ? JSON.stringify(e.sr) : String(e.sr)), Object.assign(t, e.extraParams ?? {}), t;
}
function nr(e) {
	let t = {
		geometries: typeof e.geometries == "string" ? e.geometries : JSON.stringify(e.geometries),
		geometry: typeof e.geometry == "string" ? e.geometry : JSON.stringify(e.geometry),
		sr: typeof e.sr == "object" ? JSON.stringify(e.sr) : String(e.sr)
	};
	return Object.assign(t, e.extraParams ?? {}), t;
}
function rr(e) {
	let t = {
		geometries: typeof e.geometries == "string" ? e.geometries : JSON.stringify(e.geometries),
		sr: typeof e.sr == "object" ? JSON.stringify(e.sr) : String(e.sr)
	};
	return Object.assign(t, e.extraParams ?? {}), t;
}
var ir = class {
	client;
	serviceId;
	taskName;
	constructor(e) {
		this.client = e.client, this.serviceId = e.serviceId, this.taskName = e.taskName;
	}
	async submitJob(e) {
		return this.client.request({
			method: e.method ?? "POST",
			path: this.taskPath("submitJob"),
			responseFormat: e.responseFormat ?? "json",
			query: sr(e),
			signal: e.signal
		});
	}
	async submit(e, t = {}) {
		let n = await this.submitJob(e);
		return new or({
			client: this.client,
			serviceId: this.serviceId,
			taskName: this.taskName,
			jobId: n.jobId,
			initialJob: n,
			resultNames: t.resultNames,
			pollIntervalMs: t.pollIntervalMs
		});
	}
	job(e, t = {}) {
		return new or({
			client: this.client,
			serviceId: this.serviceId,
			taskName: this.taskName,
			jobId: e,
			resultNames: t.resultNames,
			pollIntervalMs: t.pollIntervalMs
		});
	}
	async jobStatus(e, t = {}) {
		return this.client.request({
			method: "GET",
			path: `${this.taskPath("jobs")}/${encodeURIComponent(e)}`,
			responseFormat: "json",
			signal: t.signal
		});
	}
	async cancelJob(e, t = {}) {
		return this.client.request({
			method: "POST",
			path: `${this.taskPath("jobs")}/${encodeURIComponent(e)}/cancel`,
			responseFormat: "json",
			signal: t.signal
		});
	}
	async jobResult(e, t, n = {}) {
		return this.client.request({
			method: "GET",
			path: `${this.taskPath("jobs")}/${encodeURIComponent(e)}/results/${encodeURIComponent(t)}`,
			responseFormat: "json",
			signal: n.signal
		});
	}
	taskPath(e) {
		let t = this.taskName ? `/${encodeURIComponent(this.taskName)}` : "";
		return `/rest/services/${_(this.serviceId)}/GPServer${t}/${e}`;
	}
}, ar = 1e3, or = class {
	id;
	type;
	client;
	serviceId;
	taskName;
	resultNames;
	pollIntervalMs;
	pollFn;
	resultFn;
	cancelFn;
	lifecycle;
	constructor(e) {
		this.client = e.client, this.serviceId = e.serviceId, this.taskName = e.taskName, this.id = e.jobId, this.type = e.taskName ? `${e.serviceId}/${e.taskName}` : e.serviceId, this.resultNames = e.resultNames ?? [], this.pollIntervalMs = e.pollIntervalMs ?? ar, this.pollFn = e.pollFn ?? ((e, t) => new ir({
			client: this.client,
			serviceId: this.serviceId,
			taskName: this.taskName
		}).jobStatus(e, { signal: t })), this.resultFn = e.resultFn ?? ((e, t, n) => new ir({
			client: this.client,
			serviceId: this.serviceId,
			taskName: this.taskName
		}).jobResult(e, t, { signal: n })), this.cancelFn = e.cancelFn ?? ((e, t) => new ir({
			client: this.client,
			serviceId: this.serviceId,
			taskName: this.taskName
		}).cancelJob(e, { signal: t })), this.lifecycle = new Ln({
			id: this.id,
			initialStatus: cr(e.initialJob?.jobStatus),
			initialProgress: lr(e.initialJob),
			pollIntervalMs: this.pollIntervalMs,
			poll: async (e) => this.translateGeoprocessingJob(await this.pollFn(this.id, e), e)
		});
	}
	get status() {
		return this.lifecycle.status;
	}
	get progress() {
		return this.lifecycle.progress;
	}
	async poll() {
		return this.lifecycle.poll();
	}
	watch(e) {
		return this.lifecycle.watch(e);
	}
	async results(e = {}) {
		return this.lifecycle.results(e);
	}
	async cancel() {
		return this.lifecycle.cancel(async () => this.translateGeoprocessingJob(await this.cancelFn(this.id)));
	}
	async translateGeoprocessingJob(e, t) {
		let n = cr(e?.jobStatus), r = lr(e);
		if (n === "successful") return {
			status: n,
			progress: r,
			result: { outputs: await this.resolveOutputs(e, t) }
		};
		if (n === "failed" || n === "dismissed") {
			let t = ur(n, e);
			return {
				status: n,
				progress: r,
				...t ? { error: t } : {}
			};
		}
		return {
			status: n,
			progress: r
		};
	}
	async resolveOutputs(e, t) {
		if (this.resultNames.length === 0) return e?.results ?? {};
		let n = {};
		for (let e of this.resultNames) n[e] = await this.resultFn(this.id, e, t);
		return n;
	}
};
function sr(e) {
	let t = {};
	for (let [n, r] of Object.entries(e.parameters)) t[n] = typeof r == "string" || typeof r == "number" || typeof r == "boolean" ? r : JSON.stringify(r);
	return Object.assign(t, e.extraParams ?? {}), t;
}
function cr(e) {
	let t = (e ?? "esriJobSubmitted").toLowerCase();
	return t.includes("succeeded") || t === "successful" ? "successful" : t.includes("cancelled") || t.includes("canceled") || t.includes("dismissed") ? "dismissed" : t.includes("failed") || t.includes("timedout") || t.includes("timed out") ? "failed" : t.includes("submitted") || t.includes("waiting") || t.includes("accepted") ? "accepted" : Nn(t) ? t : "running";
}
function lr(e) {
	if (!e) return;
	let t = e.messages?.at(-1)?.description;
	return t === void 0 ? void 0 : { message: t };
}
function ur(e, t) {
	let n = t?.messages?.find((e) => /error|failed|cancel/i.test(e.type))?.description ?? t?.messages?.at(-1)?.description;
	return n ? {
		code: e === "dismissed" ? "GeoServicesJobDismissed" : "GeoServicesJobFailed",
		message: n
	} : e === "dismissed" ? {
		code: "GeoServicesJobDismissed",
		message: "GeoServices GP job was dismissed."
	} : {
		code: "GeoServicesJobFailed",
		message: "GeoServices GP job failed."
	};
}
function E(e) {
	return typeof e == "object" && !!e && !Array.isArray(e);
}
function D(e) {
	return typeof e == "number" && Number.isFinite(e);
}
function dr(e, t) {
	return `geoservices-feature-service:${e}/${t}`;
}
function fr(e, t) {
	let n = e.resolution?.indexResolution;
	if (n === void 0) throw Error("FeatureServer queryH3 requires resolution.indexResolution");
	let r = { resolution: n };
	e.where !== void 0 && (r.where = e.where);
	let i = br(t);
	i !== void 0 && (r.kRingDistance = i);
	let a = e.summaries[0];
	if (e.summaries.length === 1 && a?.kind === "count" && a.field === void 0) return {
		params: r,
		resolution: n,
		summaryBindings: [{
			summary: a,
			responseField: "count"
		}]
	};
	let o = [], s = e.summaries.map((e, t) => {
		let n = yr(e, t), r = vr(e);
		return o.push({
			statisticType: e.kind,
			onStatisticField: r,
			outStatisticFieldName: n
		}), {
			summary: e,
			responseField: n
		};
	});
	return r.outStatistics = JSON.stringify(o), {
		params: r,
		resolution: n,
		summaryBindings: s
	};
}
function pr(e, t, n) {
	let r = Or(e).map((e) => mr(e, t, n)), i = wr(r.map((e) => e.extent).filter((e) => e !== void 0)), a = {
		id: t.index?.modelId ?? "h3",
		title: "FeatureServer indexed cells",
		family: "h3",
		cellIdEncoding: "string",
		minResolution: 0,
		maxResolution: 15,
		supportedGeometry: [
			"none",
			"extent",
			"boundary"
		],
		hierarchy: "parent-child",
		spatialReference: e.spatialReference ?? i?.spatialReference ?? { wkid: 4326 }
	}, o = t.summaries.map(_r), s = {
		status: e.exceededTransferLimit === !0 ? "partial" : "complete",
		refinement: e.exceededTransferLimit === !0 ? "append" : void 0,
		loadedCellCount: r.length
	}, c = {
		schemaVersion: Fe,
		sourceId: t.sourceId,
		indexModels: [a],
		summaries: o,
		progressive: s,
		cache: {
			metadataCacheable: !0,
			resultCacheable: !1,
			cacheKeyParts: [
				"geoservices-feature-service",
				t.sourceId,
				"queryH3",
				`resolution=${n.resolution}`,
				`where=${t.where ?? ""}`
			]
		}
	};
	return {
		schemaVersion: Pe,
		requestId: t.requestId,
		sourceId: t.sourceId,
		index: {
			model: a,
			resolution: n.resolution,
			requestedResolution: t.resolution,
			cellCount: r.length,
			extent: i
		},
		metadata: {
			...c,
			widgets: Ve(c)
		},
		cells: r,
		page: e.exceededTransferLimit === !0 ? { loadedCellCount: r.length } : void 0,
		degraded: e.exceededTransferLimit === !0 ? [{
			capability: "spatialAggregate",
			protocol: "geoservices-feature-service",
			sourceId: t.sourceId,
			reason: "FeatureServer queryH3 response exceeded the server transfer limit; returned cells may be partial."
		}] : void 0
	};
}
function mr(e, t, n) {
	let r = e.attributes ?? {}, i = xr(r), a = Sr(e.geometry), o = t.index?.geometry ?? "boundary", s = {
		id: i,
		resolution: n.resolution,
		extent: a,
		summaries: hr(r, n)
	};
	return o === "boundary" && E(e.geometry) ? {
		...s,
		geometry: e.geometry
	} : s;
}
function hr(e, t) {
	let n = {};
	for (let r of t.summaryBindings) n[r.summary.id] = gr(r.summary, e[r.responseField]);
	return n;
}
function gr(e, t) {
	if (e.kind === "count") return {
		kind: "count",
		value: Math.max(0, Tr(t, 0))
	};
	if (e.kind === "sum" || e.kind === "avg" || e.kind === "min" || e.kind === "max") return {
		kind: e.kind,
		value: Er(t),
		unit: e.unit
	};
	throw Error(`FeatureServer queryH3 does not support ${e.kind} summaries.`);
}
function _r(e) {
	return {
		id: e.id,
		kind: e.kind,
		title: e.title,
		field: e.field,
		valueType: e.valueType ?? (e.kind === "count" ? "number" : void 0),
		unit: e.unit
	};
}
function vr(e) {
	if (e.kind === "count") {
		if (e.field === void 0) throw Error("count summaries require a field when queryH3 uses outStatistics.");
		return e.field;
	}
	if (e.kind === "sum" || e.kind === "avg" || e.kind === "min" || e.kind === "max") return e.field;
	throw Error(`FeatureServer queryH3 does not support ${e.kind} summaries.`);
}
function yr(e, t) {
	let n = e.id.replace(/[^A-Za-z0-9_]/g, "_").replace(/^([^A-Za-z_])/, "_$1");
	return `honua_${t + 1}_${n || "summary"}`.slice(0, 63);
}
function br(e) {
	if (e !== void 0) {
		if (!Number.isInteger(e) || e < 0 || e > 20) throw Error("FeatureServer queryH3 kRingDistance must be an integer between 0 and 20.");
		return e;
	}
}
function xr(e) {
	for (let t of [
		"cellIndex",
		"cell_index",
		"h3Index",
		"h3_index"
	]) {
		let n = e[t];
		if (typeof n == "string" || typeof n == "number" || typeof n == "bigint") return String(n);
	}
	throw Error("FeatureServer queryH3 response is missing a cellIndex attribute.");
}
function Sr(e) {
	if (!E(e)) return;
	if (D(e.xmin) && D(e.ymin) && D(e.xmax) && D(e.ymax)) return {
		xmin: e.xmin,
		ymin: e.ymin,
		xmax: e.xmax,
		ymax: e.ymax,
		spatialReference: E(e.spatialReference) ? e.spatialReference : void 0
	};
	let t = [];
	if (Cr(e.rings, t), Cr(e.paths, t), Cr(e.points, t), D(e.x) && D(e.y) && t.push([e.x, e.y]), t.length === 0) return;
	let n = Infinity, r = Infinity, i = -Infinity, a = -Infinity;
	for (let [e, o] of t) n = Math.min(n, e), r = Math.min(r, o), i = Math.max(i, e), a = Math.max(a, o);
	return {
		xmin: n,
		ymin: r,
		xmax: i,
		ymax: a,
		spatialReference: E(e.spatialReference) ? e.spatialReference : void 0
	};
}
function Cr(e, t) {
	if (Array.isArray(e)) {
		if (D(e[0]) && D(e[1])) {
			t.push([e[0], e[1]]);
			return;
		}
		for (let n of e) Cr(n, t);
	}
}
function wr(e) {
	if (e.length === 0) return;
	let [t, ...n] = e;
	return n.reduce((e, t) => ({
		xmin: Math.min(e.xmin, t.xmin),
		ymin: Math.min(e.ymin, t.ymin),
		xmax: Math.max(e.xmax, t.xmax),
		ymax: Math.max(e.ymax, t.ymax),
		spatialReference: e.spatialReference ?? t.spatialReference
	}), t);
}
function Tr(e, t) {
	return Er(e) ?? t;
}
function Er(e) {
	if (D(e)) return e;
	if (typeof e == "string" && e.trim().length > 0) {
		let t = Number(e);
		return Number.isFinite(t) ? t : null;
	}
	return null;
}
function Dr(e) {
	return E(e) && typeof e.count == "number" && Number.isFinite(e.count) ? e.count : E(e) && Array.isArray(e.features) ? e.features.length : 0;
}
function Or(e) {
	return !E(e) || !Array.isArray(e.features) ? [] : e.features;
}
function kr(e) {
	return !E(e) || !Array.isArray(e.objectIds) ? [] : e.objectIds.map((e) => Number(e)).filter((e) => Number.isFinite(e));
}
function Ar(e) {
	if (!E(e)) return { extent: null };
	let t = D(e.count) ? e.count : void 0;
	return {
		extent: E(e.extent) ? e.extent : null,
		count: t
	};
}
function jr(e) {
	return Array.isArray(e) ? e.join(",") : String(e);
}
function Mr(e) {
	let t = new URLSearchParams();
	for (let [n, r] of Object.entries(e)) t.set(n, String(r));
	return t.toString();
}
function Nr(e) {
	return e.startsWith("/") ? e.slice(1) : e;
}
function Pr(e) {
	return e.startsWith("/") ? e.slice(1) : e;
}
function Fr(e, t) {
	return Xr(e) ? e : Xr(t) ? t : 100;
}
function Ir(e) {
	return Xr(e) ? e : 100;
}
function O(e) {
	return typeof e != "number" || !Number.isFinite(e) ? 0 : Math.max(0, Math.trunc(e));
}
function Lr(e) {
	if (!e) return;
	let { resultOffset: t, resultRecordCount: n, ...r } = e;
	return r;
}
function Rr(e, t) {
	let n = e.objectIdFieldName;
	if (n) {
		let e = t.map((e) => e.attributes?.[n]);
		if (e.every((e) => e != null)) return JSON.stringify(e);
	}
	return JSON.stringify(t);
}
function zr(e) {
	if (Xr(e)) return e;
}
var Br = /* @__PURE__ */ new Set(["f", "limit"]), Vr = /* @__PURE__ */ new Set(["offset", "startindex"]), Hr = /* @__PURE__ */ new Set([
	"token",
	"nexttoken",
	"pagetoken",
	"continuationtoken",
	"resumptiontoken",
	"cursor",
	"next",
	"page",
	"searchafter",
	"scroll",
	"scrollid"
]), Ur = "https://placeholder.test";
async function* Wr(e, t) {
	let { pageSize: n, maxPages: r, startOffset: i, totalLimit: a } = t, o = /* @__PURE__ */ new Set(), s = /* @__PURE__ */ new Set(), c, l = 0;
	for (let t = 0; t < r; t += 1) {
		let r = a === void 0 ? n : Math.max(0, a - l);
		if (r < 1) break;
		let u = Math.min(n, r), d = c === void 0 ? i + t * n : void 0, f = await e({
			limit: u,
			offset: d,
			cursorParams: c?.params
		}), p = ti(f);
		if (p.length === 0) break;
		let m = Yr(p);
		if (m !== void 0) {
			if (s.has(m)) break;
			s.add(m);
		}
		yield p, l += p.length;
		let h = Kr(f.links, d);
		if (h !== void 0) {
			if (o.has(h.key)) break;
			o.add(h.key), c = h;
			continue;
		}
		if (c !== void 0 || p.length < u) break;
	}
}
function Gr(e, t) {
	return {
		...e,
		limit: t.limit,
		offset: t.offset,
		...t.cursorParams ? { extraParams: {
			...e.extraParams,
			...t.cursorParams
		} } : {}
	};
}
function Kr(e, t) {
	if (e) for (let n of e) {
		if (n.rel !== "next" || typeof n.href != "string" || typeof n.type == "string" && n.type.includes("html")) continue;
		let e = Jr(n.href, t);
		if (e !== void 0) return e;
	}
}
function qr(e) {
	let t = "";
	for (let n of e) n === "-" || n === "_" || (t += n.toLowerCase());
	return t;
}
function Jr(e, t) {
	let n;
	try {
		n = new URL(e, Ur);
	} catch {
		return;
	}
	let r = [], i = !1;
	for (let [e, t] of n.searchParams) Br.has(e) || (Hr.has(qr(e)) && (i = !0), r.push([e, t]));
	let a = {}, o = !1;
	for (let [e, n] of r) {
		let r = qr(e);
		if (Vr.has(r)) {
			if (i || t !== void 0 && Number(n) === t) continue;
			o = !0;
		} else Hr.has(r) && (o = !0);
		a[e] = n;
	}
	if (o) return {
		key: Object.entries(a).sort(([e], [t]) => e < t ? -1 : +(e > t)).map(([e, t]) => `${e}=${t}`).join("&"),
		params: a
	};
}
function Yr(e) {
	let t = [];
	for (let n of e) {
		let e = n?.id;
		if (typeof e != "string" && typeof e != "number") return;
		t.push(String(e));
	}
	return JSON.stringify(t);
}
function Xr(e) {
	return typeof e == "number" && Number.isFinite(e) && Math.trunc(e) > 0;
}
function Zr(e) {
	let t = new FormData();
	if (e.attachment instanceof Blob) {
		let n = $r(e.attachment, e.contentType), r = e.name ?? Qr(e.attachment);
		return t.set("attachment", n, r), t;
	}
	let n = new Blob([e.attachment], { type: e.contentType ?? "application/octet-stream" });
	return t.set("attachment", n, e.name ?? "attachment.txt"), t;
}
function Qr(e) {
	return "name" in e && typeof e.name == "string" && e.name.length > 0 ? e.name : "attachment.bin";
}
function $r(e, t) {
	return !t || e.type === t ? e : new Blob([e], { type: t });
}
function ei(e) {
	if (!E(e) || !Array.isArray(e.layers)) return [];
	let t = [];
	for (let n of e.layers) {
		if (!E(n)) continue;
		let e = Number(n.id);
		Number.isFinite(e) && t.push(Math.trunc(e));
	}
	return t;
}
function ti(e) {
	return !E(e) || !Array.isArray(e.features) ? [] : e.features;
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/wfs-protocol-error.js
var ni = class extends l {
	reason;
	constructor(e, t, n = {}) {
		super("query.execution.wfs-protocol", t, {
			context: { reason: e },
			..."cause" in n ? { cause: n.cause } : {}
		}), this.reason = e, this.name = "HonuaWfsProtocolError";
	}
}, ri = {
	lt: "<",
	gt: ">",
	amp: "&",
	apos: "'",
	quot: "\""
}, ii = "<![CDATA[", ai = "]]>";
function oi(e) {
	let t = "", n = 0, r = e.length;
	for (; n < r;) {
		let r = e[n];
		if (r === "<" && e.startsWith(ii, n)) {
			let r = e.indexOf(ai, n + 9);
			if (r < 0) {
				t += e.slice(n + 9);
				break;
			}
			t += e.slice(n + 9, r), n = r + 3;
			continue;
		}
		if (r === "&") {
			let r = e.indexOf(";", n + 1);
			if (r > n) {
				let i = si(e.slice(n + 1, r));
				if (i !== void 0) {
					t += i, n = r + 1;
					continue;
				}
			}
		}
		t += r, n += 1;
	}
	return t;
}
function si(e) {
	let t = ri[e];
	if (t !== void 0) return t;
	if (e.length >= 2 && e.charCodeAt(0) === 35) {
		let t = e.charCodeAt(1) === 120 || e.charCodeAt(1) === 88, n = t ? e.slice(2) : e.slice(1);
		if (n === "") return;
		let r = t ? Number.parseInt(n, 16) : Number.parseInt(n, 10);
		if (Number.isNaN(r) || r < 0 || r > 1114111) return;
		try {
			return String.fromCodePoint(r);
		} catch {
			return;
		}
	}
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/capabilities-xml.js
var k = Object.freeze({
	maxBytes: 2 * 1024 * 1024,
	maxElements: 12e3,
	maxDepth: 32,
	maxAttributesPerElement: 64,
	maxAttributeBytes: 64 * 1024,
	maxTextBytes: 512 * 1024
});
function ci(e, t) {
	if (typeof e != "string" || e.length === 0) throw Error(`${t} Capabilities body is empty`);
	if (xi(e) > k.maxBytes) throw Error(`${t} Capabilities exceeds the ${k.maxBytes}-byte XML limit`);
	let n = e.toLowerCase();
	if (n.includes("<!doctype") || n.includes("<!entity")) throw Error(`${t} Capabilities rejects DOCTYPE and ENTITY declarations`);
	let r = +(e.charCodeAt(0) === 65279), i, a = [], o = 0;
	for (; r < e.length;) {
		let n = e.indexOf("<", r);
		if (n < 0) {
			li(a.at(-1), e.slice(r), t), r = e.length;
			break;
		}
		if (li(a.at(-1), e.slice(r, n), t), e.startsWith("<!--", n)) {
			let i = e.indexOf("-->", n + 4);
			if (i < 0) throw Error(`${t} Capabilities contains an unterminated comment`);
			r = i + 3;
			continue;
		}
		if (e.startsWith("<![CDATA[", n)) {
			let i = e.indexOf("]]>", n + 9);
			if (i < 0) throw Error(`${t} Capabilities contains unterminated CDATA`);
			let o = a.at(-1);
			if (!o) throw Error(`${t} Capabilities contains CDATA outside its root element`);
			ui(o, e.slice(n + 9, i), t), r = i + 3;
			continue;
		}
		if (e.startsWith("<?", n)) {
			let i = e.indexOf("?>", n + 2);
			if (i < 0) throw Error(`${t} Capabilities contains an unterminated processing instruction`);
			r = i + 2;
			continue;
		}
		if (e.startsWith("<!", n)) throw Error(`${t} Capabilities contains an unsupported XML declaration`);
		let s = gi(e, n);
		if (s < 0) throw Error(`${t} Capabilities contains an unterminated tag`);
		let c = e.charCodeAt(n + 1) === 47, l = e.slice(n + (c ? 2 : 1), s);
		if (l.length === 0 || P(l.charCodeAt(0))) throw Error(`${t} Capabilities contains an invalid tag`);
		let u = bi(l);
		if (c) {
			let e = a.at(-1);
			if (!e || u !== e.name) throw Error(`${t} Capabilities has mismatched closing tag </${u}>${e ? `; expected </${e.name}>` : ""}`);
			a.pop(), r = s + 1;
			continue;
		}
		let d = l.endsWith("/"), { name: f, attributes: p } = di(d ? bi(u.slice(0, -1)) : u, t);
		if (o += 1, o > k.maxElements) throw Error(`${t} Capabilities exceeds the ${k.maxElements}-element limit`);
		if (a.length + 1 > k.maxDepth) throw Error(`${t} Capabilities exceeds the ${k.maxDepth}-level XML limit`);
		let m = {
			name: f,
			localName: vi(f),
			attributes: p,
			children: [],
			textChunks: [],
			textBytes: 0
		}, h = a.at(-1);
		if (h) h.children.push(m);
		else if (i) throw Error(`${t} Capabilities contains multiple root elements`);
		else i = m;
		d || a.push(m), r = s + 1;
	}
	if (a.length > 0) throw Error(`${t} Capabilities contains an unclosed <${a.at(-1).name}> element`);
	if (!i) throw Error(`${t} Capabilities contains no root element`);
	return Si(i);
}
function A(e, t) {
	return e?.children.find((e) => e.localName === t);
}
function j(e, t) {
	return e?.children.filter((e) => e.localName === t) ?? [];
}
function M(e, t) {
	return e.attributes[t] ?? e.attributes[vi(t)];
}
function N(e) {
	if (!e) return;
	let t = e.text.trim();
	return t.length > 0 ? t : void 0;
}
function li(e, t, n) {
	if (t.length !== 0) {
		if (!e) {
			if (!yi(t)) throw Error(`${n} Capabilities contains text outside its root element`);
			return;
		}
		ui(e, fi(t, n), n);
	}
}
function ui(e, t, n) {
	if (t.length === 0) return;
	mi(t, n);
	let r = xi(t);
	if (r > k.maxTextBytes - e.textBytes) throw Error(`${n} Capabilities element text exceeds the bounded text limit`);
	e.textBytes += r, e.textChunks.push(t);
}
function di(e, t) {
	let n = 0;
	for (; n < e.length && !P(e.charCodeAt(n));) n += 1;
	let r = e.slice(0, n);
	if (!_i(r)) throw Error(`${t} Capabilities contains an invalid element name`);
	let i = Object.create(null), a = /* @__PURE__ */ new Set(), o = 0;
	for (; n < e.length;) {
		for (; n < e.length && P(e.charCodeAt(n));) n += 1;
		if (n >= e.length) break;
		let r = n;
		for (; n < e.length && !P(e.charCodeAt(n)) && e[n] !== "=";) n += 1;
		let s = e.slice(r, n);
		if (!_i(s)) throw Error(`${t} Capabilities contains an invalid attribute name`);
		if (o += 1, o > k.maxAttributesPerElement) throw Error(`${t} Capabilities element exceeds the bounded attribute count`);
		for (; n < e.length && P(e.charCodeAt(n));) n += 1;
		if (e[n] !== "=") throw Error(`${t} Capabilities attribute "${s}" lacks '='`);
		for (n += 1; n < e.length && P(e.charCodeAt(n));) n += 1;
		let c = e[n];
		if (c !== "\"" && c !== "'") throw Error(`${t} Capabilities attribute "${s}" must be quoted`);
		let l = e.indexOf(c, n + 1);
		if (l < 0) throw Error(`${t} Capabilities attribute "${s}" is unterminated`);
		let u = e.slice(n + 1, l);
		if (u.includes("<")) throw Error(`${t} Capabilities attribute "${s}" contains an unescaped '<'`);
		let d = fi(u, t);
		if (xi(d) > k.maxAttributeBytes) throw Error(`${t} Capabilities attribute "${s}" exceeds the bounded value limit`);
		if (Object.hasOwn(i, s)) throw Error(`${t} Capabilities repeats attribute "${s}"`);
		let f = vi(s), p = s === "xmlns" || s.startsWith("xmlns:");
		if (!p && a.has(f)) throw Error(`${t} Capabilities repeats namespace-local attribute "${f}"`);
		if (i[s] = d, p || (a.add(f), s !== f && (i[f] = d)), n = l + 1, n < e.length && !P(e.charCodeAt(n))) throw Error(`${t} Capabilities attributes must be separated by XML whitespace`);
	}
	return {
		name: r,
		attributes: i
	};
}
function fi(e, t) {
	let n = 0;
	for (; n < e.length;) {
		let r = e.indexOf("&", n);
		if (r < 0) break;
		let i = e.indexOf(";", r + 1), a = e.indexOf("&", r + 1);
		if (i < 0 || a >= 0 && a < i) throw Error(`${t} Capabilities contains an unterminated XML entity reference`);
		let o = e.slice(r + 1, i);
		if (!pi(o)) throw Error(`${t} Capabilities contains unsupported XML entity "&${o};"`);
		n = i + 1;
	}
	let r = oi(e);
	return mi(r, t), r;
}
function pi(e) {
	if (e === "lt" || e === "gt" || e === "amp" || e === "apos" || e === "quot") return !0;
	let t = e.startsWith("#x") || e.startsWith("#X") ? /^[0-9A-Fa-f]+$/.test(e.slice(2)) ? Number.parseInt(e.slice(2), 16) : NaN : e.startsWith("#") && /^\d+$/.test(e.slice(1)) ? Number.parseInt(e.slice(1), 10) : NaN;
	return Number.isSafeInteger(t) && hi(t);
}
function mi(e, t) {
	for (let n of e) if (!hi(n.codePointAt(0))) throw Error(`${t} Capabilities contains a character forbidden by XML 1.0`);
}
function hi(e) {
	return e === 9 || e === 10 || e === 13 || e >= 32 && e <= 55295 || e >= 57344 && e <= 65533 || e >= 65536 && e <= 1114111;
}
function gi(e, t) {
	let n;
	for (let r = t + 1; r < e.length; r += 1) {
		let t = e[r];
		if (n) t === n && (n = void 0);
		else if (t === "\"" || t === "'") n = t;
		else if (t === ">") return r;
	}
	return -1;
}
function _i(e) {
	let t = e.split(":");
	return t.length <= 2 && t.every((e) => /^[A-Za-z_][A-Za-z0-9_.-]*$/u.test(e));
}
function vi(e) {
	let t = e.indexOf(":");
	return t < 0 ? e : e.slice(t + 1);
}
function P(e) {
	return e === 32 || e === 9 || e === 10 || e === 13;
}
function yi(e) {
	for (let t = 0; t < e.length; t += 1) if (!P(e.charCodeAt(t))) return !1;
	return !0;
}
function bi(e) {
	let t = e.length;
	for (; t > 0 && P(e.charCodeAt(t - 1));) --t;
	return e.slice(0, t);
}
function xi(e) {
	return new TextEncoder().encode(e).byteLength;
}
function Si(e) {
	let t = Object.freeze(e.children.map(Si));
	return Object.freeze({
		name: e.name,
		localName: e.localName,
		attributes: Object.freeze({ ...e.attributes }),
		children: t,
		text: e.textChunks.join("")
	});
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/wms-axis.js
var Ci = /* @__PURE__ */ new Set([
	4326,
	4258,
	4269,
	4267,
	4203,
	4283,
	7844,
	4490,
	4214,
	4152,
	4759,
	4617,
	4674,
	4618,
	4612,
	4019
]);
function wi(e) {
	let t = e.toUpperCase().lastIndexOf("EPSG");
	if (t < 0) return;
	let n = e.slice(t).match(/\d+/g);
	if (!n || n.length === 0) return;
	let r = Number(n[n.length - 1]);
	return Number.isInteger(r) ? r : void 0;
}
function Ti(e) {
	let t = wi(e);
	return t !== void 0 && Ci.has(t);
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/wms-capabilities.js
var Ei = class extends l {
	constructor(e, t = {}) {
		super("core.wms-capabilities-parse", e, t), this.name = "HonuaWmsCapabilitiesParseError";
	}
};
function Di(e) {
	try {
		let t = ci(e, "WMS");
		if (t.localName !== "WMS_Capabilities" && t.localName !== "WMT_MS_Capabilities") throw Error("missing <WMS_Capabilities> root element (received WMS Capabilities XML?)");
		let n = M(t, "version") ?? "", r = [], i = A(t, "Service"), a = Ii({
			title: N(A(i, "Title")),
			abstract: N(A(i, "Abstract"))
		}), o = A(t, "Capability"), s = Oi(A(o, "Request")), c = (e) => s.find((t) => t.name === e), l = Object.freeze({
			map: c("GetMap")?.formats ?? Object.freeze([]),
			featureInfo: c("GetFeatureInfo")?.formats ?? Object.freeze([]),
			legend: c("GetLegendGraphic")?.formats ?? Object.freeze([])
		}), u = Object.freeze({
			getMap: c("GetMap") !== void 0,
			getFeatureInfo: c("GetFeatureInfo") !== void 0,
			getLegendGraphic: c("GetLegendGraphic") !== void 0,
			operations: s
		}), d = Object.freeze(j(o, "Layer").map((e) => Ai(e, ki(), 0, r)));
		return Object.freeze({
			version: n,
			service: a,
			layers: d,
			formats: l,
			request: u,
			warnings: Object.freeze(Pi(r))
		});
	} catch (e) {
		throw e instanceof Ei ? e : new Ei(e instanceof Error ? e.message : "WMS Capabilities XML is malformed", { cause: e });
	}
}
function Oi(e) {
	return Object.freeze([
		"GetCapabilities",
		"GetMap",
		"GetFeatureInfo",
		"GetLegendGraphic"
	].flatMap((t) => {
		let n = j(e, t);
		if (n.length > 1) throw Error(`WMS Request repeats ${t} operation metadata.`);
		let r = n[0];
		if (!r) return [];
		let i = Pi(j(r, "Format").flatMap((e) => N(e) ?? [])), a = [], o = [], s = [];
		for (let e of j(r, "DCPType")) {
			let t = A(e, "HTTP");
			for (let e of t?.children ?? []) {
				if (e.localName !== "Get" && e.localName !== "Post") continue;
				let t = M(A(e, "OnlineResource") ?? e, "href"), n = e.localName === "Get" ? "GET" : "POST";
				a.includes(n) || a.push(n), t && (n === "GET" ? o : s).push(t);
			}
		}
		return [Object.freeze({
			name: t,
			formats: Object.freeze(i),
			methods: Object.freeze(a),
			getUrls: Object.freeze(Pi(o)),
			postUrls: Object.freeze(Pi(s))
		})];
	}));
}
function ki() {
	return Object.freeze({
		crs: Object.freeze([]),
		bbox: Object.freeze([]),
		styles: Object.freeze([]),
		dimensions: Object.freeze([]),
		queryable: !1
	});
}
function Ai(e, t, n, r) {
	if (n >= 24) throw Error("WMS Capabilities exceeds the 24-level layer hierarchy limit");
	let i = j(e, "CRS").concat(j(e, "SRS")).flatMap((e) => N(e)?.split(/\s+/).filter(Boolean) ?? []), a = Object.freeze(Pi([...t.crs, ...i])), o = Object.freeze(Fi(t.bbox, ji(e, r), (e) => e.crs)), s = Object.freeze(Fi(t.styles, Mi(e, r), (e) => e.name)), c = Object.freeze(Fi(t.dimensions, Ni(e, r), (e) => e.name)), l = M(e, "queryable")?.toLowerCase();
	l !== void 0 && ![
		"0",
		"1",
		"false",
		"true"
	].includes(l) && r.push("WMS layer queryable metadata was malformed and inherited conservatively.");
	let u = l === "1" || l === "true" ? !0 : l === "0" || l === "false" ? !1 : t.queryable, d = {
		crs: a,
		bbox: o,
		styles: s,
		dimensions: c,
		queryable: u
	}, f = Object.freeze(j(e, "Layer").map((e) => Ai(e, d, n + 1, r)));
	return Object.freeze({
		name: N(A(e, "Name")) ?? "",
		...I("title", N(A(e, "Title"))),
		...I("abstract", N(A(e, "Abstract"))),
		crs: a,
		bbox: o,
		styles: s,
		dimensions: c,
		queryable: u,
		children: f
	});
}
function ji(e, t) {
	let n = [];
	for (let r of j(e, "BoundingBox")) {
		let e = M(r, "CRS") ?? M(r, "SRS"), i = F(M(r, "minx")), a = F(M(r, "miny")), o = F(M(r, "maxx")), s = F(M(r, "maxy"));
		e && i !== void 0 && a !== void 0 && o !== void 0 && s !== void 0 && i <= o && a <= s ? n.push(Object.freeze({
			crs: e,
			minx: i,
			miny: a,
			maxx: o,
			maxy: s
		})) : t.push("WMS BoundingBox metadata was malformed and ignored.");
	}
	let r = A(e, "EX_GeographicBoundingBox");
	if (r && !n.some((e) => e.crs === "CRS:84")) {
		let e = F(N(A(r, "westBoundLongitude"))), i = F(N(A(r, "southBoundLatitude"))), a = F(N(A(r, "eastBoundLongitude"))), o = F(N(A(r, "northBoundLatitude")));
		e !== void 0 && i !== void 0 && a !== void 0 && o !== void 0 && e <= a && i <= o && e >= -180 && a <= 180 && i >= -90 && o <= 90 ? n.push(Object.freeze({
			crs: "CRS:84",
			minx: e,
			miny: i,
			maxx: a,
			maxy: o
		})) : t.push("WMS EX_GeographicBoundingBox metadata was malformed and ignored.");
	}
	return n;
}
function Mi(e, t) {
	return j(e, "Style").flatMap((e) => {
		let n = N(A(e, "Name"));
		if (!n) return t.push("WMS Style metadata without a name was ignored."), [];
		let r = A(e, "LegendURL"), i = A(r, "OnlineResource");
		return [Object.freeze({
			name: n,
			...I("title", N(A(e, "Title"))),
			...I("legendUrl", i ? M(i, "href") : void 0),
			...I("legendFormat", N(A(r, "Format")))
		})];
	});
}
function Ni(e, t) {
	return [...j(e, "Dimension"), ...j(e, "Extent")].flatMap((e) => {
		let n = M(e, "name");
		if (!n) return t.push("WMS Dimension metadata without a name was ignored."), [];
		let r = N(e), i = r ? r.split(",").map((e) => e.trim()).filter(Boolean) : [];
		return [Object.freeze({
			name: n,
			...I("units", M(e, "units")),
			...I("default", M(e, "default")),
			values: Object.freeze(Pi(i))
		})];
	});
}
function F(e) {
	if (e === void 0 || e.trim() === "") return;
	let t = Number(e);
	return Number.isFinite(t) ? t : void 0;
}
function Pi(e) {
	return [...new Set(e)];
}
function Fi(e, t, n) {
	let r = new Set(t.map(n));
	return [...t, ...e.filter((e) => !r.has(n(e)))];
}
function I(e, t) {
	return t === void 0 ? {} : { [e]: t };
}
function Ii(e) {
	return Object.freeze(Object.fromEntries(Object.entries(e).filter(([, e]) => e !== void 0)));
}
function* Li(e) {
	let t = [...e.layers];
	for (; t.length > 0;) {
		let e = t.shift();
		e.name.length > 0 && (yield e), t.unshift(...e.children);
	}
}
function Ri(e, t) {
	for (let n of Li(e)) if (n.name === t) return n;
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/wms.js
var zi = class {
	client;
	serviceId;
	capabilitiesPromise;
	constructor(e) {
		this.client = e.client, this.serviceId = e.serviceId;
	}
	layer(e, t) {
		let n = {
			client: this.client,
			serviceId: this.serviceId,
			layerName: e
		};
		return t !== void 0 && (n.defaultStyleId = t), new Bi(n);
	}
	async capabilities(e) {
		return this.client.getWmsCapabilities({
			serviceId: this.serviceId,
			...e?.signal ? { signal: e.signal } : {}
		});
	}
	async map(e) {
		return this.client.getWmsMap({
			serviceId: this.serviceId,
			...e
		});
	}
	async featureInfo(e) {
		return this.client.getWmsFeatureInfo({
			serviceId: this.serviceId,
			...e
		});
	}
	async legend(e, t) {
		if (!(t?.capabilities ?? await this.loadCachedCapabilities()).request.getLegendGraphic) throw new o("legend", "wms", this.serviceId);
		return this.client.getWmsLegend({
			serviceId: this.serviceId,
			...e
		});
	}
	async loadCachedCapabilities() {
		return this.capabilitiesPromise ||= this.capabilities().catch((e) => {
			throw this.capabilitiesPromise = void 0, e;
		}), this.capabilitiesPromise;
	}
}, Bi = class {
	client;
	serviceId;
	layerName;
	defaultStyleId;
	capabilitiesPromise;
	constructor(e) {
		this.client = e.client, this.serviceId = e.serviceId, this.layerName = e.layerName, this.defaultStyleId = e.defaultStyleId;
	}
	async capabilities(e) {
		return this.client.getWmsCapabilities({
			serviceId: this.serviceId,
			...e?.signal ? { signal: e.signal } : {}
		});
	}
	describe(e) {
		return Ri(e, this.layerName);
	}
	stylesIn(e) {
		return this.describe(e)?.styles ?? [];
	}
	async map(e) {
		let { style: t, ...n } = e, r = t ?? this.defaultStyleId;
		return this.client.getWmsMap({
			serviceId: this.serviceId,
			layers: [this.layerName],
			...r === void 0 ? {} : { styles: [r] },
			...n
		});
	}
	async featureInfo(e) {
		let { style: t, ...n } = e, r = t ?? this.defaultStyleId;
		return this.client.getWmsFeatureInfo({
			serviceId: this.serviceId,
			layers: [this.layerName],
			queryLayers: [this.layerName],
			...r === void 0 ? {} : { styles: [r] },
			...n
		});
	}
	async legend(e = {}, t) {
		if (!(t?.capabilities ?? await this.loadCachedCapabilities()).request.getLegendGraphic) throw new o("legend", "wms", this.serviceId);
		let { style: n, ...r } = e, i = n ?? this.defaultStyleId;
		return this.client.getWmsLegend({
			serviceId: this.serviceId,
			layer: this.layerName,
			...i === void 0 ? {} : { style: i },
			...r
		});
	}
	async loadCachedCapabilities() {
		return this.capabilitiesPromise ||= this.capabilities().catch((e) => {
			throw this.capabilitiesPromise = void 0, e;
		}), this.capabilitiesPromise;
	}
};
function Vi(e) {
	return `/rest/services/${_(e)}/MapServer/WMS`;
}
async function Hi(e, t) {
	let n = new URLSearchParams();
	if (n.set("SERVICE", "WMS"), n.set("REQUEST", "GetCapabilities"), n.set("VERSION", t.version ?? "1.3.0"), t.extraParams) for (let [e, r] of Object.entries(t.extraParams)) n.set(e, String(r));
	let r = `${Vi(t.serviceId)}?${n.toString()}`, { text: i } = await e.requestText("GET", r, {
		accept: "text/xml,application/xml",
		signal: t.signal
	});
	return Di(i);
}
async function Ui(e, t) {
	let n = qi(t);
	n.set("REQUEST", "GetMap");
	let r = `${Vi(t.serviceId)}?${n.toString()}`, i = t.format ?? "image/png", a = await e.requestBytes("GET", r, i, void 0, t.signal);
	return {
		bytes: a.bytes,
		contentType: a.contentType
	};
}
async function Wi(e, t) {
	let n = Gi(t), r = n.get("INFO_FORMAT"), i = `${Vi(t.serviceId)}?${n.toString()}`, a = await e.requestBytes("GET", i, r, void 0, t.signal);
	return Ji(a.bytes, a.contentType, r);
}
function Gi(e) {
	let t = qi(e);
	return t.set("REQUEST", "GetFeatureInfo"), t.set("QUERY_LAYERS", e.queryLayers.join(",")), t.set("I", String(Math.trunc(e.i))), t.set("J", String(Math.trunc(e.j))), t.set("INFO_FORMAT", e.infoFormat ?? "application/json"), e.featureCount !== void 0 && t.set("FEATURE_COUNT", String(Math.trunc(e.featureCount))), t;
}
async function Ki(e, t) {
	let n = new URLSearchParams();
	n.set("SERVICE", "WMS"), n.set("VERSION", "1.3.0"), n.set("REQUEST", "GetLegendGraphic"), n.set("LAYER", t.layer), t.style && n.set("STYLE", t.style);
	let r = t.format ?? "image/png";
	if (n.set("FORMAT", r), t.width !== void 0 && n.set("WIDTH", String(Math.trunc(t.width))), t.height !== void 0 && n.set("HEIGHT", String(Math.trunc(t.height))), t.extraParams) for (let [e, r] of Object.entries(t.extraParams)) n.set(e, String(r));
	let i = `${Vi(t.serviceId)}?${n.toString()}`, a = await e.requestBytes("GET", i, r, void 0, t.signal);
	return {
		bytes: a.bytes,
		contentType: a.contentType
	};
}
function qi(e) {
	let t = new URLSearchParams();
	t.set("SERVICE", "WMS"), t.set("VERSION", "1.3.0"), t.set("LAYERS", e.layers.join(",")), t.set("STYLES", e.styles ? e.styles.join(",") : "");
	let n = e.crs ?? "EPSG:3857";
	t.set("CRS", n);
	let [r, i, a, o] = e.bbox, s = Ti(n) ? [
		i,
		r,
		o,
		a
	] : [
		r,
		i,
		a,
		o
	];
	if (t.set("BBOX", s.join(",")), t.set("WIDTH", String(Math.trunc(e.width))), t.set("HEIGHT", String(Math.trunc(e.height))), t.set("FORMAT", e.format ?? "image/png"), t.set("TRANSPARENT", String(e.transparent ?? !0).toUpperCase()), e.bgcolor !== void 0 && t.set("BGCOLOR", e.bgcolor), e.time !== void 0 && t.set("TIME", e.time), e.elevation !== void 0 && t.set("ELEVATION", e.elevation), e.extraParams) for (let [n, r] of Object.entries(e.extraParams)) t.set(n, String(r));
	return t;
}
function Ji(e, t, n) {
	if (!(t.toLowerCase().includes("application/json") || n.toLowerCase().includes("application/json"))) return {
		contentType: t,
		bytes: e
	};
	let r = new TextDecoder("utf-8").decode(e);
	if (r.length === 0) return {
		contentType: t,
		features: []
	};
	let i;
	try {
		i = JSON.parse(r);
	} catch {
		return {
			contentType: t,
			bytes: e
		};
	}
	return {
		contentType: t,
		features: Yi(i)
	};
}
function Yi(e) {
	if (!e || typeof e != "object") return [];
	let t = e, n = Array.isArray(t.features) ? t.features : [], r = [];
	for (let e of n) {
		if (!e || typeof e != "object") continue;
		let t = e, n = t.attributes ?? t.properties ?? {}, i = t.geometry ?? null;
		r.push({
			attributes: n,
			geometry: i
		});
	}
	return r;
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/wms-types.js
var Xi = /* @__PURE__ */ new Map([
	["image/png", "png"],
	["image/jpeg", "jpeg"],
	["image/jpg", "jpeg"],
	["image/webp", "webp"]
]);
function Zi(e) {
	return Xi.get(e.toLowerCase()) ?? "png";
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/wmts-capabilities.js
var Qi = /* @__PURE__ */ new WeakSet(), $i = class extends l {
	constructor(e, t = {}) {
		super("core.wmts-capabilities-parse", e, t), this.name = "HonuaWmtsCapabilitiesParseError";
	}
};
function ea(e) {
	try {
		let t = ci(e, "WMTS");
		if (t.localName !== "Capabilities") throw Error("missing <Capabilities> root element");
		let n = [], r = A(t, "ServiceIdentification"), i = A(t, "Contents"), a = fa({
			title: N(A(r, "Title")),
			abstract: N(A(r, "Abstract"))
		}), o = Object.freeze(j(i, "Layer").flatMap((e) => na(e, n))), s = Object.freeze(j(i, "TileMatrixSet").flatMap((e) => sa(e, n))), c = Object.freeze(ta(A(t, "OperationsMetadata")));
		return Object.freeze({
			version: M(t, "version") ?? "",
			service: a,
			layers: o,
			tileMatrixSets: s,
			operations: c,
			warnings: Object.freeze(L(n))
		});
	} catch (e) {
		throw e instanceof $i ? e : new $i(e instanceof Error ? e.message : "WMTS Capabilities XML is malformed", { cause: e });
	}
}
function ta(e) {
	let t = /* @__PURE__ */ new Set();
	return j(e, "Operation").flatMap((e) => {
		let n = M(e, "name");
		if (!n) return [];
		if (t.has(n)) throw Error(`WMTS repeats ${n} operation metadata.`);
		t.add(n);
		let r = [], i = [], a = [];
		for (let t of j(e, "DCP")) {
			let e = A(t, "HTTP");
			for (let t of e?.children ?? []) {
				if (t.localName !== "Get" && t.localName !== "Post") continue;
				let e = t.localName === "Get" ? "GET" : "POST";
				r.includes(e) || r.push(e);
				let n = M(t, "href");
				n && (e === "GET" ? i : a).push(n);
			}
		}
		return [Object.freeze({
			name: n,
			methods: Object.freeze(r),
			getUrls: Object.freeze(L(i)),
			postUrls: Object.freeze(L(a))
		})];
	});
}
function na(e, t) {
	let n = N(A(e, "Identifier"));
	if (!n) return t.push("WMTS Layer metadata without an identifier was ignored."), [];
	let r = oa(A(e, "WGS84BoundingBox"), t);
	return [Object.freeze({
		identifier: n,
		...R("title", N(A(e, "Title"))),
		...R("abstract", N(A(e, "Abstract"))),
		formats: Object.freeze(L(j(e, "Format").flatMap((e) => N(e) ?? []))),
		infoFormats: Object.freeze(L(j(e, "InfoFormat").flatMap((e) => N(e) ?? []))),
		styles: Object.freeze(j(e, "Style").flatMap((e) => ra(e, t))),
		dimensions: Object.freeze(j(e, "Dimension").flatMap((e) => ia(e, t))),
		tileMatrixSetIds: Object.freeze(L(j(e, "TileMatrixSetLink").flatMap((e) => N(A(e, "TileMatrixSet")) ?? []))),
		resourceTemplates: Object.freeze(aa(e, "tile", t)),
		featureInfoTemplates: Object.freeze(aa(e, "FeatureInfo", t)),
		...r ? { bbox: r } : {}
	})];
}
function ra(e, t) {
	let n = N(A(e, "Identifier"));
	if (!n) return t.push("WMTS Style metadata without an identifier was ignored."), [];
	let r = A(e, "LegendURL"), i = M(e, "isDefault")?.toLowerCase();
	return i !== void 0 && ![
		"0",
		"1",
		"false",
		"true"
	].includes(i) && t.push("WMTS Style isDefault metadata was malformed and treated as false."), [Object.freeze({
		identifier: n,
		...R("title", N(A(e, "Title"))),
		isDefault: i === "true" || i === "1",
		...R("legendUrl", r ? M(r, "href") : void 0),
		...R("legendFormat", r ? M(r, "format") : void 0)
	})];
}
function ia(e, t) {
	let n = N(A(e, "Identifier"));
	if (!n) return t.push("WMTS Dimension metadata without an identifier was ignored."), [];
	let r = N(A(e, "Current"))?.toLowerCase();
	return r !== void 0 && ![
		"0",
		"1",
		"false",
		"true"
	].includes(r) && t.push("WMTS Dimension Current metadata was malformed and treated as false."), [Object.freeze({
		identifier: n,
		...R("default", N(A(e, "Default"))),
		current: r === "true" || r === "1",
		values: Object.freeze(L(j(e, "Value").flatMap((e) => N(e) ?? [])))
	})];
}
function aa(e, t, n) {
	return j(e, "ResourceURL").flatMap((e) => {
		if (M(e, "resourceType")?.toLowerCase() !== t.toLowerCase()) return [];
		let r = M(e, "template"), i = M(e, "format");
		return !r || !i ? (n.push(`WMTS ${t} ResourceURL metadata without a format or template was ignored.`), []) : [Object.freeze({
			format: i,
			template: r
		})];
	});
}
function oa(e, t) {
	if (!e) return;
	let n = la(N(A(e, "LowerCorner"))), r = la(N(A(e, "UpperCorner")));
	if (!n || !r || n[0] > r[0] || n[1] > r[1] || n[0] < -180 || r[0] > 180 || n[1] < -90 || r[1] > 90) {
		t.push("WMTS WGS84BoundingBox metadata was malformed and ignored.");
		return;
	}
	return Object.freeze({
		west: n[0],
		south: n[1],
		east: r[0],
		north: r[1]
	});
}
function sa(e, t) {
	let n = N(A(e, "Identifier")), r = j(e, "TileMatrix"), i = Object.freeze(r.flatMap((e) => ca(e, t)));
	if (!n || i.length === 0) return t.push("WMTS TileMatrixSet metadata without an identifier or valid matrices was ignored."), [];
	let a = Object.freeze({
		identifier: n,
		...R("supportedCrs", N(A(e, "SupportedCRS"))),
		...R("wellKnownScaleSet", N(A(e, "WellKnownScaleSet"))),
		matrices: i
	});
	return i.length !== r.length && Qi.add(a), [a];
}
function ca(e, t) {
	let n = N(A(e, "Identifier")), r = ua(N(A(e, "ScaleDenominator"))), i = da(N(A(e, "TileWidth"))), a = da(N(A(e, "TileHeight"))), o = da(N(A(e, "MatrixWidth"))), s = da(N(A(e, "MatrixHeight"))), c = la(N(A(e, "TopLeftCorner")));
	return !n || r === void 0 || i === void 0 || a === void 0 || o === void 0 || s === void 0 || !c ? (t.push("WMTS TileMatrix metadata with invalid numeric fields was ignored."), []) : [Object.freeze({
		identifier: n,
		scaleDenominator: r,
		tileWidth: i,
		tileHeight: a,
		matrixWidth: o,
		matrixHeight: s,
		topLeftCorner: Object.freeze(c)
	})];
}
function la(e) {
	if (!e) return;
	let t = e.trim().split(/\s+/).map(Number);
	return t.length === 2 && t.every(Number.isFinite) ? [t[0], t[1]] : void 0;
}
function ua(e) {
	if (!e) return;
	let t = Number(e);
	return Number.isFinite(t) && t > 0 ? t : void 0;
}
function da(e) {
	if (!e) return;
	let t = Number(e);
	return Number.isSafeInteger(t) && t > 0 ? t : void 0;
}
function L(e) {
	return [...new Set(e)];
}
function R(e, t) {
	return t === void 0 ? {} : { [e]: t };
}
function fa(e) {
	return Object.freeze(Object.fromEntries(Object.entries(e).filter(([, e]) => e !== void 0)));
}
function pa(e, t) {
	return e.layers.find((e) => e.identifier === t);
}
function ma(e, t) {
	return e.tileMatrixSets.find((e) => e.identifier === t);
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/wmts.js
var ha = class {
	client;
	serviceId;
	constructor(e) {
		this.client = e.client, this.serviceId = e.serviceId;
	}
	layer(e, t) {
		let n = {
			client: this.client,
			serviceId: this.serviceId,
			layerName: e
		};
		return t?.styleId !== void 0 && (n.defaultStyleId = t.styleId), t?.tileMatrixSetId !== void 0 && (n.defaultTileMatrixSetId = t.tileMatrixSetId), new ga(n);
	}
	tileset(e, t, n) {
		return new _a({
			client: this.client,
			serviceId: this.serviceId,
			layerName: e,
			styleId: t,
			tileMatrixSetId: n
		});
	}
	async capabilities(e) {
		return this.client.getWmtsCapabilities({
			serviceId: this.serviceId,
			...e?.signal ? { signal: e.signal } : {}
		});
	}
	async tile(e) {
		return this.client.fetchWmtsTile({
			serviceId: this.serviceId,
			...e
		});
	}
	async featureInfo(e) {
		return this.client.getWmtsFeatureInfo({
			serviceId: this.serviceId,
			...e
		});
	}
}, ga = class {
	client;
	serviceId;
	layerName;
	defaultStyleId;
	defaultTileMatrixSetId;
	constructor(e) {
		this.client = e.client, this.serviceId = e.serviceId, this.layerName = e.layerName, this.defaultStyleId = e.defaultStyleId, this.defaultTileMatrixSetId = e.defaultTileMatrixSetId;
	}
	describe(e) {
		return pa(e, this.layerName);
	}
	tileset(e, t) {
		let n = e ?? this.defaultStyleId ?? "default", r = t ?? this.defaultTileMatrixSetId ?? "WebMercatorQuad";
		return new _a({
			client: this.client,
			serviceId: this.serviceId,
			layerName: this.layerName,
			styleId: n,
			tileMatrixSetId: r
		});
	}
	async tile(e) {
		return this.client.fetchWmtsTile({
			serviceId: this.serviceId,
			layer: this.layerName,
			style: e.style ?? this.defaultStyleId,
			tileMatrixSet: e.tileMatrixSet ?? this.defaultTileMatrixSetId,
			tileMatrix: e.tileMatrix,
			tileRow: e.tileRow,
			tileCol: e.tileCol,
			...e.format === void 0 ? {} : { format: e.format },
			...e.mode === void 0 ? {} : { mode: e.mode },
			...e.signal === void 0 ? {} : { signal: e.signal },
			...e.extraParams === void 0 ? {} : { extraParams: e.extraParams }
		});
	}
	async featureInfo(e) {
		return this.client.getWmtsFeatureInfo({
			serviceId: this.serviceId,
			layer: this.layerName,
			style: e.style ?? this.defaultStyleId,
			tileMatrixSet: e.tileMatrixSet ?? this.defaultTileMatrixSetId,
			tileMatrix: e.tileMatrix,
			tileRow: e.tileRow,
			tileCol: e.tileCol,
			i: e.i,
			j: e.j,
			...e.format === void 0 ? {} : { format: e.format },
			...e.infoFormat === void 0 ? {} : { infoFormat: e.infoFormat },
			...e.mode === void 0 ? {} : { mode: e.mode },
			...e.signal === void 0 ? {} : { signal: e.signal },
			...e.extraParams === void 0 ? {} : { extraParams: e.extraParams }
		});
	}
}, _a = class {
	client;
	serviceId;
	layerName;
	styleId;
	tileMatrixSetId;
	constructor(e) {
		this.client = e.client, this.serviceId = e.serviceId, this.layerName = e.layerName, this.styleId = e.styleId, this.tileMatrixSetId = e.tileMatrixSetId;
	}
	describe(e) {
		return ma(e, this.tileMatrixSetId);
	}
	async tile(e) {
		return this.client.fetchWmtsTile({
			serviceId: this.serviceId,
			layer: this.layerName,
			style: this.styleId,
			tileMatrixSet: this.tileMatrixSetId,
			tileMatrix: e.tileMatrix,
			tileRow: e.tileRow,
			tileCol: e.tileCol,
			...e.format === void 0 ? {} : { format: e.format },
			...e.mode === void 0 ? {} : { mode: e.mode },
			...e.signal === void 0 ? {} : { signal: e.signal },
			...e.extraParams === void 0 ? {} : { extraParams: e.extraParams }
		});
	}
	async featureInfo(e) {
		return this.client.getWmtsFeatureInfo({
			serviceId: this.serviceId,
			layer: this.layerName,
			style: this.styleId,
			tileMatrixSet: this.tileMatrixSetId,
			tileMatrix: e.tileMatrix,
			tileRow: e.tileRow,
			tileCol: e.tileCol,
			i: e.i,
			j: e.j,
			...e.format === void 0 ? {} : { format: e.format },
			...e.infoFormat === void 0 ? {} : { infoFormat: e.infoFormat },
			...e.mode === void 0 ? {} : { mode: e.mode },
			...e.signal === void 0 ? {} : { signal: e.signal },
			...e.extraParams === void 0 ? {} : { extraParams: e.extraParams }
		});
	}
};
function va(e) {
	return `/rest/services/${_(e)}/MapServer/WMTS`;
}
async function ya(e, t) {
	let n = new URLSearchParams();
	n.set("SERVICE", "WMTS"), n.set("REQUEST", "GetCapabilities"), n.set("VERSION", "1.0.0");
	let r = `${va(t.serviceId)}?${n.toString()}`, { text: i } = await e.requestText("GET", r, {
		accept: "text/xml,application/xml",
		signal: t.signal
	});
	return ea(i);
}
async function ba(e, t) {
	let n = t.mode ?? "rest", r = t.format ?? "image/png", i = t.style ?? "default", a = t.tileMatrixSet ?? "WebMercatorQuad";
	if (n === "kvp") {
		let n = new URLSearchParams();
		if (n.set("SERVICE", "WMTS"), n.set("VERSION", "1.0.0"), n.set("REQUEST", "GetTile"), n.set("LAYER", t.layer), n.set("STYLE", i), n.set("FORMAT", r), n.set("TILEMATRIXSET", a), n.set("TILEMATRIX", String(t.tileMatrix)), n.set("TILEROW", String(t.tileRow)), n.set("TILECOL", String(t.tileCol)), t.extraParams) for (let [e, r] of Object.entries(t.extraParams)) n.set(e, String(r));
		let o = `${va(t.serviceId)}?${n.toString()}`;
		return e.requestBytes("GET", o, r, void 0, t.signal);
	}
	let o = Zi(r), s = `${va(t.serviceId)}/${encodeURIComponent(t.layer)}/${encodeURIComponent(i)}/${encodeURIComponent(a)}/${encodeURIComponent(String(t.tileMatrix))}/${encodeURIComponent(String(t.tileRow))}/${encodeURIComponent(String(t.tileCol))}.${o}${Ta(t.extraParams)}`;
	return e.requestBytes("GET", s, r, void 0, t.signal);
}
async function xa(e, t) {
	let n = t.mode ?? "rest", r = t.infoFormat ?? "application/json", i = t.format ?? "image/png", a = t.style ?? "default", o = t.tileMatrixSet ?? "WebMercatorQuad";
	if (n === "kvp") {
		let n = new URLSearchParams();
		if (n.set("SERVICE", "WMTS"), n.set("VERSION", "1.0.0"), n.set("REQUEST", "GetFeatureInfo"), n.set("LAYER", t.layer), n.set("STYLE", a), n.set("FORMAT", i), n.set("TILEMATRIXSET", o), n.set("TILEMATRIX", String(t.tileMatrix)), n.set("TILEROW", String(t.tileRow)), n.set("TILECOL", String(t.tileCol)), n.set("I", String(Math.trunc(t.i))), n.set("J", String(Math.trunc(t.j))), n.set("INFOFORMAT", r), t.extraParams) for (let [e, r] of Object.entries(t.extraParams)) n.set(e, String(r));
		let s = `${va(t.serviceId)}?${n.toString()}`, c = await e.requestBytes("GET", s, r, void 0, t.signal);
		return Ji(c.bytes, c.contentType, r);
	}
	let s = Ca(r), c = `${va(t.serviceId)}/${encodeURIComponent(t.layer)}/${encodeURIComponent(a)}/${encodeURIComponent(o)}/${encodeURIComponent(String(t.tileMatrix))}/${encodeURIComponent(String(t.tileRow))}/${encodeURIComponent(String(t.tileCol))}/${encodeURIComponent(String(Math.trunc(t.j)))}/${encodeURIComponent(String(Math.trunc(t.i)))}.${s}${Ta(t.extraParams)}`, l = await e.requestBytes("GET", c, r, void 0, t.signal);
	return Ji(l.bytes, l.contentType, r);
}
var Sa = /* @__PURE__ */ new Map([
	["application/json", "json"],
	["text/plain", "txt"],
	["text/html", "html"],
	["application/geo+json", "geojson"]
]);
function Ca(e) {
	return Sa.get(e.toLowerCase()) ?? "txt";
}
var wa = /* @__PURE__ */ new Set([
	"service",
	"version",
	"request",
	"layer",
	"style",
	"format",
	"infoformat",
	"tilematrixset",
	"tilematrix",
	"tilerow",
	"tilecol",
	"i",
	"j"
]);
function Ta(e) {
	if (!e) return "";
	let t = new URLSearchParams();
	for (let [n, r] of Object.entries(e)) r != null && (wa.has(n.toLowerCase()) || t.set(n, String(r)));
	let n = t.toString();
	return n.length > 0 ? `?${n}` : "";
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/esri-compat/url.js
var Ea = /^[A-Za-z][A-Za-z\d+.-]*:\/\//, Da = /^\/\//, Oa = "https://honua.invalid", ka = /^(?<prefix>.*)\/rest\/services\/(?<serviceId>[^/]+(?:\/[^/]+)*)\/FeatureServer\/(?<layerId>\d+)\/?$/;
function Aa(e) {
	let { parsed: t, absolute: n } = ja(e), r = t.pathname.match(ka);
	if (!r || !r.groups) throw Error("Invalid FeatureLayer URL. Expected .../rest/services/{serviceId}/FeatureServer/{layerId}");
	let i = decodeURIComponent(r.groups.serviceId), a = Number.parseInt(r.groups.layerId, 10);
	if (Number.isNaN(a)) throw Error("FeatureLayer URL contains an invalid numeric layerId.");
	let o = r.groups.prefix || "";
	return {
		baseUrl: n ? `${t.protocol}//${t.host}${o}`.replace(/\/+$/, "") : o.replace(/\/+$/, ""),
		serviceId: i,
		layerId: a
	};
}
function ja(e) {
	if (Ea.test(e)) return {
		parsed: new URL(e),
		absolute: !0
	};
	if (Da.test(e)) return {
		parsed: new URL(`https:${e}`),
		absolute: !0
	};
	let t = e.startsWith("/") ? e : `/${e}`;
	return {
		parsed: new URL(t, Oa),
		absolute: !1
	};
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/map/feature-service-adapter.js
function Ma(e) {
	let t = [
		"x" in e || "y" in e,
		"points" in e,
		"paths" in e,
		"rings" in e,
		[
			"xmin",
			"ymin",
			"xmax",
			"ymax"
		].some((t) => t in e)
	];
	if (t.filter(Boolean).length !== 1) return !1;
	if (t[0]) return z(e.x) !== void 0 && z(e.y) !== void 0;
	if (t[1]) return Array.isArray(e.points) && e.points.length > 0 && e.points.every((e) => Ia(e) !== void 0);
	if (t[2] || t[3]) {
		let n = t[2] ? e.paths : e.rings, r = t[3];
		return Array.isArray(n) && n.length > 0 && n.every((e) => {
			if (!Array.isArray(e) || e.length < (r ? 4 : 2) || !e.every((e) => Ia(e) !== void 0)) return !1;
			if (!r) return !0;
			let t = Ia(e[0]), n = Ia(e[e.length - 1]);
			return t[0] === n[0] && t[1] === n[1];
		});
	}
	let n = [
		e.xmin,
		e.ymin,
		e.xmax,
		e.ymax
	].map(z);
	return n.every((e) => e !== void 0) && n[0] <= n[2] && n[1] <= n[3];
}
function Na(e, t, n) {
	if (!e || typeof e != "object" || Array.isArray(e)) return null;
	let r;
	if (t == null || t === "esriGeometryNull") r = e;
	else if (t === "esriGeometryPoint" || e.x !== void 0 && e.y !== void 0) r = {
		x: e.x,
		y: e.y,
		z: e.z
	};
	else if (t === "esriGeometryMultipoint") r = { points: e.points };
	else if (t === "esriGeometryPolyline") r = { paths: e.paths };
	else if (t === "esriGeometryPolygon") r = { rings: e.rings };
	else if (t === "esriGeometryEnvelope") r = {
		xmin: e.xmin,
		ymin: e.ymin,
		xmax: e.xmax,
		ymax: e.ymax
	};
	else return null;
	if (!Ma(r)) return null;
	let i = ot({
		...r,
		z: z(r.z)
	});
	if (!i || !("coordinates" in i)) return null;
	let a = z(r.z) !== void 0 || (e.hasZ ?? n?.hasZ) === !0, o = La(i.coordinates, a), s = i.type === "Polygon" ? [o] : i.type === "MultiPolygon" ? o : [];
	for (let e of s) for (let t of e) {
		let e = t[0], n = t[t.length - 1];
		e && n && (e.length !== n.length || e.some((e, t) => e !== n[t])) && t.push([...e]);
	}
	return {
		type: i.type,
		coordinates: o
	};
}
function Pa(e, t, n) {
	return {
		type: "FeatureCollection",
		features: e.map((e) => ({
			type: "Feature",
			geometry: Na(e.geometry ?? null, t, n),
			properties: { ...e.attributes ?? {} }
		}))
	};
}
async function Fa(e, t, n = {}) {
	let r = Aa(t), i = new Vn({
		client: e,
		serviceId: r.serviceId,
		layerId: r.layerId
	}), a = n.outSR ?? 4326, o = {
		where: n.definitionExpression ?? "1=1",
		outFields: n.outFields ?? ["*"],
		returnGeometry: !0,
		outSr: a,
		...n.pageSize === void 0 ? {} : { pageSize: n.pageSize },
		...n.maxPages === void 0 ? {} : { maxPages: n.maxPages },
		...n.signal ? { signal: n.signal } : {}
	}, s = i.metadata(n.signal ? { signal: n.signal } : {}).catch(() => void 0), c = await i.queryFeaturesAll(o), l = await s;
	return {
		type: "geojson",
		data: Pa(c, l?.geometryType, l),
		...n.attribution ? { attribution: n.attribution } : {}
	};
}
function z(e) {
	return typeof e == "number" && Number.isFinite(e) ? e : void 0;
}
function Ia(e) {
	return Array.isArray(e) && e.length >= 2 && z(e[0]) !== void 0 && z(e[1]) !== void 0 ? e : void 0;
}
function La(e, t) {
	return Array.isArray(e) ? Array.isArray(e[0]) ? e.map((e) => La(e, t)) : e.slice(0, t && z(e[2]) !== void 0 ? 3 : 2) : e;
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/geometry-input-sr.js
function Ra(e) {
	if (typeof e == "string") try {
		e = JSON.parse(e);
	} catch {
		return 4326;
	}
	if (typeof e != "object" || !e || !("spatialReference" in e)) return 4326;
	let n = e.spatialReference;
	if (typeof n == "object" && n) {
		if ("wkt" in n && typeof n.wkt == "string" && n.wkt.trim()) return JSON.stringify(n);
		if ("latestWkid" in n && typeof n.latestWkid == "number" && Number.isInteger(n.latestWkid) && n.latestWkid > 0) return n.latestWkid;
		if ("wkid" in n && typeof n.wkid == "number" && Number.isInteger(n.wkid) && n.wkid > 0) return n.wkid;
	}
	throw new t("malformed-geometry", "Query geometry has an invalid spatialReference", {
		operation: "query-input-spatial-reference",
		reason: "invalid-spatial-reference"
	});
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/compatibility.js
var za = "1.0.0", Ba = "preview", Va = 1, Ha = "/api/v1/admin";
function Ua(e) {
	return typeof e == "object" && !!e && !Array.isArray(e);
}
function Wa(e) {
	if (!Ua(e)) throw TypeError("Server capabilities response must be a JSON object.");
	if (e.success === !1) {
		let t = typeof e.message == "string" ? e.message : "Server capabilities request failed.";
		throw Error(t);
	}
	if (!Ua(e.data)) throw TypeError("Server capabilities response is missing a data object.");
	return Ga(e.data.compatibility);
}
function Ga(e) {
	if (!Ua(e)) throw TypeError("Server capabilities response is missing data.compatibility.");
	return {
		serverVersion: Ya(e.serverVersion, "data.compatibility.serverVersion"),
		releaseChannel: Ya(e.releaseChannel, "data.compatibility.releaseChannel"),
		controlPlaneApi: Ka(e.controlPlaneApi),
		metadataSchemas: qa(e.metadataSchemas),
		features: Ja(e.features)
	};
}
function Ka(e) {
	if (!Ua(e)) throw TypeError("Server capabilities response is missing data.compatibility.controlPlaneApi.");
	return {
		major: Xa(e.major, "data.compatibility.controlPlaneApi.major"),
		basePath: Ya(e.basePath, "data.compatibility.controlPlaneApi.basePath"),
		deprecated: B(e.deprecated, "data.compatibility.controlPlaneApi.deprecated")
	};
}
function qa(e) {
	if (!Array.isArray(e)) throw TypeError("Server capabilities response is missing data.compatibility.metadataSchemas.");
	return e.map((e, t) => {
		if (!Ua(e)) throw TypeError(`Server capabilities response metadataSchemas[${t}] must be an object.`);
		return {
			version: Ya(e.version, `data.compatibility.metadataSchemas[${t}].version`),
			deprecated: B(e.deprecated, `data.compatibility.metadataSchemas[${t}].deprecated`)
		};
	});
}
function Ja(e) {
	if (!Ua(e)) throw TypeError("Server capabilities response is missing data.compatibility.features.");
	return {
		metadataResources: B(e.metadataResources, "data.compatibility.features.metadataResources"),
		manifestExport: B(e.manifestExport, "data.compatibility.features.manifestExport"),
		manifestApply: B(e.manifestApply, "data.compatibility.features.manifestApply"),
		manifestDryRun: B(e.manifestDryRun, "data.compatibility.features.manifestDryRun"),
		manifestPrune: B(e.manifestPrune, "data.compatibility.features.manifestPrune")
	};
}
function Ya(e, t) {
	if (typeof e != "string") throw TypeError(`${t} must be a string.`);
	let n = e.trim();
	if (n.length === 0) throw TypeError(`${t} must not be empty.`);
	return n;
}
function B(e, t) {
	if (typeof e != "boolean") throw TypeError(`${t} must be a boolean.`);
	return e;
}
function Xa(e, t) {
	if (typeof e != "number" || !Number.isInteger(e)) throw TypeError(`${t} must be an integer.`);
	return e;
}
function Za(e) {
	let t = [], n = Qa(za), r = Qa(e.serverVersion);
	if (!n) return t.push(`SDK minimum supported version '${za}' is not parseable for compatibility checks.`), t;
	r ? $a(r, n) < 0 && t.push(`Server version ${e.serverVersion} is older than the minimum supported ${za}.`) : t.push(`Server version '${e.serverVersion}' is not parseable for compatibility checks.`), e.controlPlaneApi.major !== Va && t.push(`Control-plane API major ${e.controlPlaneApi.major} is unsupported; expected ${Va}.`), to(e.controlPlaneApi.basePath) !== Ha && t.push(`Control-plane API base path ${e.controlPlaneApi.basePath} is unsupported; expected ${Ha}.`), e.controlPlaneApi.deprecated && t.push(`Control-plane API major ${e.controlPlaneApi.major} is marked deprecated by the server.`);
	let i = no(e.releaseChannel), a = no("preview") ?? 0;
	return i === void 0 ? t.push(`Server release channel '${e.releaseChannel}' is not recognized by this SDK baseline.`) : i < a && t.push(`Server release channel '${e.releaseChannel}' is below the minimum supported '${Ba}'.`), t;
}
function Qa(e) {
	let t = e.trim().replace(/^v/i, "");
	if (t.length === 0) return;
	let [n, r = ""] = (t.split("+", 1)[0] ?? t).split("-", 2), i = n.split(".").map((e) => Number.parseInt(e, 10));
	if (!(i.length === 0 || i.some((e) => !Number.isFinite(e)))) return {
		numbers: i,
		prerelease: r.length > 0 ? r.split(".").map((e) => e.trim()).filter((e) => e.length > 0) : []
	};
}
function $a(e, t) {
	let n = Math.max(e.numbers.length, t.numbers.length);
	for (let r = 0; r < n; r += 1) {
		let n = e.numbers[r] ?? 0, i = t.numbers[r] ?? 0;
		if (n !== i) return n < i ? -1 : 1;
	}
	if (e.prerelease.length === 0 && t.prerelease.length === 0) return 0;
	if (e.prerelease.length === 0) return 1;
	if (t.prerelease.length === 0) return -1;
	let r = Math.max(e.prerelease.length, t.prerelease.length);
	for (let n = 0; n < r; n += 1) {
		let r = e.prerelease[n], i = t.prerelease[n];
		if (r === void 0) return -1;
		if (i === void 0) return 1;
		if (r === i) continue;
		let a = Number.parseInt(r, 10), o = Number.parseInt(i, 10), s = String(a) === r, c = String(o) === i;
		return s && c ? a < o ? -1 : 1 : s ? -1 : c ? 1 : r < i ? -1 : 1;
	}
	return 0;
}
function eo(e) {
	return e instanceof a && e.statusCode === 404 ? "Server does not expose GET /api/v1/admin/capabilities." : e instanceof Error ? e.message : String(e);
}
function to(e) {
	let t = e.trim();
	return t.length === 0 ? "" : ie(t.startsWith("/") ? t : `/${t}`);
}
function no(e) {
	switch (e.trim().toLowerCase()) {
		case "nightly": return 0;
		case "dev": return 1;
		case "alpha": return 2;
		case "preview": return 3;
		case "beta": return 4;
		case "rc": return 5;
		case "stable": return 6;
		case "lts": return 7;
		default: return;
	}
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/geoservices.js
async function ro(e, t, n = {}) {
	let r = `/rest/services?${new URLSearchParams({ f: t }).toString()}`;
	return e.requestCachedMetadataJson(`geoservices:services:${t}`, r, n);
}
async function io(e, t, n, r = {}) {
	let i = new URLSearchParams({ f: "json" }), a = `/rest/services/${_(t)}/FeatureServer/${n}?${i.toString()}`;
	return e.requestCachedMetadataJson(`geoservices-feature:${t}:${n}`, a, r);
}
async function ao(e, t, n = {}) {
	let r = new URLSearchParams({ f: "json" }), i = `/rest/services/${_(t)}/FeatureServer?${r.toString()}`;
	return e.requestCachedMetadataJson(`geoservices-feature:${t}:service`, i, n);
}
async function oo(e, t, n = {}) {
	let r = new URLSearchParams({ f: "json" }), i = `/rest/services/${_(t)}/MapServer?${r.toString()}`;
	return e.requestCachedMetadataJson(`geoservices-map:${t}:service`, i, n);
}
async function so(e, t, n, r = {}) {
	let i = new URLSearchParams({ f: "json" }), a = `/rest/services/${_(t)}/MapServer/${n}?${i.toString()}`;
	return e.requestCachedMetadataJson(`geoservices-map:${t}:${n}`, a, r);
}
function co(e, t, n, r) {
	if (e !== void 0) return e;
	let i = new URL(`${r}${t}?${n.toString()}`, globalThis.location?.href ?? "https://honua.invalid");
	return i.pathname.length + i.search.length > 2e3 ? "POST" : "GET";
}
async function lo(e, t, n) {
	let r = new URLSearchParams();
	r.set("f", n && (t.method === void 0 || t.method === "GET") ? "pbf" : "json"), r.set("where", t.where ?? "1=1"), So(r, t), r.set("returnGeometry", String(t.returnGeometry ?? !0)), Mo(r, t), No(r, t);
	let i = `/rest/services/${_(t.serviceId)}/FeatureServer/${t.layerId}/query`, a = co(t.method, i, r, e.baseUrl), o = n && a === "GET";
	return t.method === void 0 && a === "POST" && r.get("f") === "pbf" && r.set("f", "json"), o ? e.requestBinaryWithJsonFallback("GET", `${i}?${r.toString()}`, r, t.signal) : a === "GET" ? e.requestJson("GET", `${i}?${r.toString()}`, void 0, t.signal) : e.requestJson("POST", i, {
		headers: { "Content-Type": "application/x-www-form-urlencoded" },
		body: r.toString()
	}, t.signal, t.method === void 0 ? { readOnlyQuery: !0 } : void 0);
}
async function uo(e, t) {
	let n = new URLSearchParams();
	n.set("f", "json"), n.set("where", t.where ?? "1=1"), So(n, t), n.set("returnGeometry", String(t.returnGeometry ?? !0)), Mo(n, t), No(n, t);
	let r = `/rest/services/${_(t.serviceId)}/MapServer/${t.layerId}/query`;
	return co(t.method, r, n, e.baseUrl) === "GET" ? e.requestJson("GET", `${r}?${n.toString()}`, void 0, t.signal) : e.requestJson("POST", r, {
		headers: { "Content-Type": "application/x-www-form-urlencoded" },
		body: n.toString()
	}, t.signal, t.method === void 0 ? { readOnlyQuery: !0 } : void 0);
}
async function fo(e, t) {
	let n = `/rest/services/${_(t.serviceId)}/FeatureServer/${t.layerId}/applyEdits`, r = new URLSearchParams();
	return r.set("f", "json"), r.set("rollbackOnFailure", String(t.rollbackOnFailure ?? !0)), t.adds !== void 0 && r.set("adds", Co(t.adds)), t.updates !== void 0 && r.set("updates", Co(t.updates)), t.deletes !== void 0 && r.set("deletes", wo(t.deletes)), e.requestJson("POST", n, {
		headers: { "Content-Type": "application/x-www-form-urlencoded" },
		body: r.toString()
	}, t.signal);
}
async function po(e, t) {
	let n = t.method ?? "GET", r = new URLSearchParams();
	if (r.set("f", "json"), r.set("relationshipId", String(t.relationshipId)), t.objectIds !== void 0 && r.set("objectIds", Array.isArray(t.objectIds) ? t.objectIds.join(",") : String(t.objectIds)), r.set("where", t.where ?? "1=1"), r.set("outFields", yo(t.outFields)), r.set("returnGeometry", String(t.returnGeometry ?? !0)), t.extraParams) for (let [e, n] of Object.entries(t.extraParams)) r.set(e, String(n));
	let i = `/rest/services/${_(t.serviceId)}/FeatureServer/${t.layerId}/queryRelatedRecords`;
	return n === "GET" ? e.requestJson("GET", `${i}?${r.toString()}`, void 0, t.signal) : e.requestJson("POST", i, {
		headers: { "Content-Type": "application/x-www-form-urlencoded" },
		body: r.toString()
	}, t.signal);
}
async function mo(e, t) {
	let n = t.method ?? "GET", r = new URLSearchParams();
	if (r.set("f", "json"), r.set("relationshipId", String(t.relationshipId)), t.objectIds !== void 0 && r.set("objectIds", Array.isArray(t.objectIds) ? t.objectIds.join(",") : String(t.objectIds)), r.set("where", t.where ?? "1=1"), r.set("outFields", yo(t.outFields)), r.set("returnGeometry", String(t.returnGeometry ?? !0)), t.extraParams) for (let [e, n] of Object.entries(t.extraParams)) r.set(e, String(n));
	let i = `/rest/services/${_(t.serviceId)}/MapServer/${t.layerId}/queryRelatedRecords`;
	return n === "GET" ? e.requestJson("GET", `${i}?${r.toString()}`, void 0, t.signal) : e.requestJson("POST", i, {
		headers: { "Content-Type": "application/x-www-form-urlencoded" },
		body: r.toString()
	}, t.signal);
}
async function ho(e, t) {
	let n = t.method ?? "GET", r = new URLSearchParams();
	if (r.set("f", t.responseFormat ?? "json"), r.set("bbox", To(t.bbox)), r.set("size", Eo(t.size)), t.format !== void 0 && r.set("format", t.format), t.dpi !== void 0 && r.set("dpi", String(t.dpi)), t.transparent !== void 0 && r.set("transparent", String(t.transparent)), t.layers !== void 0 && r.set("layers", t.layers), t.bboxSr !== void 0 && r.set("bboxSR", String(t.bboxSr)), t.imageSr !== void 0 && r.set("imageSR", String(t.imageSr)), t.backgroundColor !== void 0 && r.set("backgroundColor", t.backgroundColor), t.extraParams) for (let [e, n] of Object.entries(t.extraParams)) r.set(e, String(n));
	let i = `/rest/services/${_(t.serviceId)}/MapServer/export`;
	return n === "GET" ? e.requestJson("GET", `${i}?${r.toString()}`) : e.requestJson("POST", i, {
		headers: { "Content-Type": "application/x-www-form-urlencoded" },
		body: r.toString()
	});
}
async function go(e, t) {
	let n = new URLSearchParams();
	if (n.set("f", t.responseFormat ?? "json"), t.size !== void 0 && n.set("size", Do(t.size)), t.dynamicLayers !== void 0 && n.set("dynamicLayers", t.dynamicLayers), t.extraParams) for (let [e, r] of Object.entries(t.extraParams)) n.set(e, String(r));
	let r = `/rest/services/${_(t.serviceId)}/MapServer/legend`;
	return e.requestJson("GET", `${r}?${n.toString()}`);
}
async function _o(e, t) {
	let n = t.method ?? "GET", r = new URLSearchParams();
	if (r.set("f", t.responseFormat ?? "json"), r.set("geometry", Oo(t.geometry)), r.set("geometryType", t.geometryType ?? "esriGeometryPoint"), r.set("mapExtent", ko(t.mapExtent)), r.set("imageDisplay", Ao(t.imageDisplay)), r.set("returnGeometry", String(t.returnGeometry ?? !0)), r.set("tolerance", String(t.tolerance ?? 3)), t.sr !== void 0 && r.set("sr", String(t.sr)), t.layers !== void 0 && r.set("layers", t.layers), t.maxAllowableOffset !== void 0 && r.set("maxAllowableOffset", String(t.maxAllowableOffset)), t.layerDefs !== void 0 && r.set("layerDefs", t.layerDefs), t.dynamicLayers !== void 0 && r.set("dynamicLayers", t.dynamicLayers), t.time !== void 0 && r.set("time", t.time), t.extraParams) for (let [e, n] of Object.entries(t.extraParams)) r.set(e, String(n));
	let i = `/rest/services/${_(t.serviceId)}/MapServer/identify`;
	return n === "GET" ? e.requestJson("GET", `${i}?${r.toString()}`) : e.requestJson("POST", i, {
		headers: { "Content-Type": "application/x-www-form-urlencoded" },
		body: r.toString()
	});
}
async function vo(e, t) {
	let n = t.method ?? "GET", r = new URLSearchParams();
	if (r.set("f", t.responseFormat ?? "json"), r.set("searchText", t.searchText), r.set("contains", String(t.contains ?? !0)), t.searchFields !== void 0 && r.set("searchFields", jo(t.searchFields)), t.layers !== void 0 && r.set("layers", t.layers), t.sr !== void 0 && r.set("sr", String(t.sr)), t.layerDefs !== void 0 && r.set("layerDefs", t.layerDefs), t.returnGeometry !== void 0 && r.set("returnGeometry", String(t.returnGeometry)), t.maxAllowableOffset !== void 0 && r.set("maxAllowableOffset", String(t.maxAllowableOffset)), t.dynamicLayers !== void 0 && r.set("dynamicLayers", t.dynamicLayers), t.returnZ !== void 0 && r.set("returnZ", String(t.returnZ)), t.returnM !== void 0 && r.set("returnM", String(t.returnM)), t.gdbVersion !== void 0 && r.set("gdbVersion", t.gdbVersion), t.time !== void 0 && r.set("time", t.time), t.relationParam !== void 0 && r.set("relationParam", t.relationParam), t.extraParams) for (let [e, n] of Object.entries(t.extraParams)) r.set(e, String(n));
	let i = `/rest/services/${_(t.serviceId)}/MapServer/find`;
	return n === "GET" ? e.requestJson("GET", `${i}?${r.toString()}`) : e.requestJson("POST", i, {
		headers: { "Content-Type": "application/x-www-form-urlencoded" },
		body: r.toString()
	});
}
function yo(e) {
	return e === void 0 ? "*" : Array.isArray(e) ? e.length > 0 ? e.join(",") : "" : e;
}
function bo(e) {
	return xo(e, "returnCountOnly") || xo(e, "returnIdsOnly");
}
function xo(e, t) {
	if (e === void 0) return !1;
	let n = e[t];
	if (typeof n == "boolean") return n;
	if (typeof n == "number") return n !== 0;
	if (typeof n != "string") return !1;
	let r = n.trim().toLowerCase();
	return r === "true" || r === "1" || r === "yes";
}
function So(e, t) {
	bo(t.extraParams) || e.set("outFields", yo(t.outFields));
}
function Co(e) {
	return typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function wo(e) {
	return Array.isArray(e) ? e.join(",") : String(e);
}
function To(e) {
	return Array.isArray(e) ? e.join(",") : e;
}
function Eo(e) {
	return Array.isArray(e) ? e.join(",") : e;
}
function Do(e) {
	return typeof e == "number" ? String(e) : Array.isArray(e) ? e.join(",") : e;
}
function Oo(e) {
	return typeof e == "string" ? e : JSON.stringify(e);
}
function ko(e) {
	return Array.isArray(e) ? e.join(",") : e;
}
function Ao(e) {
	return Array.isArray(e) ? e.join(",") : e;
}
function jo(e) {
	return e ? Array.isArray(e) ? e.join(",") : e : "";
}
function Mo(e, t) {
	t.outSr !== void 0 && e.set("outSR", typeof t.outSr == "object" && t.outSr !== null ? JSON.stringify(t.outSr) : String(t.outSr)), t.orderByFields !== void 0 && e.set("orderByFields", t.orderByFields), t.objectIds !== void 0 && e.set("objectIds", Array.isArray(t.objectIds) ? t.objectIds.join(",") : String(t.objectIds)), t.geometry !== void 0 && (e.set("inSR", String(t.extraParams?.inSR ?? Ra(t.geometry))), e.set("geometry", typeof t.geometry == "object" && t.geometry !== null ? JSON.stringify(t.geometry) : String(t.geometry))), t.geometryType !== void 0 && e.set("geometryType", t.geometryType), t.spatialRel !== void 0 && e.set("spatialRel", t.spatialRel), t.distance !== void 0 && e.set("distance", String(t.distance)), t.units !== void 0 && e.set("units", t.units), t.nearestCount !== void 0 && e.set("nearestCount", String(t.nearestCount)), t.returnDistance !== void 0 && e.set("returnDistance", String(t.returnDistance)), t.returnDistinctValues !== void 0 && e.set("returnDistinctValues", String(t.returnDistinctValues)), t.returnCentroid !== void 0 && e.set("returnCentroid", String(t.returnCentroid)), t.groupByFieldsForStatistics !== void 0 && e.set("groupByFieldsForStatistics", t.groupByFieldsForStatistics), t.outStatistics !== void 0 && e.set("outStatistics", Array.isArray(t.outStatistics) ? JSON.stringify(t.outStatistics) : String(t.outStatistics)), t.resultOffset !== void 0 && e.set("resultOffset", String(t.resultOffset)), t.resultRecordCount !== void 0 && e.set("resultRecordCount", String(t.resultRecordCount));
}
function No(e, t) {
	if (!t.extraParams) return;
	let n = bo(t.extraParams);
	for (let [r, i] of Object.entries(t.extraParams)) t.outSr !== void 0 && (r === "outSr" || r === "outSR") || n && r === "outFields" || e.set(r, String(i));
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/ogc-endpoint-layout.js
var Po = "/ogc/features";
function V(e) {
	return encodeURIComponent(String(e));
}
function Fo(e) {
	let t = e.length;
	for (; t > 0 && e.charCodeAt(t - 1) === 47;) t--;
	return e.slice(0, t);
}
function Io(e = Po) {
	let t = Fo(e) || Po;
	return {
		mode: "honua-facade",
		landing: () => t,
		conformance: () => `${t}/conformance`,
		collections: () => `${t}/collections`,
		collection: (e) => `${t}/collections/${V(e)}`,
		queryables: (e) => `${t}/collections/${V(e)}/queryables`,
		items: (e) => `${t}/collections/${V(e)}/items`,
		item: (e, n) => `${t}/collections/${V(e)}/items/${V(n)}`
	};
}
function Lo(e) {
	let t = Fo(e.landingUrl), n = Fo(e.collectionsUrl), r = Fo(e.conformanceUrl);
	return {
		mode: "ogc-api",
		landing: () => t,
		conformance: () => r,
		collections: () => n,
		collection: (e) => `${n}/${V(e)}`,
		queryables: (e) => `${n}/${V(e)}/queryables`,
		items: (e) => `${n}/${V(e)}/items`,
		item: (e, t) => `${n}/${V(e)}/items/${V(t)}`
	};
}
function Ro(e, t) {
	if (/^https?:\/\//i.test(e)) return e;
	let n = t.endsWith("/") ? t : `${t}/`;
	try {
		return new URL(e, n).toString();
	} catch {
		return e;
	}
}
function zo(e, ...t) {
	if (!e) return;
	let n = new Set(t.map((e) => e.toLowerCase()));
	for (let t of e) {
		if (typeof t.href != "string" || t.href.length === 0) continue;
		let e = (t.rel ?? "").toLowerCase();
		if (n.has(e)) return t.href;
		let r = e.slice(e.lastIndexOf("/") + 1);
		if (r && n.has(r)) return t.href;
	}
}
async function Bo(e) {
	let t = await e.requestCachedMetadataJson("ogc-features:layout:landing", "?f=json", {}), n = Fo(e.baseUrl), r = t?.links, i = zo(r, "data"), a = zo(r, "conformance");
	return Lo({
		landingUrl: n,
		collectionsUrl: i ? Ro(i, n) : `${n}/collections`,
		conformanceUrl: a ? Ro(a, n) : `${n}/conformance`
	});
}
async function Vo(e) {
	try {
		let t = await e.requestCachedMetadataJson("ogc-features:layout:facade-probe", `${Po}?f=json`, {});
		return Array.isArray(t?.links);
	} catch {
		return !1;
	}
}
async function Ho(e, t = "honua-facade") {
	if (t === "honua-facade" || t === "auto" && await Vo(e)) return Io();
	try {
		return await Bo(e);
	} catch (t) {
		throw t instanceof o ? t : new o("query", "ogc-features", `ogc-api layout discovery failed for ${e.baseUrl}: ${t instanceof Error ? t.message : String(t)}`);
	}
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/ogc-features.js
function H(e) {
	return e.layout ?? Io();
}
function Uo(e) {
	return e.mode === "honua-facade" ? "facade" : e.collections();
}
async function Wo(e, t = {}) {
	let n = C(t), r = H(t);
	return e.requestCachedMetadataJson(`ogc-features:landing:${Uo(r)}:${n.toString()}`, `${r.landing()}?${n.toString()}`, t);
}
async function Go(e, t = {}) {
	let n = C(t), r = H(t);
	return e.requestCachedMetadataJson(`ogc-features:conformance:${Uo(r)}:${n.toString()}`, `${r.conformance()}?${n.toString()}`, t);
}
async function Ko(e, t = {}) {
	let n = C(t), r = H(t);
	return e.requestCachedMetadataJson(`ogc-features:collections:${Uo(r)}:${n.toString()}`, `${r.collections()}?${n.toString()}`, t);
}
async function qo(e, t) {
	let n = C(t), r = H(t);
	return e.requestCachedMetadataJson(`ogc-features:collection:${Uo(r)}:${t.collectionId}:${n.toString()}`, `${r.collection(t.collectionId)}?${n.toString()}`, t);
}
async function Jo(e, t) {
	let n = C(t), r = H(t);
	return e.requestCachedMetadataJson(`ogc-features:queryables:${Uo(r)}:${t.collectionId}:${n.toString()}`, `${r.queryables(t.collectionId)}?${n.toString()}`, t);
}
async function Yo(e, t) {
	let n = C(t);
	t.limit !== void 0 && n.set("limit", String(t.limit)), t.offset !== void 0 && n.set("offset", String(t.offset)), t.bbox !== void 0 && n.set("bbox", t.bbox), t.datetime !== void 0 && n.set("datetime", t.datetime), t.filter !== void 0 && n.set("filter", t.filter), t.filterLang !== void 0 && n.set("filter-lang", t.filterLang), t.ids !== void 0 && n.set("ids", st(t.ids)), t.properties !== void 0 && n.set("properties", st(t.properties)), t.sortby !== void 0 && n.set("sortby", t.sortby), t.crs !== void 0 && n.set("crs", t.crs);
	let r = H(t).items(t.collectionId);
	return e.requestJson("GET", `${r}?${n.toString()}`, void 0, t.signal);
}
async function Xo(e, t) {
	let n = C(t);
	t.crs !== void 0 && n.set("crs", t.crs);
	let r = H(t).item(t.collectionId, t.featureId);
	return e.requestJson("GET", `${r}?${n.toString()}`, void 0, t.signal);
}
async function Zo(e, t) {
	let n = C(t), r = H(t).items(t.collectionId);
	return e.requestJson("POST", `${r}?${n.toString()}`, {
		headers: S({ "Content-Type": "application/geo+json" }, t.headers),
		body: JSON.stringify(t.feature)
	}, t.signal);
}
async function Qo(e, t) {
	let n = C(t);
	t.crs !== void 0 && n.set("crs", t.crs);
	let r = H(t).item(t.collectionId, t.featureId);
	return e.requestJson("PUT", `${r}?${n.toString()}`, {
		headers: S({ "Content-Type": "application/geo+json" }, t.headers),
		body: JSON.stringify(t.feature)
	}, t.signal);
}
async function $o(e, t) {
	let n = C(t);
	t.crs !== void 0 && n.set("crs", t.crs);
	let r = H(t).item(t.collectionId, t.featureId);
	return e.requestJson("PATCH", `${r}?${n.toString()}`, {
		headers: S({ "Content-Type": "application/merge-patch+json" }, t.headers),
		body: JSON.stringify(t.patch)
	}, t.signal);
}
async function es(e, t) {
	let n = C(t);
	t.crs !== void 0 && n.set("crs", t.crs);
	let r = H(t).item(t.collectionId, t.featureId);
	await e.requestJson("DELETE", `${r}?${n.toString()}`, void 0, t.signal);
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/ogc-processes.js
var ts = 1e3, ns = "/ogc/processes", rs = {
	core: "http://www.opengis.net/spec/ogcapi-processes-1/1.0/conf/core",
	dismiss: "http://www.opengis.net/spec/ogcapi-processes-1/1.0/conf/dismiss"
}, is = {
	core: "processes-1/1.0/conf/core",
	dismiss: "processes-1/1.0/conf/dismiss"
}, as = {
	results: "http://www.opengis.net/def/rel/ogc/1.0/results",
	status: "self"
}, os = {
	sync: "sync-execute",
	async: "async-execute",
	dismiss: "dismiss"
};
function ss(e, t, n) {
	return new o(e, "ogc-processes", t, { context: n });
}
function cs(e, t) {
	for (let n of e?.conformsTo ?? []) {
		if (typeof n != "string") continue;
		let e = (n.split("#")[0] ?? "").split("?")[0] ?? "", r = e.length;
		for (; r > 0 && e.charCodeAt(r - 1) === 47;) --r;
		if (e = e.slice(0, r), e.endsWith(t)) return !0;
	}
	return !1;
}
function ls(e, t) {
	if (!e) return !1;
	for (let n of e) if (typeof n == "string" && n.toLowerCase() === t) return !0;
	return !1;
}
function U(e) {
	if (e.basePath === void 0) return ns;
	let t = e.basePath, n = t.length;
	for (; n > 0 && t.charCodeAt(n - 1) === 47;) n--;
	return t.slice(0, n);
}
function us(e) {
	let t = U(e);
	return t === ns ? "" : `${encodeURIComponent(t)}:`;
}
function ds(e) {
	return /^https?:\/\//i.test(e);
}
function fs(e, t) {
	if (ds(e)) try {
		return new URL(`${e}${t}`).toString();
	} catch {
		return;
	}
}
function ps(e, t, n) {
	if (typeof e != "string" || e.length === 0 || !ds(n)) return;
	let r;
	try {
		r = new URL(e, t ?? `${n}/`);
	} catch {
		return;
	}
	let i;
	try {
		i = new URL(n).origin;
	} catch {
		return;
	}
	if (r.origin === i) return `${r.pathname}${r.search}`;
}
function ms(e, t) {
	return `${U({ ...e === void 0 ? {} : { basePath: e } })}/jobs/${encodeURIComponent(t)}`;
}
function hs(e, t) {
	let n = e.indexOf("?");
	if (n < 0) {
		let n = t.toString();
		return n.length === 0 ? e : `${e}?${n}`;
	}
	let r = new URLSearchParams(e.slice(n + 1)), i = new URLSearchParams();
	for (let [e, n] of t) r.has(e) || i.append(e, n);
	let a = i.toString();
	return a.length === 0 ? e : `${e}&${a}`;
}
function gs(e, t, n, ...r) {
	if (!e) return;
	let i = /* @__PURE__ */ new Set();
	for (let e of r) {
		let t = e.toLowerCase();
		i.add(t), i.add(t.slice(t.lastIndexOf("/") + 1));
	}
	for (let r of e) {
		let e = (r.rel ?? "").toLowerCase(), a = e.slice(e.lastIndexOf("/") + 1);
		if (!i.has(e) && !(a && i.has(a))) continue;
		let o = ps(r.href, t, n);
		if (o) return o;
	}
}
var _s = class {
	client;
	basePath;
	capabilityPolicy;
	pollBudget;
	declaredConformance;
	conformancePromise;
	jobControlByProcess = /* @__PURE__ */ new Map();
	constructor(e) {
		this.client = e.client, this.basePath = e.basePath, this.capabilityPolicy = e.capabilityPolicy ?? "advertised", this.pollBudget = e.pollBudget, this.declaredConformance = e.conformance;
	}
	withBase(e) {
		return this.basePath === void 0 ? e : {
			...e,
			basePath: this.basePath
		};
	}
	async landing(e = {}) {
		return this.client.getOgcProcessesLanding(this.withBase(e));
	}
	async conformance(e = {}) {
		let t = await this.client.getOgcProcessesConformance(this.withBase(e));
		return Array.isArray(t?.conformsTo) && (this.declaredConformance = t), t;
	}
	async list(e = {}) {
		let t = await this.client.listOgcProcesses(this.withBase(e));
		for (let e of t?.processes ?? []) e?.id && this.rememberJobControl(e.id, e.jobControlOptions, "summary");
		return t;
	}
	async describe(e, t = {}) {
		let n = await this.client.getOgcProcess(this.withBase({
			...t,
			processId: e
		}));
		return this.rememberJobControl(e, n?.jobControlOptions, "description"), n;
	}
	rememberJobControl(e, t, n) {
		n === "summary" && this.jobControlByProcess.get(e)?.source === "description" || this.jobControlByProcess.set(e, {
			options: [...t ?? []],
			source: n
		});
	}
	async execute(e) {
		let t = this.withBase(e), n = await this.assertExecutionDeclared(e), r = await this.client.executeOgcProcess(t), i = r.processID ?? e.processId, a = e.jobControlOptions ?? this.jobControlByProcess.get(e.processId)?.options;
		return r.synchronous === !0 ? new ys(i, r.results ?? {}) : new vs({
			client: this.client,
			jobId: r.jobID,
			processId: i,
			initialStatus: r.statusInfo ?? {
				jobID: r.jobID,
				processID: i,
				status: r.status
			},
			...t.basePath === void 0 ? {} : { basePath: t.basePath },
			...r.statusPath === void 0 ? {} : { statusPath: r.statusPath },
			...this.pollBudget === void 0 ? {} : { pollBudget: this.pollBudget },
			...n === void 0 ? {} : { conformance: n },
			...a === void 0 ? {} : { jobControlOptions: a },
			capabilityPolicy: this.capabilityPolicy
		});
	}
	job(e, t = {}) {
		let { basePath: n } = this.withBase(t), r = t.processId ? this.jobControlByProcess.get(t.processId)?.options : void 0;
		return new vs({
			client: this.client,
			jobId: e,
			processId: t.processId,
			...n === void 0 ? {} : { basePath: n },
			...t.statusPath === void 0 ? {} : { statusPath: t.statusPath },
			...t.resultsPath === void 0 ? {} : { resultsPath: t.resultsPath },
			...this.pollBudget === void 0 ? {} : { pollBudget: this.pollBudget },
			...this.declaredConformance === void 0 ? {} : { conformance: this.declaredConformance },
			...r === void 0 ? {} : { jobControlOptions: r },
			capabilityPolicy: this.capabilityPolicy
		});
	}
	async resolveConformance(e) {
		if (this.declaredConformance) return this.declaredConformance;
		if (this.capabilityPolicy === "strict") {
			if (!this.conformancePromise) {
				let t = e ? { signal: e } : {};
				this.conformancePromise = this.client.getOgcProcessesConformance(this.withBase(t)).then((e) => (this.declaredConformance = e, e)).catch((e) => {
					throw this.conformancePromise = void 0, e;
				});
			}
			return this.conformancePromise;
		}
	}
	async assertExecutionDeclared(e) {
		let t = await this.resolveConformance(e.signal);
		if ((t?.conformsTo !== void 0 || this.capabilityPolicy === "strict") && !cs(t, is.core)) throw ss("processes.execute", e.processId, {
			missingClass: rs.core,
			construct: "execute"
		});
		let n = e.mode ?? "auto";
		if (n === "auto") return t;
		let r = e.jobControlOptions ?? this.jobControlByProcess.get(e.processId)?.options;
		if (!r && this.capabilityPolicy === "strict" && (r = (await this.describe(e.processId, e.signal ? { signal: e.signal } : {})).jobControlOptions ?? []), !r) return t;
		let i = n === "sync" ? os.sync : os.async;
		if (!ls(r, i)) throw ss(`processes.${i}`, e.processId, {
			construct: i,
			declaredJobControlOptions: r.join(",")
		});
		return t;
	}
}, vs = class {
	id;
	type;
	client;
	basePath;
	pollIntervalMs;
	pollFn;
	pollBudget;
	conformance;
	jobControlOptions;
	capabilityPolicy;
	statusPath;
	resultsPath;
	lifecycle;
	constructor(e) {
		this.client = e.client, this.id = e.jobId, this.type = e.processId ?? e.initialStatus?.processID ?? "unknown", this.basePath = e.basePath, this.pollIntervalMs = e.pollIntervalMs ?? ts, this.pollBudget = e.pollBudget, this.conformance = e.conformance, this.jobControlOptions = e.jobControlOptions, this.capabilityPolicy = e.capabilityPolicy ?? "advertised", this.statusPath = e.statusPath, this.resultsPath = e.resultsPath, this.pollFn = e.pollFn ?? ((e, t) => this.fetchStatus(e, t));
		let t = e.initialStatus;
		this.absorbLinks(t?.links), this.lifecycle = new Ln({
			id: this.id,
			initialStatus: t?.status ?? "accepted",
			initialProgress: Ss(t),
			pollIntervalMs: () => this.pollIntervalMs,
			pollBudget: this.pollBudget,
			poll: async (e) => this.translateOgcStatus(await this.pollFn(this.id, e))
		});
	}
	async fetchStatus(e, t) {
		if (this.capabilityPolicy === "strict" && this.statusPath === void 0) throw ss("processes.jobStatusLink", this.type, {
			construct: "job status route",
			linkRelation: as.status
		});
		return this.client.getOgcProcessJob({
			jobId: e,
			...t ? { signal: t } : {},
			...this.basePath === void 0 ? {} : { basePath: this.basePath },
			...this.statusPath === void 0 ? {} : { routePath: this.statusPath }
		});
	}
	absorbLinks(e) {
		if (!e || e.length === 0) return;
		let t = this.client.serverBaseUrl, n = fs(t, this.statusPath ?? ms(this.basePath, this.id)), r = gs(e, n, t, as.results);
		if (r && (this.resultsPath = r), this.statusPath === void 0) {
			let r = gs(e, n, t, as.status, "status", "monitor");
			r && (this.statusPath = r);
		}
	}
	get status() {
		return this.lifecycle.status;
	}
	get progress() {
		return this.lifecycle.progress;
	}
	async poll() {
		return this.lifecycle.poll();
	}
	watch(e) {
		return this.lifecycle.watch(e);
	}
	async results(e = {}) {
		return this.lifecycle.results(e);
	}
	async cancel() {
		return this.lifecycle.terminal ? this.lifecycle.status : (this.assertDismissDeclared(), this.lifecycle.cancel(() => this.cancelSnapshot()));
	}
	async cancelSnapshot() {
		try {
			let e = await this.client.cancelOgcProcessJob(this.jobRequest(this.statusPath));
			return this.translateOgcStatus(e);
		} catch (e) {
			let t = e?.statusCode;
			if (t === 404) return {
				status: this.lifecycle.status,
				progress: this.lifecycle.progress
			};
			if (t === 409 && bs(e)) {
				let t;
				try {
					t = await this.pollFn(this.id);
				} catch {
					throw e;
				}
				let n = await this.translateOgcStatus(t);
				if (!Nn(n.status)) throw e;
				return n;
			}
			throw e;
		}
	}
	async translateOgcStatus(e) {
		let t = e.status ?? "accepted", n = Ss(e);
		if (this.absorbLinks(e.links), t === "successful") try {
			if (this.capabilityPolicy === "strict" && this.resultsPath === void 0) throw ss("processes.jobResultsLink", this.type, {
				construct: "job results route",
				linkRelation: as.results
			});
			return {
				status: "successful",
				progress: n,
				result: { outputs: await this.client.getOgcProcessJobResults(this.jobRequest(this.resultsPath)) }
			};
		} catch (e) {
			if (e instanceof o) throw e;
			return {
				status: "failed",
				progress: n,
				error: {
					code: e?.name ?? "ResultsFetchFailed",
					message: e instanceof Error ? e.message : String(e)
				}
			};
		}
		if (t === "failed" || t === "dismissed") {
			let r = xs(t, e);
			return {
				status: t,
				progress: n,
				...r ? { error: r } : {}
			};
		}
		return {
			status: t,
			progress: n
		};
	}
	jobRequest(e) {
		return {
			jobId: this.id,
			...this.basePath === void 0 ? {} : { basePath: this.basePath },
			...e === void 0 ? {} : { routePath: e }
		};
	}
	assertDismissDeclared() {
		if (this.capabilityPolicy === "strict" && this.statusPath === void 0) throw ss("processes.jobStatusLink", this.type, {
			construct: "job status route",
			linkRelation: as.status
		});
		if (!ls(this.jobControlOptions, os.dismiss) && !(!this.conformance?.conformsTo && this.capabilityPolicy !== "strict") && !cs(this.conformance, is.dismiss)) throw ss("processes.dismiss", this.type, {
			missingClass: rs.dismiss,
			construct: os.dismiss
		});
	}
}, ys = class {
	id = "";
	type;
	status = "successful";
	progress = { percent: 100 };
	snapshot;
	constructor(e, t) {
		this.type = e, this.snapshot = {
			status: "successful",
			progress: this.progress,
			result: { outputs: t }
		};
	}
	async poll() {
		return this.snapshot;
	}
	watch(e) {
		return e(this.snapshot), () => {};
	}
	async results() {
		return this.snapshot.result;
	}
	async cancel() {
		return "successful";
	}
};
function bs(e) {
	let t = e?.body;
	if (!t || typeof t != "object") return !1;
	let n = t.title, r = t.detail;
	return !!(typeof n == "string" && /cannot dismiss completed job/i.test(n) || typeof r == "string" && /terminal state/i.test(r));
}
function xs(e, t) {
	if (t.exception) return { ...t.exception };
	if (typeof t.message == "string" && t.message.length > 0) return {
		code: e === "dismissed" ? "JobDismissed" : "JobFailed",
		message: t.message
	};
}
function Ss(e) {
	if (!e) return;
	let t = {};
	if (typeof e.progress == "number" && Number.isFinite(e.progress) && (t.percent = Cs(e.progress)), e.message !== void 0 && (t.message = e.message), e.updated !== void 0 && (t.updatedAt = e.updated), !(t.percent === void 0 && t.message === void 0 && t.updatedAt === void 0)) return t;
}
function Cs(e) {
	return e < 0 ? 0 : e > 100 ? 100 : e;
}
async function ws(e, t = {}) {
	let n = C(t), r = U(t);
	return e.requestCachedMetadataJson(`ogc-processes:landing:${us(t)}${n.toString()}`, `${r}?${n.toString()}`, t);
}
async function Ts(e, t = {}) {
	let n = C(t), r = U(t);
	return e.requestCachedMetadataJson(`ogc-processes:conformance:${us(t)}${n.toString()}`, `${r}/conformance?${n.toString()}`, t);
}
async function Es(e, t = {}) {
	let n = C(t), r = U(t);
	return e.requestCachedMetadataJson(`ogc-processes:processes:${us(t)}${n.toString()}`, `${r}/processes?${n.toString()}`, t);
}
async function Ds(e, t) {
	let n = C(t), r = U(t);
	return e.requestCachedMetadataJson(`ogc-processes:process:${us(t)}${encodeURIComponent(t.processId)}:${n.toString()}`, `${r}/processes/${encodeURIComponent(t.processId)}?${n.toString()}`, t);
}
async function Os(e, t) {
	let n = Fs(t), r = `${U(t)}/processes/${encodeURIComponent(t.processId)}/execution`, i = JSON.stringify({
		inputs: t.inputs ?? {},
		outputs: t.outputs,
		response: "document"
	}), a = await e.pipelineFetch("POST", r, {
		headers: n,
		body: i
	}, t.signal), o = await ks(a), s = As(o, t.processId), c = fs(e.baseUrl, r), l = ps(a.headers.get("Location") ?? void 0, c, e.baseUrl);
	if (s) {
		let n = l ?? gs(s.links, c, e.baseUrl, as.status, "status");
		return {
			jobID: s.jobID,
			status: s.status,
			processID: s.processID ?? t.processId,
			...s.links ? { links: [...s.links] } : {},
			statusInfo: s,
			...n === void 0 ? {} : { statusPath: n },
			synchronous: !1
		};
	}
	return l === void 0 ? {
		jobID: "",
		status: "successful",
		processID: t.processId,
		results: o ?? {},
		synchronous: !0
	} : {
		jobID: js(l),
		status: "accepted",
		processID: t.processId,
		statusPath: l,
		synchronous: !1
	};
}
async function ks(e) {
	if (e.status === 204) return;
	let t = await e.text();
	if (t.trim().length !== 0) try {
		return JSON.parse(t);
	} catch {
		throw ss("processes.documentResponse", void 0, {
			construct: "document-mode execution response",
			contentType: e.headers.get("Content-Type") ?? "unknown"
		});
	}
}
function As(e, t) {
	if (!e || typeof e != "object" || Array.isArray(e)) return;
	let n = e;
	if (!(typeof n.jobID != "string" || n.jobID.length === 0)) return {
		...n,
		jobID: n.jobID,
		processID: n.processID ?? t,
		status: n.status ?? "accepted"
	};
}
function js(e) {
	let t = e.split("?", 1)[0] ?? e, n = t.endsWith("/") ? t.slice(0, -1) : t, r = n.slice(n.lastIndexOf("/") + 1);
	try {
		return decodeURIComponent(r);
	} catch {
		return r;
	}
}
async function Ms(e, t) {
	let n = C(t), r = t.routePath ?? `${U(t)}/jobs/${encodeURIComponent(t.jobId)}`;
	return e.requestJson("GET", hs(r, n), void 0, t.signal);
}
async function Ns(e, t) {
	let n = C(t), r = t.routePath ?? `${U(t)}/jobs/${encodeURIComponent(t.jobId)}/results`;
	return e.requestJson("GET", hs(r, n), void 0, t.signal);
}
async function Ps(e, t) {
	let n = C(t), r = t.routePath ?? `${U(t)}/jobs/${encodeURIComponent(t.jobId)}`;
	return e.requestJson("DELETE", hs(r, n), void 0, t.signal);
}
function Fs(e) {
	let t = new Headers(S({
		"Content-Type": "application/json",
		Accept: "application/json"
	}, e.headers));
	return t.delete("Prefer"), e.mode === "async" && t.set("Prefer", "respond-async"), t;
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/optional-grpc-peer.js
var Is = [
	"@connectrpc/connect-web",
	"@connectrpc/connect",
	"@bufbuild/protobuf"
], Ls = /ERR_MODULE_NOT_FOUND|ERR_PACKAGE_PATH_NOT_EXPORTED|Failed to resolve module specifier|Failed to fetch dynamically imported module|Cannot find package|Cannot find module/, Rs = class extends Error {
	peer;
	constructor(e, t) {
		let n = [
			`HonuaClient transport "grpc-web" could not load the optional peer "${e}".`,
			"Install @bufbuild/protobuf, @connectrpc/connect, and @connectrpc/connect-web.",
			"REST, including createHonua, does not load them."
		].join(" ");
		super(n, t instanceof Error ? { cause: t } : void 0), this.name = "HonuaOptionalGrpcPeerError", this.peer = e;
	}
};
function zs(e) {
	return typeof e == "object" && !!e;
}
function Bs(e) {
	return zs(e) ? e instanceof Error && e.name === "HonuaGrpcError" || typeof e.code == "number" : !1;
}
function Vs(e) {
	if (!(e instanceof Error)) return;
	let t = `${"code" in e && (typeof e.code == "string" || typeof e.code == "number") ? String(e.code) : ""}\n${e.message}`;
	if (Ls.test(t)) return Is.find((e) => t.includes(e));
}
function Hs(e) {
	let t = /* @__PURE__ */ new Set(), n = e;
	for (; zs(n) && !t.has(n);) {
		if (t.add(n), Bs(n)) return;
		let e = Vs(n);
		if (e) return e;
		n = n.cause;
	}
}
function Us(e) {
	let t = Hs(e);
	return t ? new Rs(t, e) : e;
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/pbf-decoder.js
var W = 0, Ws = 1, G = 2, Gs = 5, Ks = class extends Error {
	constructor(e) {
		super(e), this.name = "PbfDecodeError";
	}
}, qs = class e {
	view;
	bytes;
	pos;
	end;
	constructor(e, t = 0, n) {
		this.bytes = e, this.view = new DataView(e.buffer, e.byteOffset, e.byteLength), this.pos = t, this.end = n === void 0 ? e.length : t + n;
	}
	readVarint() {
		let e = 0, t = 0;
		for (; this.pos < this.end;) {
			let n = this.bytes[this.pos++];
			if (e |= (n & 127) << t, !(n & 128)) return e >>> 0;
			if (t += 7, t > 35) throw Error("Varint too long");
		}
		throw Error("Unexpected end of buffer reading varint");
	}
	readVarint64() {
		let e = 0, t = 0, n = 0;
		for (; n < 28 && this.pos < this.end;) {
			let t = this.bytes[this.pos++];
			if (e |= (t & 127) << n, n += 7, !(t & 128)) return e >>> 0;
		}
		for (; this.pos < this.end;) {
			let e = this.bytes[this.pos++];
			if (t |= (e & 127) << n - 28, n += 7, !(e & 128)) break;
			if (n > 63) throw Error("Varint too long");
		}
		return (t >>> 0) * 268435456 + (e >>> 0);
	}
	readVarint64Bigint() {
		let e = 0n, t = 0n;
		for (; this.pos < this.end;) {
			let n = this.bytes[this.pos++];
			if (e |= BigInt(n & 127) << t, !(n & 128)) return e;
			if (t += 7n, t > 63n) throw Error("Varint too long");
		}
		throw Error("Unexpected end of buffer reading varint");
	}
	readSVarint64() {
		let e = this.readVarint64(), t = Math.floor(e / 2);
		return e % 2 == 0 ? t : -(t + 1);
	}
	readSVarint32() {
		let e = this.readVarint();
		return e >>> 1 ^ -(e & 1);
	}
	readDouble() {
		let e = this.view.getFloat64(this.pos, !0);
		return this.pos += 8, e;
	}
	readFloat() {
		let e = this.view.getFloat32(this.pos, !0);
		return this.pos += 4, e;
	}
	readString(e) {
		let t = this.bytes.subarray(this.pos, this.pos + e);
		return this.pos += e, Js.decode(t);
	}
	readBool() {
		return this.readVarint() !== 0;
	}
	readTag() {
		let e = this.readVarint();
		return [e >>> 3, e & 7];
	}
	skip(e) {
		switch (e) {
			case W:
				this.readVarint64();
				break;
			case Ws:
				this.pos += 8;
				break;
			case G: {
				let e = this.readVarint();
				this.pos += e;
				break;
			}
			case Gs:
				this.pos += 4;
				break;
			default: throw Error(`Unknown wire type: ${e}`);
		}
	}
	subReader(t) {
		let n = new e(this.bytes, this.pos, t);
		return this.pos += t, n;
	}
	readPackedSInt64(e) {
		let t = [], n = this.pos + e;
		for (; this.pos < n;) t.push(this.readSVarint64());
		return t;
	}
	readPackedUInt32(e) {
		let t = [], n = this.pos + e;
		for (; this.pos < n;) t.push(this.readVarint());
		return t;
	}
}, Js = new TextDecoder(), Ys = {
	0: "esriGeometryPoint",
	1: "esriGeometryMultipoint",
	2: "esriGeometryPolyline",
	3: "esriGeometryPolygon",
	4: "esriGeometryEnvelope",
	127: "esriGeometryNull"
};
function Xs(e) {
	let t = 0, n = 0;
	for (; e.pos < e.end;) {
		let [r, i] = e.readTag();
		r === 1 && i === Ws ? t = e.readDouble() : r === 2 && i === Ws ? n = e.readDouble() : e.skip(i);
	}
	return {
		xScale: t,
		yScale: n
	};
}
function Zs(e) {
	let t = 0, n = 0;
	for (; e.pos < e.end;) {
		let [r, i] = e.readTag();
		r === 1 && i === Ws ? t = e.readDouble() : r === 2 && i === Ws ? n = e.readDouble() : e.skip(i);
	}
	return {
		xTranslate: t,
		yTranslate: n
	};
}
function Qs(e) {
	let t = 1, n = 1, r = 0, i = 0;
	for (; e.pos < e.end;) {
		let [a, o] = e.readTag();
		if (a === 2 && o === G) {
			let r = e.readVarint(), i = Xs(e.subReader(r));
			t = i.xScale, n = i.yScale;
		} else if (a === 3 && o === G) {
			let t = e.readVarint(), n = Zs(e.subReader(t));
			r = n.xTranslate, i = n.yTranslate;
		} else e.skip(o);
	}
	return {
		xScale: t,
		yScale: n,
		xTranslate: r,
		yTranslate: i
	};
}
function $s(e) {
	let t = "", n = 0, r = "";
	for (; e.pos < e.end;) {
		let [i, a] = e.readTag();
		if (i === 1 && a === G) {
			let n = e.readVarint();
			t = e.readString(n);
		} else if (i === 2 && a === W) n = e.readVarint();
		else if (i === 3 && a === G) {
			let t = e.readVarint();
			r = e.readString(t);
		} else e.skip(a);
	}
	return {
		name: t,
		fieldType: n,
		alias: r || t
	};
}
function ec(e) {
	let t = 0, n = 0;
	for (; e.pos < e.end;) {
		let [r, i] = e.readTag();
		r === 1 && i === W ? t = e.readVarint() : r === 2 && i === W ? n = e.readVarint() : e.skip(i);
	}
	return {
		wkid: t,
		latestWkid: n || t
	};
}
var tc = BigInt(2 ** 53 - 1), nc = BigInt(-(2 ** 53 - 1)), rc = 1n << 64n;
function ic(e) {
	return e >= 1n << 63n ? e - rc : e;
}
function ac(e) {
	return e <= tc && e >= nc ? Number(e) : e.toString();
}
function oc(e) {
	let t = null, n = -1;
	for (; e.pos < e.end;) {
		let [r, i] = e.readTag();
		switch (r) {
			case 1:
				if (i === G) {
					let n = e.readVarint();
					t = e.readString(n);
				} else e.skip(i);
				break;
			case 2:
				i === Gs ? t = e.readFloat() : e.skip(i);
				break;
			case 3:
				i === Ws ? t = e.readDouble() : e.skip(i);
				break;
			case 4:
				i === W ? t = e.readSVarint32() : e.skip(i);
				break;
			case 5:
				i === W ? t = e.readVarint() : e.skip(i);
				break;
			case 6:
				i === W ? t = ac(ic(e.readVarint64Bigint())) : e.skip(i);
				break;
			case 7:
				i === W ? t = ac(e.readVarint64Bigint()) : e.skip(i);
				break;
			case 8:
				if (i === W) {
					let n = e.readVarint64Bigint();
					t = ac(n >> 1n ^ -(n & 1n));
				} else e.skip(i);
				break;
			case 9:
				i === W ? t = e.readBool() : e.skip(i);
				break;
			case 10:
				i === W ? (e.readVarint(), t = null) : e.skip(i);
				break;
			case 11:
				i === W ? n = e.readVarint() : e.skip(i);
				break;
			default: e.skip(i);
		}
	}
	return {
		value: t,
		fieldIndex: n
	};
}
function sc(e, t, n) {
	let r = [], i = [];
	for (; e.pos < e.end;) {
		let [t, n] = e.readTag();
		if (t === 1 && n === W) e.readVarint();
		else if (t === 2 && n === G) {
			let t = e.readVarint();
			r = e.readPackedUInt32(t);
		} else if (t === 3 && n === G) {
			let t = e.readVarint();
			i = e.readPackedSInt64(t);
		} else e.skip(n);
	}
	if (i.length === 0) return null;
	if (i.length % 2 != 0) throw new Ks(`PBF geometry coordinate stream has an odd length (${i.length})`);
	let a = t?.xScale ?? 1, o = t?.yScale ?? 1, s = t?.xTranslate ?? 0, c = t?.yTranslate ?? 0, l = 0, u = 0, d = [];
	for (let e = 0; e < i.length; e += 2) {
		l += i[e], u += i[e + 1];
		let t = l * a + s, n = u * o + c;
		if (!Number.isFinite(t) || !Number.isFinite(n)) throw new Ks("PBF geometry produced a non-finite coordinate");
		d.push([t, n]);
	}
	return cc(n, d, r);
}
function cc(e, t, n) {
	switch (e) {
		case "esriGeometryPoint": return t.length > 0 ? {
			x: t[0][0],
			y: t[0][1]
		} : {
			x: null,
			y: null
		};
		case "esriGeometryMultipoint": return { points: t };
		case "esriGeometryPolyline": {
			let e = [], r = 0;
			for (let i of n) e.push(t.slice(r, r + i)), r += i;
			return { paths: e.length > 0 ? e : [t] };
		}
		case "esriGeometryPolygon": {
			let e = [], r = 0;
			for (let i of n) e.push(t.slice(r, r + i)), r += i;
			return { rings: e.length > 0 ? e : [t] };
		}
		default: return {
			x: t[0]?.[0] ?? null,
			y: t[0]?.[1] ?? null
		};
	}
}
function lc(e, t, n, r) {
	let i = {}, a = null, o = [];
	for (; e.pos < e.end;) {
		let [t, i] = e.readTag();
		if (t === 1 && i === G) {
			let t = e.readVarint(), n = e.subReader(t);
			o.push(oc(n));
		} else if (t === 2 && i === G) {
			let t = e.readVarint();
			a = sc(e.subReader(t), n, r);
		} else e.skip(i);
	}
	for (let [e, n] of o.entries()) {
		let r = n.fieldIndex >= 0 ? n.fieldIndex : e;
		r < t.length && (i[t[r].name] = n.value);
	}
	let s = { attributes: i };
	return a !== null && (s.geometry = a), s;
}
function uc(e) {
	let t = "", n = "", r = null, i = !1, a = !1, o = !1, s = null, c = [], l = [];
	for (; e.pos < e.end;) {
		let [u, d] = e.readTag();
		switch (u) {
			case 1:
				if (d === G) {
					let n = e.readVarint();
					t = e.readString(n);
				} else e.skip(d);
				break;
			case 7:
				d === W ? n = Ys[e.readVarint()] ?? "esriGeometryNull" : e.skip(d);
				break;
			case 8:
				if (d === G) {
					let t = e.readVarint();
					r = ec(e.subReader(t));
				} else e.skip(d);
				break;
			case 9:
				d === W ? i = e.readBool() : e.skip(d);
				break;
			case 10:
				d === W ? a = e.readBool() : e.skip(d);
				break;
			case 11:
				d === W ? o = e.readBool() : e.skip(d);
				break;
			case 12:
				if (d === G) {
					let t = e.readVarint();
					s = Qs(e.subReader(t));
				} else e.skip(d);
				break;
			case 13:
				if (d === G) {
					let t = e.readVarint();
					c.push($s(e.subReader(t)));
				} else e.skip(d);
				break;
			case 15:
				if (d === G) {
					let t = e.readVarint();
					l.push(e.subReader(t));
				} else e.skip(d);
				break;
			default: e.skip(d);
		}
	}
	if (a || o) throw new Ks(`PBF response carries ${a ? "Z" : ""}${a && o ? "/" : ""}${o ? "M" : ""} geometry; falling back to f=json`);
	let u = l.map((e) => lc(e, c, s, n)), d = {
		objectIdFieldName: t,
		fields: c.map((e) => ({
			name: e.name,
			type: dc(e.fieldType),
			alias: e.alias
		})),
		features: u
	};
	return n && n !== "esriGeometryNull" && (d.geometryType = n), r && (d.spatialReference = r), a && (d.hasZ = !0), o && (d.hasM = !0), i && (d.exceededTransferLimit = !0), d;
}
function dc(e) {
	switch (e) {
		case 0: return "esriFieldTypeSmallInteger";
		case 1: return "esriFieldTypeInteger";
		case 2: return "esriFieldTypeSingle";
		case 3: return "esriFieldTypeDouble";
		case 4: return "esriFieldTypeString";
		case 5: return "esriFieldTypeDate";
		case 6: return "esriFieldTypeOID";
		case 7: return "esriFieldTypeGeometry";
		case 8: return "esriFieldTypeBlob";
		case 10: return "esriFieldTypeGUID";
		case 11: return "esriFieldTypeGlobalID";
		case 12: return "esriFieldTypeXML";
		case 13: return "esriFieldTypeBigInteger";
		case 14: return "esriFieldTypeDateOnly";
		default: return "esriFieldTypeString";
	}
}
function fc(e) {
	let t = new qs(e instanceof Uint8Array ? e : new Uint8Array(e)), n = {};
	for (; t.pos < t.end;) {
		let [e, r] = t.readTag();
		if (e === 1 && r === G) {
			let e = t.readVarint();
			t.pos += e;
		} else if (e === 2 && r === G) {
			let e = t.readVarint(), r = t.subReader(e);
			for (; r.pos < r.end;) {
				let [e, t] = r.readTag();
				if (e === 1 && t === G) {
					let e = r.readVarint();
					n = uc(r.subReader(e));
				} else r.skip(t);
			}
		} else t.skip(r);
	}
	return n;
}
function pc(e) {
	let t = e.headers.get("content-type") ?? "";
	return t.includes("application/x-protobuf") || t.includes("application/protobuf");
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/process-runner.js
var mc = class {
	adapter;
	constructor(e) {
		this.adapter = e;
	}
	get protocol() {
		return this.adapter.protocol;
	}
	execute(e) {
		return this.adapter.execute(e);
	}
	job(e, t = {}) {
		return this.adapter.job(e, t);
	}
	async validate(e) {
		if (!this.adapter.validate) throw Error(`${this.protocol} does not expose validate through the unified process runner.`);
		return this.adapter.validate(e);
	}
	async dryRun(e) {
		if (!this.adapter.dryRun) throw Error(`${this.protocol} does not expose dryRun through the unified process runner.`);
		return this.adapter.dryRun(e);
	}
};
function hc(e) {
	return new mc(e);
}
function gc(e) {
	return {
		protocol: "ogc-processes",
		execute(t) {
			if (!t.processId) throw Error("OGC Processes execution requires processId.");
			return e.execute({
				processId: t.processId,
				inputs: t.inputs,
				outputs: t.outputs,
				mode: t.mode ?? "async",
				signal: t.signal,
				...t.jobControlOptions === void 0 ? {} : { jobControlOptions: t.jobControlOptions }
			});
		},
		job(t, n = {}) {
			return e.job(t, { processId: n.processId });
		}
	};
}
function _c(e) {
	return {
		protocol: "geoservices-gp",
		execute(t) {
			return e.submit({
				parameters: t.parameters ?? t.inputs ?? {},
				signal: t.signal
			}, { resultNames: t.resultNames });
		},
		job(t, n = {}) {
			return e.job(t, { resultNames: n.resultNames });
		}
	};
}
function vc(e) {
	return {
		protocol: "geospatial-grpc",
		validate(t) {
			if (!e.validatePlan) throw Error("geospatial-grpc ProcessService client does not expose validatePlan.");
			return e.validatePlan({ plan: bc(t) });
		},
		dryRun(t) {
			if (!e.dryRunPlan) throw Error("geospatial-grpc ProcessService client does not expose dryRunPlan.");
			return e.dryRunPlan({ plan: bc(t) });
		},
		async execute(t) {
			return new yc({
				client: e,
				accepted: await e.submitJob({
					plan: bc(t),
					context: t.context
				})
			});
		},
		job(t) {
			return new yc({
				client: e,
				accepted: {
					jobId: t,
					state: "JOB_STATE_UNSPECIFIED"
				}
			});
		}
	};
}
var yc = class {
	id;
	type = "geospatial-grpc";
	client;
	lifecycle;
	constructor(e) {
		this.client = e.client, this.id = xc(e.accepted), this.lifecycle = new Ln({
			id: this.id,
			initialStatus: Sc(e.accepted.state),
			pollIntervalMs: 1e3,
			poll: async (e) => this.translateJob(await this.client.getJob({
				jobId: this.id,
				signal: e
			}))
		});
	}
	get status() {
		return this.lifecycle.status;
	}
	get progress() {
		return this.lifecycle.progress;
	}
	async poll() {
		return this.lifecycle.poll();
	}
	watch(e) {
		return this.lifecycle.watch(e);
	}
	async results(e = {}) {
		return this.lifecycle.results(e);
	}
	async cancel() {
		return this.lifecycle.cancel(async () => this.translateJob(await this.client.cancelJob({ jobId: this.id })));
	}
	async translateJob(e) {
		let t = "progress" in e ? e.progress : void 0, n = Sc(e.state ?? t?.state), r = Cc(t);
		if (n === "successful") return wc(await this.client.getJobResult({ jobId: this.id }), r);
		if (n === "failed" || n === "dismissed") {
			let e = await this.safeGetTerminalResult(), t = e ? Tc(e) : void 0;
			return {
				status: n,
				progress: r,
				...t ? { error: t } : {}
			};
		}
		return {
			status: n,
			progress: r
		};
	}
	async safeGetTerminalResult() {
		try {
			return await this.client.getJobResult({ jobId: this.id });
		} catch {
			return;
		}
	}
};
function bc(e) {
	if (e.plan === void 0) throw Error("geospatial-grpc process execution requires plan.");
	return e.plan;
}
function xc(e) {
	let t = e.jobId ?? e.job_id;
	if (!t) throw Error("ProcessService response did not include jobId.");
	return t;
}
function Sc(e) {
	if (e === 6) return "successful";
	if (e === 7) return "failed";
	if (e === 8) return "dismissed";
	if (e === 5) return "running";
	if (e === 1 || e === 2 || e === 3 || e === 4) return "accepted";
	let t = String(e ?? "").toLowerCase();
	return t.includes("completed") || t === "successful" ? "successful" : t.includes("failed") ? "failed" : t.includes("cancelled") || t.includes("canceled") || t.includes("dismissed") ? "dismissed" : t.includes("draft") || t.includes("clarification") || t.includes("validated") || t.includes("approval") || t.includes("accepted") ? "accepted" : t.includes("running") ? "running" : "accepted";
}
function Cc(e) {
	if (!e) return;
	let t = e.progressPercent ?? e.progress_percent, n = e.updatedAt ?? e.updated_at, r = {};
	return typeof t == "number" && Number.isFinite(t) && (r.percent = Math.max(0, Math.min(100, t))), e.message && (r.message = e.message), n !== void 0 && (r.updatedAt = Ec(n)), r.percent === void 0 && r.message === void 0 && r.updatedAt === void 0 ? void 0 : r;
}
function wc(e, t) {
	let n = Tc(e);
	return n ? {
		status: "failed",
		progress: t,
		error: n
	} : {
		status: "successful",
		progress: t,
		result: { outputs: { result: e.result ?? e.outcome?.value ?? e } }
	};
}
function Tc(e) {
	let t = e.error ?? (e.outcome?.case === "error" ? e.outcome.value : void 0);
	if (t) return {
		code: t.code === void 0 ? t.errorCode ?? t.error_code ?? "GeospatialGrpcProcessError" : String(t.code),
		message: t.message ?? "geospatial-grpc process execution failed.",
		details: t.details
	};
}
function Ec(e) {
	let t = typeof e == "bigint" || typeof e == "string" ? Number(e) : e;
	return Number.isFinite(t) ? new Date(t).toISOString() : String(e);
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/request-pipeline.js
var Dc = /* @__PURE__ */ new Set([
	429,
	502,
	503,
	504
]), Oc = /* @__PURE__ */ new Set([
	"GET",
	"HEAD",
	"PUT",
	"DELETE"
]);
function kc(e) {
	if (!(typeof e != "number" || !Number.isFinite(e))) return Math.max(1, Math.trunc(e));
}
function Ac(e) {
	if (!e) return;
	let t = typeof e.maxRetries == "number" && Number.isFinite(e.maxRetries) ? Math.max(0, Math.trunc(e.maxRetries)) : 0;
	if (t < 1) return;
	let n = typeof e.baseDelayMs == "number" && Number.isFinite(e.baseDelayMs) ? Math.max(1, Math.trunc(e.baseDelayMs)) : 100, r = typeof e.maxDelayMs == "number" && Number.isFinite(e.maxDelayMs) ? Math.max(n, Math.trunc(e.maxDelayMs)) : 2e3, i = new Set((e.retryStatuses ?? Array.from(Dc)).map((e) => Math.trunc(e)).filter((e) => Number.isFinite(e) && e >= 100 && e <= 599));
	if (i.size === 0) for (let e of Dc) i.add(e);
	return {
		maxRetries: t,
		baseDelayMs: n,
		maxDelayMs: r,
		retryStatuses: i
	};
}
function jc(e) {
	let t = e.get("retry-after");
	if (!t) return;
	let n = Number.parseInt(t, 10);
	if (Number.isFinite(n) && n >= 0) return n * 1e3;
	let r = Date.parse(t);
	if (Number.isFinite(r)) return Math.max(0, r - Date.now());
}
function Mc(e) {
	return jc(e.headers);
}
function Nc(e, t, r, i, a, o = !1) {
	return !e || r >= e.maxRetries || !Oc.has(t) && !o || a instanceof c ? !1 : i === void 0 ? a instanceof s || a instanceof n : e.retryStatuses.has(i);
}
function Pc(e, t, n) {
	let r = n ? Mc(n) : void 0;
	if (r !== void 0) return Math.min(e?.maxDelayMs ?? r, r);
	if (!e) return 0;
	let i = e.baseDelayMs * 2 ** t;
	return Math.min(e.maxDelayMs, i) * (.5 + Math.random() * .5);
}
var Fc = /* @__PURE__ */ new Set([
	4,
	8,
	10,
	14
]), Ic = 1;
function Lc(e) {
	if (typeof e == "object" && e && "code" in e && typeof e.code == "number") return e.code;
}
function Rc(e, t, n, r) {
	if (!e || t >= e.maxRetries || r) return !1;
	let i = Lc(n);
	return i === void 0 || i === Ic ? !1 : Fc.has(i);
}
function zc(e) {
	if (typeof e == "object" && e && "metadata" in e) {
		let t = e.metadata;
		if (t && typeof t.get == "function") return t;
	}
}
function Bc(e, t, n) {
	let r = zc(n), i = r ? jc(r) : void 0;
	return i === void 0 ? Pc(e, t) : Math.min(e?.maxDelayMs ?? i, i);
}
async function Vc(e, t) {
	if (t?.aborted) throw new c();
	e <= 0 || await new Promise((n, r) => {
		let i = setTimeout(() => {
			t?.removeEventListener("abort", a), n();
		}, e), a = () => {
			clearTimeout(i), r(new c());
		};
		t?.addEventListener("abort", a, { once: !0 });
	});
}
function Hc(e) {
	return e instanceof Error && e.name === "AbortError" ? new c() : e instanceof Error ? new s(e.message, e) : new s(String(e), e);
}
function Uc(e, t) {
	if (t === void 0) return {
		signal: e ?? void 0,
		didTimeout: !1,
		dispose: () => void 0
	};
	let n = new AbortController(), r = !1, i, a;
	return i = setTimeout(() => {
		r = !0, n.abort();
	}, t), e && (e.aborted ? n.abort() : (a = () => {
		n.abort();
	}, e.addEventListener("abort", a, { once: !0 }))), {
		signal: n.signal,
		get didTimeout() {
			return r;
		},
		dispose: () => {
			i &&= (clearTimeout(i), void 0), e && a && (e.removeEventListener("abort", a), a = void 0);
		}
	};
}
function Wc(e, t, n) {
	let r = n ? { responseHeaders: n } : void 0;
	if (Kc(t)) {
		let n = t.error;
		if (Kc(n) && typeof n.message == "string") return new a(e, n.message, t, r);
		if (typeof t.message == "string") return new a(e, t.message, t, r);
		if (typeof t.detail == "string") return new a(e, t.detail, t, r);
	}
	return new a(e, "Request failed", t, r);
}
function Gc(e, t, n) {
	if (!Kc(t) || !Kc(t.error)) return;
	let r = t.error, i = typeof r.code == "number" ? r.code : void 0, o = typeof r.message == "string" ? r.message : "Request failed";
	if (!(i === void 0 && typeof r.message != "string")) return new a(i ?? e, o, t, {
		protocolCode: i,
		transportStatus: e,
		responseHeaders: n
	});
}
function Kc(e) {
	return typeof e == "object" && !!e;
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/wfs-capabilities.js
var K = Object.freeze({
	maxBytes: 2 * 1024 * 1024,
	maxElements: 12e3,
	maxDepth: 32,
	maxAttributesPerElement: 64,
	maxAttributeBytesPerElement: 64 * 1024,
	maxTextBytes: 512 * 1024
});
function qc(e) {
	let t = ul(e);
	if (t.local !== "WFS_Capabilities") {
		if (t.local === "ExceptionReport") {
			let e = Qc(t);
			throw new i(e.exceptionCode, e.message, e.locator);
		}
		throw Error(`WFS GetCapabilities: expected <WFS_Capabilities>, got <${t.qname}>`);
	}
	let n = t.attributes.version, r = q(t, "OperationsMetadata") ? J(vl(t, "OperationsMetadata"), "Operation") : [], a = /* @__PURE__ */ new Map(), o = /* @__PURE__ */ new Map();
	for (let e of r) {
		let t = e.attributes.name;
		if (!t) continue;
		let n = [], r, i;
		for (let t of J(e, "DCP")) {
			let e = q(t, "HTTP");
			if (e) for (let t of e.children) t.local === "Get" && (n.push("GET"), r === void 0 && t.attributes.href && (r = t.attributes.href)), t.local === "Post" && (n.push("POST"), i === void 0 && t.attributes.href && (i = t.attributes.href));
		}
		let s = [];
		for (let t of J(e, "Parameter")) if (t.attributes.name?.toLowerCase() === "outputformat") {
			for (let e of J(t, "AllowedValues")) for (let t of J(e, "Value")) {
				let e = t.text.trim();
				e && s.push(e);
			}
			for (let e of J(t, "Value")) {
				let t = e.text.trim();
				t && s.push(t);
			}
		}
		let c = Object.freeze([...new Set(n)]), l = Object.freeze([...s]);
		a.set(t, Object.freeze({
			name: t,
			methods: c,
			outputFormats: l,
			...r === void 0 ? {} : { getUrl: r },
			...i === void 0 ? {} : { postUrl: i }
		})), o.set(t, l);
	}
	let s = q(t, "FeatureTypeList"), c = [];
	if (s) for (let e of J(s, "FeatureType")) {
		let t = q(e, "Name"), n = q(e, "Title"), r = q(e, "DefaultCRS") ?? q(e, "DefaultSRS"), i = [...J(e, "OtherCRS").map((e) => e.text.trim()), ...J(e, "OtherSRS").map((e) => e.text.trim())].filter((e) => e.length > 0), a = q(e, "WGS84BoundingBox"), o = a ? q(a, "LowerCorner") : void 0, s = a ? q(a, "UpperCorner") : void 0, l = t ? Jc(t, t.text.trim()) : void 0, u = {
			name: t?.text.trim() ?? "",
			otherCrs: Object.freeze([...i]),
			...l ? { namespace: l } : {}
		};
		if (n?.text && (u.title = n.text.trim()), r?.text && (u.defaultCrs = r.text.trim()), o && s) {
			let [e, t] = o.text.trim().split(/\s+/).map(Number), [n, r] = s.text.trim().split(/\s+/).map(Number);
			Number.isFinite(e) && Number.isFinite(t) && Number.isFinite(n) && Number.isFinite(r) && (u.wgs84BoundingBox = Object.freeze({
				xmin: e,
				ymin: t,
				xmax: n,
				ymax: r
			}));
		}
		u.name && c.push(Object.freeze(u));
	}
	let l = $c(q(t, "Filter_Capabilities")), u = el(t), d = /* @__PURE__ */ new Map();
	for (let [e, n] of Object.entries(t.attributes)) e.startsWith("xmlns:") && n && d.set(e.slice(6), n);
	return Object.freeze({
		...n ? { version: n } : {},
		operations: Yc(a),
		outputFormatsByOp: Yc(o),
		featureTypes: Object.freeze([...c]),
		namespaces: Yc(d),
		filterCapabilities: Xc(l),
		storedQueryNames: Object.freeze([...u])
	});
}
function Jc(e, t) {
	let n = t.indexOf(":");
	if (n <= 0 || n === t.length - 1 || t.indexOf(":", n + 1) !== -1) return;
	let r = t.slice(0, n);
	return Object.freeze({
		prefix: r,
		uri: e.namespaces[r] ?? ""
	});
}
function Yc(e) {
	let t = new Map(e), n = {
		get size() {
			return t.size;
		},
		get(e) {
			return t.get(e);
		},
		has(e) {
			return t.has(e);
		},
		forEach(e, r) {
			for (let [i, a] of t) e.call(r, a, i, n);
		},
		entries() {
			return t.entries();
		},
		keys() {
			return t.keys();
		},
		values() {
			return t.values();
		},
		[Symbol.iterator]() {
			return t[Symbol.iterator]();
		}
	};
	return Object.freeze(n);
}
function Xc(e) {
	return Object.freeze({
		spatial: Object.freeze([...e.spatial]),
		scalar: Object.freeze([...e.scalar]),
		temporal: Object.freeze([...e.temporal])
	});
}
function Zc(e) {
	let t = ul(e);
	if (t.local !== "ExceptionReport") throw Error(`WFS ExceptionReport: expected <ExceptionReport>, got <${t.qname}>`);
	return Qc(t);
}
function Qc(e) {
	let t = q(e, "Exception");
	if (!t) return {
		exceptionCode: "UnknownException",
		message: "ExceptionReport without <Exception>"
	};
	let n = t.attributes.exceptionCode ?? "UnknownException", r = t.attributes.locator, i = q(t, "ExceptionText"), a = {
		exceptionCode: n,
		message: (i ? i.text.trim() : "") || n
	};
	return r && (a.locator = r), a;
}
function $c(e) {
	if (!e) return {
		spatial: [],
		scalar: [],
		temporal: []
	};
	let t = [], n = [], r = [], i = q(q(e, "Spatial_Capabilities"), "SpatialOperators");
	if (i) for (let e of J(i, "SpatialOperator")) e.attributes.name && t.push(e.attributes.name);
	let a = q(q(e, "Scalar_Capabilities"), "ComparisonOperators");
	if (a) for (let e of J(a, "ComparisonOperator")) {
		let t = e.text.trim();
		t && n.push(t);
	}
	let o = q(q(e, "Temporal_Capabilities"), "TemporalOperators");
	if (o) for (let e of J(o, "TemporalOperator")) e.attributes.name && r.push(e.attributes.name);
	return {
		spatial: t,
		scalar: n,
		temporal: r
	};
}
function el(e) {
	let t = q(e, "StoredQueries");
	if (!t) return [];
	let n = [];
	for (let e of J(t, "StoredQuery")) e.attributes.id && n.push(e.attributes.id);
	return n;
}
function tl(e) {
	let t = ul(e);
	if (t.local !== "ListStoredQueriesResponse") {
		if (t.local === "ExceptionReport") {
			let e = Qc(t);
			throw new i(e.exceptionCode, e.message, e.locator);
		}
		throw Error(`WFS ListStoredQueries: expected <ListStoredQueriesResponse>, got <${t.qname}>`);
	}
	let n = [];
	for (let e of J(t, "StoredQuery")) e.attributes.id && n.push(e.attributes.id);
	return n;
}
var nl = /* @__PURE__ */ new Set([
	"GeometryPropertyType",
	"GeometryAssociationType",
	"GeometryArrayPropertyType",
	"MultiGeometryPropertyType",
	"AbstractGeometryType",
	"PointPropertyType",
	"MultiPointPropertyType",
	"LineStringPropertyType",
	"MultiLineStringPropertyType",
	"CurvePropertyType",
	"MultiCurvePropertyType",
	"PolygonPropertyType",
	"MultiPolygonPropertyType",
	"SurfacePropertyType",
	"MultiSurfacePropertyType",
	"SolidPropertyType",
	"MultiSolidPropertyType"
]), rl = "http://www.opengis.net/gml";
function il(e, t) {
	let n = ul(e);
	if (n.local !== "schema") {
		if (n.local === "ExceptionReport") {
			let e = Qc(n);
			throw new i(e.exceptionCode, e.message, e.locator);
		}
		throw Error(`WFS DescribeFeatureType: expected <schema>, got <${n.qname}>`);
	}
	let r = al(n, gl(t));
	if (!r) return Object.freeze([]);
	let a = [];
	return ol(r, a), Object.freeze(a);
}
function al(e, t) {
	let n = J(e, "complexType"), r = J(e, "element").find((e) => e.attributes.name === t);
	if (r) {
		let e = r.attributes.type, t = e ? n.find((t) => t.attributes.name === gl(e)) : q(r, "complexType");
		if (t) return t;
	}
	return n.find((e) => e.attributes.name === `${t}Type`) || (n.length === 1 ? n[0] : void 0);
}
function ol(e, t) {
	for (let n of e.children) {
		if (n.local === "element") {
			let e = n.attributes.name, r = n.attributes.type;
			if (e && r && sl(n, r)) {
				t.push(e);
				continue;
			}
		}
		ol(n, t);
	}
}
function sl(e, t) {
	let n = t.indexOf(":"), r = n > 0 ? t.slice(0, n) : void 0;
	if (!nl.has(gl(t))) return !1;
	let i = r === void 0 ? void 0 : e.namespaces[r];
	return i === void 0 || i.startsWith(rl);
}
function cl(e) {
	let t = ul(e);
	if (t.local !== "TransactionResponse") {
		if (t.local === "ExceptionReport") {
			let e = Qc(t);
			throw new i(e.exceptionCode, e.message, e.locator);
		}
		throw Error(`WFS Transaction: expected <TransactionResponse>, got <${t.qname}>`);
	}
	let n = q(t, "TransactionSummary"), r = n ? ll(q(n, "totalInserted")?.text) : 0, a = n ? ll(q(n, "totalUpdated")?.text) : 0, o = n ? ll(q(n, "totalDeleted")?.text) : 0, s = q(t, "InsertResults"), c = [];
	if (s) for (let e of J(s, "Feature")) {
		let t = e.attributes.handle, n = [];
		for (let t of J(e, "ResourceId")) t.attributes.rid && n.push(t.attributes.rid);
		for (let t of J(e, "FeatureId")) t.attributes.fid && n.push(t.attributes.fid);
		let r = { ids: n };
		t && (r.handle = t), c.push(r);
	}
	return {
		totalInserted: r,
		totalUpdated: a,
		totalDeleted: o,
		insertResults: c
	};
}
function ll(e) {
	if (!e) return 0;
	let t = Number(e.trim());
	return Number.isFinite(t) ? Math.trunc(t) : 0;
}
function ul(e) {
	if (typeof e != "string" || e.length === 0) throw Error("WFS XML parser: empty document");
	if (hl(e) > K.maxBytes) throw Error(`WFS XML parser: document exceeds the ${K.maxBytes}-byte limit`);
	let t = e.toLowerCase();
	if (t.includes("<!doctype") || t.includes("<!entity")) throw Error("WFS XML parser: DOCTYPE / ENTITY declarations are rejected");
	let n = dl(e), r = n.length, i = 0, a = [], o, s = {
		elements: 0,
		textBytes: 0
	};
	for (; i < r;) {
		if (n[i] === "<") {
			if (n.startsWith("<?", i)) {
				let e = n.indexOf("?>", i + 2);
				if (e === -1) throw Error("WFS XML parser: unterminated processing instruction");
				i = e + 2;
				continue;
			}
			if (n.startsWith("<!--", i)) {
				let e = n.indexOf("-->", i + 4);
				if (e === -1) throw Error("WFS XML parser: unterminated comment");
				i = e + 3;
				continue;
			}
			if (n.startsWith("<![CDATA[", i)) {
				let e = n.indexOf("]]>", i + 9);
				if (e === -1) throw Error("WFS XML parser: unterminated CDATA");
				let t = n.slice(i + 9, e), r = a[a.length - 1];
				r && ml(r, t, s), i = e + 3;
				continue;
			}
			if (n[i + 1] === "/") {
				let e = n.indexOf(">", i + 2);
				if (e === -1) throw Error("WFS XML parser: unterminated closing tag");
				let t = n.slice(i + 2, e).trim(), r = a[a.length - 1];
				if (!r) throw Error(`WFS XML parser: stray closing tag </${t}>`);
				if (r.qname !== t) throw Error(`WFS XML parser: mismatched closing tag </${t}> (expected </${r.qname}>)`);
				a.pop(), i = e + 1;
				continue;
			}
			let e = fl(n, i);
			if (e === -1) throw Error("WFS XML parser: unterminated opening tag");
			let t = n.slice(i + 1, e), r = t.endsWith("/"), { name: c, attributes: l } = pl(r ? t.slice(0, -1).trim() : t.trim());
			if (s.elements += 1, s.elements > K.maxElements) throw Error(`WFS XML parser: document exceeds the ${K.maxElements}-element limit`);
			if (a.length + 1 > K.maxDepth) throw Error(`WFS XML parser: document exceeds the ${K.maxDepth}-level depth limit`);
			let u = gl(c), d = a[a.length - 1], f = Object.assign(Object.create(null), d?.namespaces);
			for (let [e, t] of Object.entries(l)) e === "xmlns" ? f[""] = t : e.startsWith("xmlns:") && (f[e.slice(6)] = t);
			let p = {
				local: u,
				qname: c,
				attributes: l,
				namespaces: f,
				children: [],
				text: ""
			};
			if (d) d.children.push(p);
			else if (o) throw Error("WFS XML parser: multiple root elements");
			else o = p;
			r || a.push(p), i = e + 1;
			continue;
		}
		let e = n.indexOf("<", i), t = n.slice(i, e === -1 ? r : e), c = a[a.length - 1];
		c && ml(c, _l(t), s), i = e === -1 ? r : e;
	}
	if (a.length > 0) throw Error(`WFS XML parser: unclosed element <${a[a.length - 1].qname}>`);
	if (!o) throw Error("WFS XML parser: no root element");
	return o;
}
function dl(e) {
	let t = e;
	return t.charCodeAt(0) === 65279 && (t = t.slice(1)), t.trim();
}
function fl(e, t) {
	let n = t + 1, r;
	for (; n < e.length;) {
		let t = e[n];
		if (r) t === r && (r = void 0);
		else if (t === "\"" || t === "'") r = t;
		else if (t === ">") return n;
		n += 1;
	}
	return -1;
}
function pl(e) {
	let t = e.trim();
	if (!t) throw Error("WFS XML parser: empty tag");
	let n = 0;
	for (; n < t.length && !/\s/.test(t[n]);) n += 1;
	let r = t.slice(0, n);
	if (!r) throw Error("WFS XML parser: missing tag name");
	let i = Object.create(null), a = 0, o = 0, s = n;
	for (; s < t.length;) {
		for (; s < t.length && /\s/.test(t[s]);) s += 1;
		if (s >= t.length) break;
		let e = t.indexOf("=", s);
		if (e === -1) throw Error("WFS XML parser: attribute lacks '='");
		let n = t.slice(s, e).trim();
		if (n.length === 0 || /\s/.test(n)) throw Error("WFS XML parser: invalid attribute name");
		if (a += 1, a > K.maxAttributesPerElement) throw Error("WFS XML parser: element exceeds the bounded attribute count");
		let r = e + 1;
		for (; r < t.length && /\s/.test(t[r]);) r += 1;
		let c = t[r];
		if (c !== "\"" && c !== "'") throw Error(`WFS XML parser: attribute "${n}" must be quoted`);
		let l = t.indexOf(c, r + 1);
		if (l === -1) throw Error(`WFS XML parser: unterminated attribute value for "${n}"`);
		let u = t.slice(r + 1, l), d = n.startsWith("xmlns:") ? n : gl(n), f = _l(u);
		if (o += hl(n) + hl(f), o > K.maxAttributeBytesPerElement) throw Error("WFS XML parser: element exceeds the bounded attribute-byte limit");
		if (Object.hasOwn(i, d)) throw Error(`WFS XML parser: repeated attribute "${d}"`);
		i[d] = f, s = l + 1;
	}
	return {
		name: r,
		attributes: i
	};
}
function ml(e, t, n) {
	if (t.length !== 0) {
		if (n.textBytes += hl(t), n.textBytes > K.maxTextBytes) throw Error(`WFS XML parser: document exceeds the ${K.maxTextBytes}-byte text limit`);
		e.text += t;
	}
}
function hl(e) {
	return new TextEncoder().encode(e).byteLength;
}
function gl(e) {
	let t = e.indexOf(":");
	return t === -1 ? e : e.slice(t + 1);
}
function _l(e) {
	return e.includes("&") ? e.replaceAll("&lt;", "<").replaceAll("&gt;", ">").replaceAll("&quot;", "\"").replaceAll("&apos;", "'").replaceAll("&amp;", "&") : e;
}
function q(e, t) {
	if (e) {
		for (let n of e.children) if (n.local === t) return n;
	}
}
function vl(e, t) {
	let n = q(e, t);
	if (!n) throw Error(`WFS XML parser: missing child <${t}> on <${e.qname}>`);
	return n;
}
function J(e, t) {
	return e ? e.children.filter((e) => e.local === t) : [];
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/wfs.js
var yl = "2.0.0", Y = "WFS", bl = 2 * 1024 * 1024, xl = 32 * 1024 * 1024, Sl = /* @__PURE__ */ new Set([
	"application/json",
	"application/geo+json",
	"application/vnd.geo+json",
	"json",
	"geojson"
]), Cl = /* @__PURE__ */ new Set([
	"application/gml+xml; version=3.2",
	"application/gml+xml;version=3.2",
	"application/gml+xml",
	"gml3.2",
	"gml32",
	"text/xml; subtype=gml/3.2",
	"text/xml;subtype=gml/3.2",
	"text/xml; subtype=gml/3.2.1",
	"text/xml;subtype=gml/3.2.1"
]), wl = /* @__PURE__ */ new WeakMap(), Tl = class {
	client;
	endpointUrl;
	version;
	capabilitiesFetches = 0;
	rawCapabilitiesXml;
	resolvedSnapshot;
	geometryPropertyNames = /* @__PURE__ */ new Map();
	constructor(e) {
		this.client = e.client, this.endpointUrl = e.endpointUrl, this.version = e.version ?? yl, wl.set(this, { generation: 0 });
	}
	async capabilities(e) {
		if (e?.signal?.aborted) throw new c();
		if (this.resolvedSnapshot) return this.resolvedSnapshot;
		let t = El(this), n = t.generation, r = t.flight;
		if (!r || r.generation !== n) {
			let e = {}, i = new AbortController(), a = this.fetchCapabilities({ signal: i.signal }).then((r) => (t.generation === n && t.flight?.identity === e && (this.rawCapabilitiesXml = r.xml, this.resolvedSnapshot = r.snapshot), r.snapshot)), o = {
				generation: n,
				identity: e,
				controller: i,
				promise: a,
				subscribers: 0,
				settled: !1
			};
			r = o, t.flight = o, a.then(() => {
				o.settled = !0, t.flight === o && (t.flight = void 0);
			}, () => {
				o.settled = !0, t.flight === o && (t.flight = void 0);
			});
		}
		return Dl(t, r, e?.signal);
	}
	refresh() {
		let e = El(this);
		e.generation += 1;
		let t = e.flight;
		e.flight = void 0, this.rawCapabilitiesXml = void 0, this.resolvedSnapshot = void 0, this.geometryPropertyNames.clear(), t && !t.settled && t.controller.abort();
	}
	async describeFeatureType(e, t) {
		let n = new URLSearchParams({
			service: Y,
			version: this.version,
			request: "DescribeFeatureType",
			typeNames: e
		}), r = {
			accept: "application/xml, text/xml",
			maxResponseBytes: bl
		};
		t?.signal && (r.signal = t.signal);
		let { text: i, contentType: a } = await this.requestText("GET", X(this.operationUrl("DescribeFeatureType", "GET"), n), r);
		return {
			text: i,
			contentType: a
		};
	}
	async geometryPropertyName(e, t) {
		let n = this.geometryPropertyNames.get(e);
		if (n !== void 0) return n;
		let r;
		try {
			let { text: n } = await this.describeFeatureType(e, t);
			r = il(n, e);
		} catch (t) {
			throw t instanceof c ? t : new ni("unresolved-geometry-property", `WFS DescribeFeatureType could not resolve the geometry property of feature type "${e}"; set locator.geometryName to name it explicitly`, { cause: t });
		}
		let i = r[0];
		if (i === void 0) throw new ni("unresolved-geometry-property", `WFS DescribeFeatureType declares no geometry property for feature type "${e}"; set locator.geometryName to name it explicitly`);
		return this.geometryPropertyNames.set(e, i), i;
	}
	operationUrl(e, t) {
		let n = this.resolvedSnapshot?.operations.get(e);
		return (t === "GET" ? n?.getUrl : n?.postUrl) ?? this.endpointUrl;
	}
	rawCapabilities() {
		return this.rawCapabilitiesXml;
	}
	featureType(e) {
		return new Al({
			root: this,
			typeName: e
		});
	}
	async storedQueries(e) {
		let t = new URLSearchParams({
			service: Y,
			version: this.version,
			request: "ListStoredQueries"
		}), n = {
			accept: "application/xml, text/xml",
			maxResponseBytes: bl
		};
		e?.signal && (n.signal = e.signal);
		let { text: r } = await this.requestText("GET", X(this.endpointUrl, t), n);
		return tl(r);
	}
	storedQuery(e) {
		return new jl({
			root: this,
			id: e
		});
	}
	async requestText(e, t, n) {
		try {
			let r = {
				accept: n.accept,
				maxResponseBytes: n.maxResponseBytes ?? bl
			};
			return n.contentType !== void 0 && (r.contentType = n.contentType), n.body !== void 0 && (r.body = n.body), n.signal !== void 0 && (r.signal = n.signal), await this.client.requestText(e, t, r);
		} catch (e) {
			throw Fl(e);
		}
	}
	negotiateOutputFormat(e) {
		let t = e.outputFormatsByOp.get("GetFeature") ?? [];
		for (let e of t) if (Sl.has(e.toLowerCase())) return {
			kind: "json",
			format: e
		};
		for (let e of t) if (Cl.has(e.toLowerCase())) return {
			kind: "gml",
			format: e
		};
		return t.length === 0 ? {
			kind: "json",
			format: "application/geo+json"
		} : {
			kind: "gml",
			format: t[0]
		};
	}
	async fetchCapabilities(e) {
		this.capabilitiesFetches += 1;
		let t = new URLSearchParams({
			service: Y,
			version: this.version,
			request: "GetCapabilities"
		}), n = {
			accept: "application/xml, text/xml",
			maxResponseBytes: bl
		};
		e?.signal && (n.signal = e.signal);
		let { text: r } = await this.requestText("GET", X(this.endpointUrl, t), n);
		return {
			snapshot: Ol(qc(r), this.endpointUrl, this.client.serverBaseUrl),
			xml: r
		};
	}
};
function El(e) {
	let t = wl.get(e);
	if (!t) throw TypeError("WFS capabilities state is unavailable");
	return t;
}
function Dl(e, t, n) {
	return t.subscribers += 1, new Promise((r, i) => {
		let a = !1, o = () => a ? !1 : (a = !0, n?.removeEventListener("abort", l), t.controller.signal.removeEventListener("abort", u), --t.subscribers, !0), s = () => {
			t.settled || t.subscribers !== 0 || t.controller.signal.aborted || (e.flight === t && (e.flight = void 0), t.controller.abort());
		}, l = () => {
			o() && (i(new c()), s());
		}, u = () => {
			o() && i(new c("WFS capabilities discovery was invalidated"));
		};
		n?.addEventListener("abort", l, { once: !0 }), t.controller.signal.addEventListener("abort", u, { once: !0 }), n?.aborted ? l() : t.controller.signal.aborted && u(), t.promise.then((e) => {
			o() && r(e);
		}, (e) => {
			o() && i(e);
		});
	});
}
function Ol(e, t, n) {
	let r = new URL(t, n), i = new Map([...e.operations].map(([e, t]) => [e, Object.freeze({
		...t,
		...t.getUrl ? { getUrl: new URL(t.getUrl, r).toString() } : {},
		...t.postUrl ? { postUrl: new URL(t.postUrl, r).toString() } : {}
	})]));
	return Object.freeze({
		...e,
		operations: kl(i)
	});
}
function kl(e) {
	let t = new Map(e), n = {
		get size() {
			return t.size;
		},
		get(e) {
			return t.get(e);
		},
		has(e) {
			return t.has(e);
		},
		forEach(e, r) {
			for (let [i, a] of t) e.call(r, a, i, n);
		},
		entries() {
			return t.entries();
		},
		keys() {
			return t.keys();
		},
		values() {
			return t.values();
		},
		[Symbol.iterator]() {
			return t[Symbol.iterator]();
		}
	};
	return Object.freeze(n);
}
var Al = class {
	root;
	typeName;
	constructor(e) {
		this.root = e.root, this.typeName = e.typeName;
	}
	capabilities(e) {
		return this.root.capabilities(e);
	}
	describeFeatureType(e) {
		return this.root.describeFeatureType(this.typeName, e);
	}
	geometryPropertyName(e) {
		return this.root.geometryPropertyName(this.typeName, e);
	}
	async getFeature(e) {
		let t = e.method ?? "GET", n = {
			accept: e.outputFormat ?? "application/geo+json, application/json;q=0.9, application/xml;q=0.5",
			maxResponseBytes: xl
		};
		e.signal && (n.signal = e.signal);
		let r;
		if (t === "POST" && e.body !== void 0) n.contentType = "application/xml", n.body = e.body, r = await this.root.requestText("POST", this.root.operationUrl("GetFeature", "POST"), n);
		else {
			let t = new URLSearchParams({
				service: Y,
				version: this.root.version,
				request: "GetFeature",
				typeNames: this.typeName
			});
			e.namespace !== void 0 && t.set("NAMESPACES", `xmlns(${e.namespace.prefix},${e.namespace.uri})`), e.filter !== void 0 && t.set("filter", e.filter), e.bbox !== void 0 && t.set("bbox", e.bbox), e.propertyName && e.propertyName.length > 0 && t.set("propertyName", e.propertyName.join(",")), e.sortBy !== void 0 && t.set("sortBy", e.sortBy), typeof e.count == "number" && t.set("count", String(e.count)), typeof e.startIndex == "number" && t.set("startIndex", String(e.startIndex)), e.resultType !== void 0 && t.set("resultType", e.resultType), e.srsName !== void 0 && t.set("srsName", e.srsName), e.outputFormat !== void 0 && t.set("outputFormat", e.outputFormat), r = await this.root.requestText("GET", X(this.root.operationUrl("GetFeature", "GET"), t), n);
		}
		if (Ml(r.contentType, r.text)) return {
			kind: "json",
			data: Pl(r.text),
			contentType: r.contentType
		};
		if (Nl(r.contentType, r.text) && /ExceptionReport/i.test(r.text)) {
			let e = Zc(r.text);
			throw new i(e.exceptionCode, e.message, e.locator);
		}
		return {
			kind: "raw",
			text: r.text,
			contentType: r.contentType
		};
	}
	async getPropertyValue(e) {
		let t = new URLSearchParams({
			service: Y,
			version: this.root.version,
			request: "GetPropertyValue",
			typeNames: this.typeName,
			valueReference: e.valueReference
		});
		e.filter !== void 0 && t.set("filter", e.filter), typeof e.count == "number" && t.set("count", String(e.count)), typeof e.startIndex == "number" && t.set("startIndex", String(e.startIndex));
		let n = {
			accept: "application/xml, text/xml",
			maxResponseBytes: xl
		};
		e.signal && (n.signal = e.signal);
		let { text: r, contentType: i } = await this.root.requestText("GET", X(this.root.endpointUrl, t), n);
		return {
			text: r,
			contentType: i
		};
	}
	async transaction(e) {
		let t = {
			accept: "application/xml, text/xml",
			contentType: "application/xml",
			body: e.body
		};
		e.signal && (t.signal = e.signal);
		let { text: n } = await this.root.requestText("POST", this.root.operationUrl("Transaction", "POST"), t);
		return cl(n);
	}
	buildGetFeatureUrl(e) {
		let t = new URLSearchParams({
			service: Y,
			version: this.root.version,
			request: "GetFeature",
			typeNames: this.typeName
		});
		return e.filter !== void 0 && t.set("filter", e.filter), e.bbox !== void 0 && t.set("bbox", e.bbox), typeof e.count == "number" && t.set("count", String(e.count)), typeof e.startIndex == "number" && t.set("startIndex", String(e.startIndex)), e.outputFormat !== void 0 && t.set("outputFormat", e.outputFormat), e.namespace !== void 0 && t.set("NAMESPACES", `xmlns(${e.namespace.prefix},${e.namespace.uri})`), X(this.root.endpointUrl, t);
	}
}, jl = class {
	root;
	id;
	constructor(e) {
		this.root = e.root, this.id = e.id;
	}
	async describe(e) {
		let t = new URLSearchParams({
			service: Y,
			version: this.root.version,
			request: "DescribeStoredQueries",
			storedQuery_Id: this.id
		}), n = { accept: "application/xml, text/xml" };
		e?.signal && (n.signal = e.signal);
		let { text: r, contentType: i } = await this.root.requestText("GET", X(this.root.endpointUrl, t), n);
		return {
			text: r,
			contentType: i
		};
	}
	async execute(e) {
		let t = new URLSearchParams({
			service: Y,
			version: this.root.version,
			request: "GetFeature",
			storedquery_id: this.id
		});
		if (typeof e.count == "number" && t.set("count", String(e.count)), typeof e.startIndex == "number" && t.set("startIndex", String(e.startIndex)), e.outputFormat !== void 0 && t.set("outputFormat", e.outputFormat), e.parameters) for (let [n, r] of Object.entries(e.parameters)) t.set(n, String(r));
		let n = {
			accept: e.outputFormat ?? "application/geo+json, application/json;q=0.9, application/xml;q=0.5",
			maxResponseBytes: xl
		};
		e.signal && (n.signal = e.signal);
		let { text: r, contentType: a } = await this.root.requestText("GET", X(this.root.operationUrl("GetFeature", "GET"), t), n);
		if (Ml(a, r)) return {
			kind: "json",
			data: Pl(r),
			contentType: a
		};
		if (Nl(a, r) && /ExceptionReport/i.test(r)) {
			let e = Zc(r);
			throw new i(e.exceptionCode, e.message, e.locator);
		}
		return {
			kind: "raw",
			text: r,
			contentType: a
		};
	}
};
function X(e, t) {
	return `${e}${e.includes("?") ? "&" : "?"}${t.toString()}`;
}
function Ml(e, t) {
	if (e && /json/i.test(e)) return !0;
	let n = t.trimStart();
	return n.startsWith("{") || n.startsWith("[");
}
function Nl(e, t) {
	return e && /xml/i.test(e) ? !0 : t.trimStart().startsWith("<");
}
function Pl(e) {
	try {
		return JSON.parse(e);
	} catch (e) {
		let t = e instanceof Error ? e.message : String(e);
		throw Error(`WFS GetFeature returned non-JSON output despite Accept negotiation: ${t}`);
	}
}
function Fl(e) {
	if (!(e instanceof a)) return e;
	let t = e.body;
	if (!t || typeof t != "object" || typeof t.raw != "string") return e;
	let n = t.raw;
	if (!/ExceptionReport/i.test(n)) return e;
	try {
		let e = Zc(n);
		return new i(e.exceptionCode, e.message, e.locator);
	} catch {
		return e;
	}
}
//#endregion
//#region node_modules/@honua/sdk-js/dist/src/core/client.js
function Il(e) {
	return ie(e);
}
function Ll(e) {
	return e.startsWith("http://") || e.startsWith("https://") || e.startsWith("/") ? e : `/${e}`;
}
function Rl(e) {
	return e.startsWith("http://") || e.startsWith("https://");
}
function Z(e, t) {
	if (Rl(t)) {
		if (!Rl(e)) throw Error("Absolute request URLs are not allowed when baseUrl is relative.");
		let n = new URL(e).origin, r = new URL(t);
		if (r.origin !== n) throw Error(`Cross-origin request URL is not allowed: ${t}`);
		return r.toString();
	}
	return `${e}${t}`;
}
var zl = 20, Bl = /* @__PURE__ */ new Set([
	301,
	302,
	303,
	307,
	308
]);
function Vl(e, t, n) {
	if (!n) throw Error("Redirect response is missing a Location header.");
	let r;
	try {
		r = new URL(n, t);
	} catch {
		throw Error(`Redirect response has an invalid Location header: ${n}`);
	}
	return Z(e, r.toString());
}
function Hl(e, t) {
	return Rl(t) ? Z(e, t) : Rl(e) ? Z(e, Ll(t)) : t;
}
var Ul = 6e4, Wl = 256, Gl = class e {
	static minimumSupportedServerVersion = za;
	static minimumSupportedServerReleaseChannel = Ba;
	baseUrl;
	fetchFn;
	defaultHeaders;
	authProvider;
	authRefreshSkewMs;
	interceptors;
	timeoutMs;
	retryOptions;
	preferBinary;
	transport;
	protocolTransport;
	serverCompatibilityCache;
	authCredentialsCache;
	authRefreshPromise;
	connectClient;
	connectClientPromise;
	metadataCache = /* @__PURE__ */ new Map();
	ogcLayoutCache = /* @__PURE__ */ new Map();
	wfsRootCache = /* @__PURE__ */ new Map();
	constructor(e) {
		this.baseUrl = Il(e.baseUrl), this.fetchFn = (e.fetchFn ?? fetch).bind(globalThis);
		let t = {};
		e.apiKey && (t["X-API-Key"] = e.apiKey), e.bearerToken && (t.Authorization = `Bearer ${e.bearerToken}`), this.defaultHeaders = t, this.authProvider = eu(e.auth), this.authRefreshSkewMs = tu(e.authRefreshSkewMs), this.interceptors = e.interceptors ?? [], this.timeoutMs = kc(e.timeoutMs), this.retryOptions = Ac(e.retry), this.preferBinary = e.preferBinary === !0, this.transport = e.transport ?? "rest", this.protocolTransport = {
			baseUrl: this.baseUrl,
			requestJson: (e, t, n, r, i) => this.requestJson(e, t, n, r, i),
			requestText: (e, t, n) => this.requestText(e, t, n),
			requestBytes: (e, t, n, r, i) => this.requestBytes(e, t, n, r, i),
			requestCachedMetadataJson: (e, t, n) => this.requestCachedMetadataJson(e, t, n),
			pipelineFetch: (e, t, n, r, i) => this.pipelineFetch(e, t, n, r, i),
			requestBinaryWithJsonFallback: (e, t, n, r) => this.requestBinaryWithJsonFallback(e, t, n, r)
		};
	}
	buildConnectAuthInterceptor() {
		return (e) => async (t) => {
			let n = await this.composeHeaders();
			for (let [e, r] of Object.entries(n)) t.header.set(e, r);
			return e(t);
		};
	}
	buildConnectRetryInterceptor() {
		return (e) => async (t) => {
			if (t.stream || !this.retryOptions) return e(t);
			let n = this.retryOptions;
			for (let r = 0;; r += 1) try {
				return await e(t);
			} catch (e) {
				let i = t.signal?.aborted === !0;
				if (!Rc(n, r, e, i)) throw e;
				await Vc(Bc(n, r, e), t.signal);
			}
		};
	}
	connectTransportOptions() {
		let e = [this.buildConnectAuthInterceptor()];
		return this.retryOptions && e.unshift(this.buildConnectRetryInterceptor()), {
			baseUrl: this.baseUrl,
			fetch: this.fetchFn,
			interceptors: e,
			...this.timeoutMs === void 0 ? {} : { defaultTimeoutMs: this.timeoutMs }
		};
	}
	async ensureConnectClient() {
		if (this.connectClient) return this.connectClient;
		let e = this.connectClientPromise;
		e || (e = gu().then(({ createClient: e, createGrpcWebTransport: t, FeatureService: n }) => {
			let r = t(this.connectTransportOptions());
			return this.connectClient = e(n, r), this.connectClient;
		}), this.connectClientPromise = e);
		try {
			return await e;
		} catch (t) {
			throw this.connectClientPromise === e && (this.connectClientPromise = void 0), Us(t);
		}
	}
	async fetchWithSafeRedirects(e, t, n = "safe-follow") {
		let r = e, i = t;
		for (let e = 0;; e += 1) {
			let t = await this.fetchFn(r, {
				...i,
				redirect: n === "error" ? "error" : "manual"
			});
			if (n === "error") {
				if (t.type === "opaqueredirect" || t.redirected || Bl.has(t.status) || t.url !== "" && t.url !== r) throw await t.body?.cancel().catch(() => void 0), new s("Redirects are not allowed for this bounded request.", void 0);
				return t;
			}
			if (t.type === "opaqueredirect") throw new s("Refusing to follow an opaque cross-origin redirect; the request's API key would be leaked to the redirect target.", void 0);
			if (!Bl.has(t.status) || n === "manual") return t;
			if (e >= zl) throw new s(`Exceeded the maximum of ${zl} redirects.`, void 0);
			let a = t.headers.get("location"), o = Vl(this.baseUrl, r, a), c = (i.method ?? "GET").toUpperCase(), l = t.status === 303 || (t.status === 301 || t.status === 302) && c !== "GET" && c !== "HEAD";
			if (n === "preserve-method" && l && c !== "GET" && c !== "HEAD") return t;
			i = l ? {
				...i,
				method: "GET",
				body: null
			} : i, r = o, await t.body?.cancel().catch(() => void 0);
		}
	}
	static async loadGrpcAdapter() {
		let e;
		try {
			e = await mu(pu);
		} catch (e) {
			throw Us(e);
		}
		if (!_u(e)) throw Error("HonuaClient gRPC adapter loaded but did not export the query runtime.");
		return e;
	}
	get isGrpcWeb() {
		return this.transport === "grpc-web";
	}
	get serverBaseUrl() {
		return this.baseUrl;
	}
	async refreshAuthCredentials(e = "manual") {
		return (await this.resolveAuthCredentials({
			forceRefresh: !0,
			reason: e
		}))?.credentials;
	}
	async getAuthHeaders() {
		return this.composeHeaders();
	}
	async getAuthToken() {
		let e = await this.composeHeaders(), t = e.Authorization ?? e.authorization;
		return t ? t.startsWith("Bearer ") ? t.slice(7) : t : e["X-API-Key"] ?? e["x-api-key"];
	}
	clearAuthCredentials() {
		this.authCredentialsCache = void 0;
	}
	async revokeAuthCredentials(e = { reason: "manual" }) {
		let t = this.authCredentialsCache;
		this.authCredentialsCache = void 0, t && this.authProvider?.revokeCredentials && await this.authProvider.revokeCredentials(t.credentials, e);
	}
	async *queryFeaturesStream(t) {
		let n = await this.ensureConnectClient(), r = await e.loadGrpcAdapter(), i = r.toProtoQueryRequest(t);
		yield* r.streamProtoPages(n.queryFeaturesStream(i, t.signal ? { signal: t.signal } : void 0));
	}
	service(e) {
		return new Bn({
			client: this,
			serviceId: e
		});
	}
	featureLayer(e, t) {
		return new Vn({
			client: this,
			serviceId: e,
			layerId: t
		});
	}
	mapService(e) {
		return new Hn({
			client: this,
			serviceId: e
		});
	}
	mapLayer(e, t) {
		return new Un({
			client: this,
			serviceId: e,
			layerId: t
		});
	}
	ogcFeatures() {
		return new Gn({ client: this });
	}
	ogcTiles() {
		return new qt({ client: this });
	}
	ogcMaps() {
		return new pt({ client: this });
	}
	ogcRecords() {
		return new Ot({ client: this });
	}
	wms(e) {
		return new zi({
			client: this,
			serviceId: e
		});
	}
	wmts(e) {
		return new ha({
			client: this,
			serviceId: e
		});
	}
	ogcProcesses(e = {}) {
		return new _s({
			...e,
			client: this
		});
	}
	stac() {
		return new bn({ client: this });
	}
	imageService(e) {
		return new qn({
			client: this,
			serviceId: e
		});
	}
	geometryService() {
		return new Qn({ client: this });
	}
	geoprocessing(e, t) {
		return new ir({
			client: this,
			serviceId: e,
			taskName: t
		});
	}
	processRunner(e) {
		return hc(e);
	}
	ogcProcessRunner(e = {}) {
		return hc(gc(this.ogcProcesses(e)));
	}
	geoprocessingRunner(e, t) {
		return hc(_c(this.geoprocessing(e, t)));
	}
	geospatialGrpcProcessRunner(e) {
		return hc(vc(e));
	}
	wfs(e = "/wfs", t = {}) {
		let n = t.version ?? "2.0.0", r = `${e}\u0000${n}`, i = this.wfsRootCache.get(r);
		if (i) return i;
		let a = new Tl({
			client: this,
			endpointUrl: e,
			version: n
		});
		return this.wfsRootCache.set(r, a), a;
	}
	odata(e, t = {}) {
		return new ue({
			client: this,
			entitySet: e,
			basePath: t.basePath
		});
	}
	clearMetadataCache(e = {}) {
		if (!e.keyPrefix) {
			this.metadataCache.clear();
			return;
		}
		let t = `metadata:${e.keyPrefix}`;
		for (let e of this.metadataCache.keys()) e.startsWith(t) && this.metadataCache.delete(e);
	}
	async listServices(e = "json", t = {}) {
		let n = typeof e == "string" ? e : "json", r = typeof e == "string" ? t : e;
		return ro(this.protocolTransport, n, r);
	}
	async getCompatibility(e = {}) {
		if (!e.refresh && this.serverCompatibilityCache) return this.serverCompatibilityCache;
		let t = Wa(await this.requestJson("GET", "/api/v1/admin/capabilities", void 0, e.signal));
		return this.serverCompatibilityCache = t, t;
	}
	async checkCompatibility(t = {}) {
		try {
			let n = await this.getCompatibility(t), r = Za(n);
			return {
				supported: r.length === 0,
				minimumSupportedServerVersion: e.minimumSupportedServerVersion,
				compatibility: n,
				reasons: r
			};
		} catch (t) {
			return {
				supported: !1,
				minimumSupportedServerVersion: e.minimumSupportedServerVersion,
				reasons: [eo(t)]
			};
		}
	}
	async supportsFeature(e, t = {}) {
		return (await this.getCompatibility(t)).features[e];
	}
	async scanMigrationSource(e) {
		let { signal: t, exportJson: n, ...r } = e, i = `/api/v1/admin/import/scan${n ? "?export=json" : ""}`;
		return this.requestJson("POST", i, {
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify(r)
		}, t);
	}
	async request(e) {
		let t = e.method ?? "GET", n = new URLSearchParams();
		if (n.set("f", e.responseFormat ?? "json"), e.query) for (let [t, r] of Object.entries(e.query)) n.set(t, String(r));
		let r = lu(Ll(e.path), n);
		return this.requestJson(t, r, {
			headers: e.headers,
			body: e.body
		}, e.signal);
	}
	async pipelineRequestJson(e, t, n, r) {
		return this.requestJson(e, t, n, r);
	}
	async composeHeaders(...e) {
		let t = await this.resolveAuthHeaders();
		return S(this.defaultHeaders, t, ...e);
	}
	async resolveAuthHeaders() {
		let e = await this.resolveAuthCredentials({ forceRefresh: !1 });
		if (e) return ou(e.credentials);
	}
	async resolveAuthCredentials(e) {
		if (!this.authProvider) return;
		let t = this.authCredentialsCache;
		if (!e.forceRefresh && t && !iu(t, this.authRefreshSkewMs)) return t;
		if (this.authRefreshPromise) return this.authRefreshPromise;
		let n = e.reason ?? au(t);
		this.authRefreshPromise = this.loadAuthCredentials(n, e.forceRefresh, t?.credentials);
		try {
			return await this.authRefreshPromise;
		} finally {
			this.authRefreshPromise = void 0;
		}
	}
	async loadAuthCredentials(e, t, n) {
		let r = nu(await this.authProvider?.getCredentials({
			reason: e,
			forceRefresh: t,
			...n ? { previousCredentials: n } : {}
		}));
		if (!r) {
			this.authCredentialsCache = void 0;
			return;
		}
		let i = {
			credentials: r,
			expiresAtMs: ru(r.expiresAt)
		};
		return this.authCredentialsCache = i, i;
	}
	async pipelineFetch(e, t, n, r, i = {}) {
		let a = {
			url: Z(this.baseUrl, t),
			path: t,
			method: e,
			init: {
				...n,
				method: e,
				headers: await this.composeHeaders(n?.headers),
				body: n?.body ?? null,
				...n?.signal ? { signal: n.signal } : {}
			}
		};
		return this.executeRequest(a, {
			callerSignal: r,
			...i.okStatuses ? { okStatuses: i.okStatuses } : {},
			...i.redirect ? { redirect: i.redirect } : {},
			...i.beforeAttempt ? { beforeAttempt: i.beforeAttempt } : {},
			...i.beforeReplay ? { beforeReplay: i.beforeReplay } : {},
			deadlineThroughFinalize: !0,
			streamResponse: i.prepareResponse === void 0,
			...i.errorBody ? { errorBody: i.errorBody } : i.discardErrorBody ? { errorBody: (e) => (e.body?.cancel().catch(() => void 0), Promise.resolve({})) } : {},
			finalize: async (e, t, n, r, a) => {
				let o = i.prepareResponse ? await i.prepareResponse(e, a) : e, s = o === e ? e : Zl(o, e);
				return await r(s), s;
			}
		});
	}
	async requestCachedMetadataJson(e, t, n = {}) {
		let r = d(n), i = `metadata:${e}${r.maxResponseBytes === void 0 ? "" : `:max-response-bytes:${r.maxResponseBytes}`}`, a = this.metadataCache.get(i), o = r.cache === "bypass", s = Date.now(), c = a ? f(a.cachedAtMs, s, r.ttlMs) : !1;
		if (!o && !r.refresh && a && c) return h(a.body, su(a, "hit", {
			now: s,
			ttlMs: r.ttlMs,
			staleIfErrorMs: r.staleIfErrorMs
		}));
		let l = {
			url: Z(this.baseUrl, t),
			path: t,
			method: "GET",
			init: {
				method: "GET",
				headers: await this.composeHeaders(m({
					accept: "application/json",
					refresh: r.refresh || !!a,
					bypass: o,
					validator: a?.validator
				}))
			}
		};
		return this.executeRequest(l, {
			callerSignal: r.signal,
			deadlineThroughFinalize: !0,
			onTerminalError: (e) => this.staleMetadataFallback(a, r, e),
			shortCircuit: async (e, t, n, s) => {
				if (e.status !== 304 || !a || o) return;
				await s();
				let c = {
					...a,
					cachedAtMs: Date.now(),
					...p(e.headers) ?? a.validator ? { validator: p(e.headers) ?? a.validator } : {}
				};
				return this.setMetadataCacheEntry(i, c), { value: h(c.body, su(c, "refreshed", {
					now: Date.now(),
					ttlMs: r.ttlMs,
					staleIfErrorMs: r.staleIfErrorMs,
					revalidatedAt: (/* @__PURE__ */ new Date()).toISOString()
				})) };
			},
			finalize: async (e, t, n, s, c) => {
				let l = await ql(this.hasAfterInterceptors() ? e.clone() : e, r.maxResponseBytes, c), u = Gc(e.status, l, e.headers);
				if (u) {
					let e = this.staleMetadataFallback(a, r, u);
					if (e) return e;
					throw await this.applyErrorInterceptors({
						request: $(n),
						error: u,
						durationMs: t
					}), u;
				}
				await s();
				let d = ee(l), f = p(e.headers), m = e.headers.get("last-modified") ?? void 0, te = {
					body: d,
					cachedAtMs: Date.now(),
					keyFingerprint: i,
					...f ? { validator: f } : {},
					...m ? { sourceUpdatedAt: m } : {}
				}, g = o ? "bypass" : a ? "refreshed" : "miss";
				return o || this.setMetadataCacheEntry(i, te), h(d, su(te, g, {
					now: Date.now(),
					ttlMs: r.ttlMs,
					staleIfErrorMs: r.staleIfErrorMs,
					...g === "refreshed" ? { revalidatedAt: (/* @__PURE__ */ new Date()).toISOString() } : {}
				}));
			}
		});
	}
	staleMetadataFallback(e, t, n) {
		if (!e || t.cache === "bypass" || !t.staleIfError) return;
		let r = t.staleIfErrorMs ?? 36e5;
		if (!(Date.now() - e.cachedAtMs > r)) return h(e.body, su(e, "stale", {
			now: Date.now(),
			ttlMs: t.ttlMs,
			staleIfErrorMs: t.staleIfErrorMs,
			refreshErrorId: cu(n)
		}));
	}
	setMetadataCacheEntry(e, t) {
		for (this.metadataCache.set(e, t); this.metadataCache.size > Wl;) {
			let e = [...this.metadataCache.entries()].sort((e, t) => e[1].cachedAtMs - t[1].cachedAtMs)[0];
			if (!e) return;
			this.metadataCache.delete(e[0]);
		}
	}
	async getLayerMetadata(e, t, n = {}) {
		return io(this.protocolTransport, e, t, n);
	}
	async getFeatureServiceMetadata(e, t = {}) {
		return ao(this.protocolTransport, e, t);
	}
	resolveOgcFeaturesLayout(e = "honua-facade") {
		if (e === "honua-facade") return Promise.resolve(Io());
		let t = this.ogcLayoutCache.get(e);
		return t || (t = Ho(this.protocolTransport, e).catch((t) => {
			throw this.ogcLayoutCache.delete(e), t;
		}), this.ogcLayoutCache.set(e, t)), t;
	}
	async getOgcFeaturesLanding(e = {}) {
		return Wo(this.protocolTransport, e);
	}
	async getOgcFeaturesConformance(e = {}) {
		return Go(this.protocolTransport, e);
	}
	async listOgcCollections(e = {}) {
		return Ko(this.protocolTransport, e);
	}
	async getOgcCollection(e) {
		return qo(this.protocolTransport, e);
	}
	async getOgcQueryables(e) {
		return Jo(this.protocolTransport, e);
	}
	async listOgcItems(e) {
		return Yo(this.protocolTransport, e);
	}
	async getOgcItem(e) {
		return Xo(this.protocolTransport, e);
	}
	async createOgcItem(e) {
		return Zo(this.protocolTransport, e);
	}
	async replaceOgcItem(e) {
		return Qo(this.protocolTransport, e);
	}
	async patchOgcItem(e) {
		return $o(this.protocolTransport, e);
	}
	async deleteOgcItem(e) {
		await es(this.protocolTransport, e);
	}
	async getOgcTilesLanding(e = {}) {
		return Yt(this.protocolTransport, e);
	}
	async getOgcTilesConformance(e = {}) {
		return Xt(this.protocolTransport, e);
	}
	async listOgcTileMatrixSets(e = {}) {
		return Zt(this.protocolTransport, e);
	}
	async getOgcTileMatrixSet(e) {
		return Qt(this.protocolTransport, e);
	}
	async listOgcCollectionTilesets(e) {
		return $t(this.protocolTransport, e);
	}
	async getOgcCollectionTileset(e) {
		return en(this.protocolTransport, e);
	}
	async fetchOgcTile(e) {
		return tn(this.protocolTransport, e);
	}
	async getOgcMapsLanding(e = {}) {
		return ht(this.protocolTransport, e);
	}
	async getOgcMapsConformance(e = {}) {
		return gt(this.protocolTransport, e);
	}
	async getOgcMapImage(e) {
		return _t(this.protocolTransport, e);
	}
	async getOgcRecordsLanding(e = {}) {
		return Nt(this.protocolTransport, e);
	}
	async getOgcRecordsConformance(e = {}) {
		return Pt(this.protocolTransport, e);
	}
	async listOgcRecordCollections(e = {}) {
		return Ft(this.protocolTransport, e);
	}
	async getOgcRecordCollection(e) {
		return It(this.protocolTransport, e);
	}
	async searchOgcRecords(e) {
		return Lt(this.protocolTransport, e);
	}
	async getOgcRecord(e) {
		return Rt(this.protocolTransport, e);
	}
	async fetchOgcRecordsRaw(e) {
		return zt(this.protocolTransport, e);
	}
	async fetchOgcRecordRaw(e) {
		return Bt(this.protocolTransport, e);
	}
	async getWmsCapabilities(e) {
		return Hi(this.protocolTransport, e);
	}
	async getWmsMap(e) {
		return Ui(this.protocolTransport, e);
	}
	async getWmsFeatureInfo(e) {
		return Wi(this.protocolTransport, e);
	}
	async getWmsLegend(e) {
		return Ki(this.protocolTransport, e);
	}
	async getWmtsCapabilities(e) {
		return ya(this.protocolTransport, e);
	}
	async fetchWmtsTile(e) {
		return ba(this.protocolTransport, e);
	}
	async getWmtsFeatureInfo(e) {
		return xa(this.protocolTransport, e);
	}
	async getOgcProcessesLanding(e = {}) {
		return ws(this.protocolTransport, e);
	}
	async getOgcProcessesConformance(e = {}) {
		return Ts(this.protocolTransport, e);
	}
	async listOgcProcesses(e = {}) {
		return Es(this.protocolTransport, e);
	}
	async getOgcProcess(e) {
		return Ds(this.protocolTransport, e);
	}
	async executeOgcProcess(e) {
		return Os(this.protocolTransport, e);
	}
	async getOgcProcessJob(e) {
		return Ms(this.protocolTransport, e);
	}
	async getOgcProcessJobResults(e) {
		return Ns(this.protocolTransport, e);
	}
	async cancelOgcProcessJob(e) {
		return Ps(this.protocolTransport, e);
	}
	async getStacLanding(e = {}) {
		return Tn(this.protocolTransport, e);
	}
	async listStacCollections(e = {}) {
		return En(this.protocolTransport, e);
	}
	async getStacCollection(e) {
		return Dn(this.protocolTransport, e);
	}
	async getStacItem(e) {
		return On(this.protocolTransport, e);
	}
	async searchStac(e = {}) {
		return kn(this.protocolTransport, e);
	}
	async getMapServiceMetadata(e, t = {}) {
		return oo(this.protocolTransport, e, t);
	}
	async getMapLayerMetadata(e, t, n = {}) {
		return so(this.protocolTransport, e, t, n);
	}
	async queryFeatures(t) {
		if (this.transport === "grpc-web") {
			let n = await this.ensureConnectClient(), r = await e.loadGrpcAdapter(), i = r.toProtoQueryRequest(t);
			try {
				let e = await n.queryFeatures(i, t.signal ? { signal: t.signal } : void 0);
				return r.fromProtoQueryResponse(e);
			} catch (e) {
				throw r.wrapConnectError(e);
			}
		}
		return lo(this.protocolTransport, t, this.preferBinary);
	}
	async queryMapLayer(e) {
		return uo(this.protocolTransport, e);
	}
	async applyEdits(e) {
		return fo(this.protocolTransport, e);
	}
	async queryRelatedRecords(e) {
		return po(this.protocolTransport, e);
	}
	async queryMapRelatedRecords(e) {
		return mo(this.protocolTransport, e);
	}
	async exportMap(e) {
		return ho(this.protocolTransport, e);
	}
	async getMapLegend(e) {
		return go(this.protocolTransport, e);
	}
	async identifyMap(e) {
		return _o(this.protocolTransport, e);
	}
	async findMap(e) {
		return vo(this.protocolTransport, e);
	}
	async requestJson(e, t, n, r, i) {
		let a = {
			url: Z(this.baseUrl, t),
			path: t,
			method: e,
			init: {
				method: e,
				headers: await this.composeHeaders({ Accept: "application/json" }, n?.headers),
				body: n?.body
			}
		};
		return this.executeRequest(a, {
			callerSignal: r,
			deadlineThroughFinalize: !0,
			readOnlyQuery: i?.readOnlyQuery,
			redirect: i?.readOnlyQuery ? "preserve-method" : void 0,
			finalize: async (e, t, n, r) => {
				let i = await ql(this.hasAfterInterceptors() ? e.clone() : e), a = Gc(e.status, i, e.headers);
				if (a) throw a;
				return await r(), i;
			}
		});
	}
	async requestBinaryWithJsonFallback(e, t, n, r) {
		let i = {
			url: Z(this.baseUrl, t),
			path: t,
			method: e,
			init: {
				method: e,
				headers: await this.composeHeaders({ Accept: "application/x-protobuf, application/json;q=0.9" })
			}
		};
		return this.executeRequest(i, {
			callerSignal: r,
			deadlineThroughFinalize: !0,
			finalize: async (e, i, a, o, s) => {
				if (await o(), pc(e)) try {
					return fc(await Q(e, 2 ** 53 - 1, s));
				} catch (e) {
					if (s?.aborted) throw e;
					n.set("f", "json");
					let i = `${ae(t)}?${n.toString()}`;
					return this.requestJson("GET", i, void 0, s ?? r ?? a.init.signal ?? void 0);
				}
				let c = await ql(e, void 0, s), l = Gc(e.status, c, e.headers);
				if (l) throw await this.applyErrorInterceptors({
					request: $(a),
					error: l,
					durationMs: i
				}), l;
				return c;
			}
		});
	}
	async requestText(e, t, n) {
		if (n?.maxResponseBytes !== void 0 && (!Number.isSafeInteger(n.maxResponseBytes) || n.maxResponseBytes <= 0)) throw TypeError("maxResponseBytes must be a positive safe integer.");
		let r = n?.accept ?? "*/*", i = { Accept: r };
		n?.contentType && (i["Content-Type"] = n.contentType);
		let a = {
			url: Z(this.baseUrl, t),
			path: t,
			method: e,
			init: {
				method: e,
				headers: await this.composeHeaders(i),
				...n?.body === void 0 ? {} : { body: n.body }
			}
		};
		return this.executeRequest(a, {
			callerSignal: n?.signal,
			deadlineThroughFinalize: !0,
			errorBody: async (e, t) => {
				let i = e.clone(), a = n?.maxResponseBytes === void 0 ? new TextDecoder().decode(await Q(i, 2 ** 53 - 1, t)) : new TextDecoder().decode(await Q(i, n.maxResponseBytes, t)), o = e.headers.get("content-type") ?? r;
				return a ? {
					raw: a,
					contentType: o
				} : {};
			},
			finalize: async (e, t, i, a, o) => {
				if (n?.maxResponseBytes === void 0) return await a(), {
					text: new TextDecoder().decode(await Q(e, 2 ** 53 - 1, o)),
					contentType: e.headers.get("content-type") ?? r,
					status: e.status
				};
				let s = await Q(e, n.maxResponseBytes, o);
				return await a(Zl(Jl(e, s), e)), {
					text: new TextDecoder().decode(s),
					contentType: e.headers.get("content-type") ?? r,
					status: e.status
				};
			}
		});
	}
	async requestBytes(e, t, n, r, i) {
		let a = n ?? "application/octet-stream", o = {
			url: Z(this.baseUrl, t),
			path: t,
			method: e,
			init: {
				method: e,
				headers: await this.composeHeaders({ Accept: a }, r?.headers),
				body: r?.body
			}
		};
		return this.executeRequest(o, {
			callerSignal: i,
			deadlineThroughFinalize: !0,
			finalize: async (e, t, n, r, i) => {
				await r();
				let o = await Q(e, 2 ** 53 - 1, i);
				return {
					bytes: o,
					contentType: e.headers.get("content-type") ?? a,
					empty: e.status === 204 || o.byteLength === 0
				};
			}
		});
	}
	async executeRequest(e, t) {
		let r = await this.applyBeforeInterceptors(e), i = t.readOnlyQuery === !0 && r.method === "POST" && r.method === e.method && r.url === e.url && typeof r.init.body == "string", a = t.callerSignal ?? r.init.signal ?? void 0, o = !1;
		for (let e = 0;; e += 1) {
			let s;
			await t.beforeAttempt?.($(r), e);
			let l = Uc(t.callerSignal ?? r.init.signal, this.timeoutMs), u = performance.now(), d = !1;
			try {
				s = await this.fetchWithSafeRedirects(r.url, {
					...r.init,
					method: r.method,
					signal: l.signal
				}, t.redirect), d = !0;
			} catch (o) {
				let s = performance.now() - u, c = l.didTimeout ? new n(this.timeoutMs ?? 0) : Hc(o);
				if (Nc(this.retryOptions, r.method, e, void 0, c, i)) {
					await t.beforeReplay?.($(r), void 0, "retry"), await this.sleepBeforeRetry(e, void 0, a);
					continue;
				}
				let d = t.onTerminalError?.(c);
				if (d !== void 0) return d;
				throw await this.applyErrorInterceptors({
					request: $(r),
					error: c,
					durationMs: s
				}), c;
			} finally {
				(!d || !t.deadlineThroughFinalize) && l.dispose();
			}
			let f = performance.now() - u, p = r, m = !1, h, ee, te = (e, t) => {
				if (ee) return {
					claimed: !1,
					completion: ee.completion
				};
				let n = this.applyErrorInterceptorsConcurrently({
					request: $(p),
					error: e,
					durationMs: t
				});
				return ee = { completion: n }, {
					claimed: !0,
					completion: n
				};
			}, g = async (e) => await Xl(e.catch(async (e) => {
				if (l.signal?.aborted) throw e;
				let t = te(e, performance.now() - u);
				throw t.claimed && await t.completion, e;
			}), l.signal), ne = async (e = s) => {
				try {
					await this.applyAfterInterceptors($(p), e, f, t.deadlineThroughFinalize ? l.signal : void 0);
				} catch (e) {
					throw t.deadlineThroughFinalize || await this.applyErrorInterceptors({
						request: $(p),
						error: e,
						durationMs: f
					}), e;
				}
			};
			try {
				if (t.shortCircuit) {
					let e = t.shortCircuit(s, f, p, ne), n = await (t.deadlineThroughFinalize ? g(e) : e);
					if (n) return n.value;
				}
				if (!s.ok && !t.okStatuses?.includes(s.status)) {
					let n = t.errorBody ? t.errorBody(s, t.deadlineThroughFinalize ? l.signal : void 0) : Kl(s.clone()), c = t.deadlineThroughFinalize ? await Xl(n, l.signal) : await n, u = Wc(s.status, c, s.headers);
					!o && (s.status === 401 || s.status === 403) && this.authProvider && (Oc.has(r.method) || i) && await t.beforeReplay?.($(r), s.status, "authentication");
					let d = o ? void 0 : await this.refreshReplaySafeRequestAuth(r, s.status, i);
					if (d) {
						r = d, o = !0;
						continue;
					}
					if (Nc(this.retryOptions, r.method, e, s.status, u, i)) {
						await t.beforeReplay?.($(r), s.status, "retry"), await this.sleepBeforeRetry(e, s, a);
						continue;
					}
					let p = t.onTerminalError?.(u);
					if (p !== void 0) return p;
					if (t.deadlineThroughFinalize) return await g(Promise.reject(u));
					throw await this.applyErrorInterceptors({
						request: $(r),
						error: u,
						durationMs: f
					}), u;
				}
				let u = t.streamResponse ? vu(s, l.signal, () => l.dispose(), () => l.didTimeout ? new n(this.timeoutMs ?? 0) : new c()) : void 0, d = u?.response ?? s;
				h = u?.cancel;
				let ee = t.finalize(d, f, p, ne, l.signal), te = await (t.deadlineThroughFinalize ? g(ee) : ee);
				return m = t.streamResponse === !0 && s.body !== null, te;
			} catch (o) {
				if (!t.deadlineThroughFinalize || !l.signal?.aborted) throw o;
				let s = l.didTimeout ? new n(this.timeoutMs ?? 0) : new c();
				if (l.didTimeout && Nc(this.retryOptions, r.method, e, void 0, s, i)) {
					await t.beforeReplay?.($(r), void 0, "retry"), await this.sleepBeforeRetry(e, void 0, a);
					continue;
				}
				if (l.didTimeout) {
					let e = t.onTerminalError?.(s);
					if (e !== void 0) return e;
				}
				throw te(s, performance.now() - u), s;
			} finally {
				t.deadlineThroughFinalize && !m && (l.dispose(), h?.());
			}
		}
	}
	async applyBeforeInterceptors(e) {
		let t = $(e);
		t = {
			...t,
			url: Hl(this.baseUrl, t.url)
		};
		for (let e of this.interceptors) {
			let n = await e.before?.($(t));
			n && (t = Yl(t, n), t = {
				...t,
				url: Hl(this.baseUrl, t.url)
			});
		}
		return t;
	}
	async applyAfterInterceptors(e, t, n, r) {
		for (let i of this.interceptors) {
			if (!i.after) continue;
			if (r?.aborted) throw new c();
			let a = {
				request: $(e),
				response: t.clone(),
				durationMs: n
			};
			if (await i.after(a), r?.aborted) throw new c();
		}
	}
	hasAfterInterceptors() {
		return this.interceptors.some((e) => e.after !== void 0);
	}
	async applyErrorInterceptors(e) {
		for (let t of this.interceptors) try {
			await t.error?.(e);
		} catch {}
	}
	async applyErrorInterceptorsConcurrently(e) {
		await Promise.all(this.interceptors.map(async (t) => {
			try {
				await t.error?.(e);
			} catch {}
		}));
	}
	async refreshReplaySafeRequestAuth(e, t, n = !1) {
		if (t !== 401 && t !== 403 || !this.authProvider || !Oc.has(e.method) && !n) return;
		let r = await this.resolveAuthCredentials({
			forceRefresh: !0,
			reason: "unauthorized"
		});
		if (r) return {
			...e,
			init: {
				...e.init,
				headers: S(e.init.headers, ou(r.credentials))
			}
		};
	}
	async sleepBeforeRetry(e, t, n) {
		await Vc(Pc(this.retryOptions, e, t), n);
	}
};
async function Kl(e, t, n = !1) {
	let r = t === void 0 ? await e.text() : new TextDecoder().decode(await Q(e, t));
	if (!r) return {};
	try {
		return JSON.parse(r);
	} catch {
		if (n) throw new a(e.status, "Expected a JSON response", { raw: r }, { responseHeaders: e.headers });
		return { raw: r };
	}
}
async function ql(e, t, n) {
	return Kl(n ? Jl(e, await Q(e, t ?? 2 ** 53 - 1, n)) : e, t, !0);
}
async function Q(e, t, n) {
	if (n?.aborted) throw e.body?.cancel().catch(() => void 0), new c();
	let r = e.headers.get("content-length");
	if (r !== null) {
		let n = Number(r);
		if (Number.isFinite(n) && n > t) throw e.body?.cancel().catch(() => void 0), new s(`Response body exceeds the configured ${t}-byte limit.`, void 0);
	}
	if (!e.body) return /* @__PURE__ */ new Uint8Array();
	let i = e.body.getReader(), a = [], o = 0, l = () => void i.cancel().catch(() => void 0);
	n?.addEventListener("abort", l, { once: !0 });
	try {
		for (;;) {
			if (n?.aborted) throw new c();
			let { done: e, value: r } = await i.read();
			if (n?.aborted) throw new c();
			if (e) break;
			if (o += r.byteLength, o > t) throw i.cancel().catch(() => void 0), new s(`Response body exceeds the configured ${t}-byte limit.`, void 0);
			a.push(r);
		}
	} catch (e) {
		throw n?.aborted ? new c() : e;
	} finally {
		n?.removeEventListener("abort", l), i.releaseLock();
	}
	let u = new Uint8Array(o), d = 0;
	for (let e of a) u.set(e, d), d += e.byteLength;
	return u;
}
function Jl(e, t) {
	let n = e.status === 204 || e.status === 205 || e.status === 304 ? null : t.slice();
	return new Response(n, {
		status: e.status,
		statusText: e.statusText,
		headers: e.headers
	});
}
function Yl(e, t) {
	let n = t.init === void 0 ? e.init : {
		...e.init,
		...t.init,
		headers: S(e.init.headers, t.init.headers)
	};
	return {
		url: t.url ?? e.url,
		path: e.path,
		method: t.method ?? e.method,
		init: {
			...n,
			method: t.method ?? e.method
		}
	};
}
function $(e) {
	return {
		...e,
		init: {
			...e.init,
			headers: $l(e.init.headers)
		}
	};
}
function Xl(e, t) {
	let n = Promise.resolve(e);
	return t ? t.aborted ? (n.catch(() => void 0), Promise.reject(new c())) : new Promise((e, r) => {
		let i = !1, a = (e) => {
			i || (i = !0, t.removeEventListener("abort", o), e());
		}, o = () => a(() => r(new c()));
		t.addEventListener("abort", o, { once: !0 }), n.then((t) => a(() => e(t)), (e) => a(() => r(e)));
	}) : n;
}
function Zl(e, t) {
	return Ql(e, Object.freeze({
		url: t.url,
		type: t.type,
		redirected: t.redirected
	}));
}
function Ql(e, t) {
	let n = e.clone.bind(e);
	return Object.defineProperties(e, {
		url: {
			configurable: !0,
			value: t.url
		},
		type: {
			configurable: !0,
			value: t.type
		},
		redirected: {
			configurable: !0,
			value: t.redirected
		},
		clone: {
			configurable: !0,
			value: () => Ql(n(), t)
		}
	}), e;
}
function $l(e) {
	return S(e);
}
function eu(e) {
	if (e) return typeof e == "function" ? { getCredentials: e } : {
		getCredentials: (t) => e.getCredentials(t),
		...e.revokeCredentials ? { revokeCredentials: e.revokeCredentials.bind(e) } : {}
	};
}
function tu(e) {
	return typeof e != "number" || !Number.isFinite(e) ? Ul : Math.max(0, Math.trunc(e));
}
function nu(e) {
	if (typeof e == "string") return e.length > 0 ? { bearerToken: e } : void 0;
	if (!e) return;
	let t = {};
	if (typeof e.apiKey == "string" && e.apiKey.length > 0 && (t.apiKey = e.apiKey), typeof e.bearerToken == "string" && e.bearerToken.length > 0 && (t.bearerToken = e.bearerToken), typeof e.authorization == "string" && e.authorization.length > 0 && (t.authorization = e.authorization), e.expiresAt !== void 0 && (t.expiresAt = e.expiresAt), !(!t.apiKey && !t.bearerToken && !t.authorization)) return t;
}
function ru(e) {
	if (e === void 0) return;
	if (e instanceof Date) return Number.isFinite(e.getTime()) ? e.getTime() : void 0;
	if (typeof e == "number") return Number.isFinite(e) ? e : void 0;
	let t = Date.parse(e);
	return Number.isFinite(t) ? t : void 0;
}
function iu(e, t) {
	return e.expiresAtMs !== void 0 && e.expiresAtMs - Date.now() <= t;
}
function au(e) {
	return e ? "expired" : "initial";
}
function ou(e) {
	let t = {};
	return e.apiKey && (t["X-API-Key"] = e.apiKey), e.authorization ? t.Authorization = e.authorization : e.bearerToken && (t.Authorization = `Bearer ${e.bearerToken}`), Object.keys(t).length > 0 ? t : void 0;
}
function su(e, t, n) {
	return u({
		scope: "metadata",
		status: t,
		keyFingerprint: e.keyFingerprint,
		ageMs: Math.max(0, n.now - e.cachedAtMs),
		...n.ttlMs === void 0 ? {} : { ttlMs: n.ttlMs },
		...n.staleIfErrorMs === void 0 ? {} : { staleIfErrorMs: n.staleIfErrorMs },
		...n.revalidatedAt ? { revalidatedAt: n.revalidatedAt } : {},
		...e.sourceUpdatedAt ? { sourceUpdatedAt: e.sourceUpdatedAt } : {},
		...e.validator ? { validator: e.validator } : {},
		...n.refreshErrorId ? { refreshErrorId: n.refreshErrorId } : {}
	});
}
function cu(e) {
	return e instanceof a ? `http-${e.statusCode}` : e instanceof n ? "timeout" : e instanceof s ? "network" : e instanceof Error && e.name ? e.name : "unknown";
}
function lu(e, t) {
	if (t.size === 0) return e;
	let n = e.indexOf("#"), r = n >= 0 ? e.slice(n) : "", i = n >= 0 ? e.slice(0, n) : e, a = i.indexOf("?"), o = a >= 0 ? i.slice(0, a) : i, s = a >= 0 ? i.slice(a + 1) : "", c = new URLSearchParams(s);
	for (let [e, n] of t.entries()) c.set(e, n);
	let l = c.toString();
	return `${l.length > 0 ? `${o}?${l}` : o}${r}`;
}
var uu = "@connectrpc/connect", du = "@connectrpc/connect-web", fu = "../gen/geospatial/v1/feature_service_pb.js", pu = "./grpc-adapter.js";
function mu(e) {
	return import(
		/* @vite-ignore */
		e
);
}
function hu(e) {
	return typeof e == "object" && !!e || typeof e == "function";
}
async function gu() {
	let [e, t, n] = await Promise.all([
		mu(uu),
		mu(du),
		mu(fu)
	]);
	if (!hu(e) || typeof e.createClient != "function") throw Error("The optional peer \"@connectrpc/connect\" loaded but did not export createClient.");
	if (!hu(t) || typeof t.createGrpcWebTransport != "function") throw Error("The optional peer \"@connectrpc/connect-web\" loaded but did not export createGrpcWebTransport.");
	let r = hu(n) ? n.FeatureService : void 0;
	if (typeof r != "object" || !r) throw Error("The gRPC FeatureService descriptor loaded but was not a service descriptor.");
	return {
		createClient: e.createClient,
		createGrpcWebTransport: t.createGrpcWebTransport,
		FeatureService: r
	};
}
function _u(e) {
	return hu(e) ? typeof e.toProtoQueryRequest == "function" && typeof e.fromProtoQueryResponse == "function" && typeof e.wrapConnectError == "function" && typeof e.streamProtoPages == "function" : !1;
}
function vu(e, t, n, r) {
	if (!e.body) return {
		response: e,
		cancel: n
	};
	let i = e.body.getReader(), a = !1, o, s = () => {
		a || (a = !0, t?.removeEventListener("abort", c), n());
	}, c = () => {
		a || (s(), o.error(r()), i.cancel().catch(() => void 0));
	}, l = new ReadableStream({
		start(e) {
			o = e, t?.addEventListener("abort", c, { once: !0 }), t?.aborted && c();
		},
		async pull(e) {
			if (!a) try {
				let t = await i.read();
				if (a) return;
				t.done ? (s(), e.close()) : e.enqueue(t.value);
			} catch (t) {
				if (a) return;
				s(), e.error(t);
			}
		},
		async cancel(e) {
			s(), await i.cancel(e);
		}
	});
	return {
		response: Zl(new Response(l, {
			status: e.status,
			statusText: e.statusText,
			headers: e.headers
		}), e),
		cancel: c
	};
}
//#endregion
//#region src/map/paged-feature-source.ts
async function yu(e, t, n) {
	if (!Su(e.href, t.sourceBaseUrl, t.documentUrl, t.legacySourceBaseUrl)) throw Error("The paged source is outside the authorized deployment.");
	let r = e.pathname.match(/^(.*)(\/rest\/services\/(?:[^/]+\/)+FeatureServer\/\d+)\/query$/);
	if (!r) throw Error("Expected an authorized FeatureServer query URL.");
	let i = e.origin + r[1], a = r[2], o = t.getAuth(), s = 0, c = !1, l = !1, u = await Fa(new Gl({
		baseUrl: i,
		preferBinary: !1,
		fetchFn: async (u, d) => {
			if (n.throwIfAborted(), o !== t.getAuth()) throw Error("The Studio source authority changed.");
			if (u instanceof Request) throw Error("The SDK source adapter requires an explicit URL.");
			let f = new URL(String(u), i);
			if (f.origin !== e.origin || ![`${r[1]}${a}`, `${r[1]}${a}/query`].includes(f.pathname) || (d?.method ?? "GET").toUpperCase() !== "GET") throw Error("The SDK source adapter requested an unsupported route or method.");
			for (let e = 0; e < 2; e++) {
				let r = await o?.getAccessToken(e === 1 ? { forceRefresh: !0 } : void 0);
				if (n.throwIfAborted(), o !== t.getAuth()) throw Error("The Studio source authority changed.");
				let i = new Headers(d?.headers);
				i.delete("authorization"), i.delete("x-api-key"), r && i.set("Authorization", `Bearer ${r}`);
				let a = await (t.fetchImpl ?? o?.fetchRequest ?? fetch)(f.href, {
					...d,
					headers: i,
					signal: n,
					credentials: "omit",
					redirect: "error",
					cache: "no-store"
				});
				if (a.redirected || o !== t.getAuth()) throw Error("The Studio source authority changed or redirected.");
				let u = a.ok ? await a.clone().json() : void 0;
				if (n.throwIfAborted(), o !== t.getAuth()) throw Error("The Studio source authority changed.");
				if (a.status === 401 || u?.error?.code === 499) {
					if (e === 0 && o) continue;
					throw Error("The Studio session cannot authorize this source query.");
				}
				if (u?.error) throw Error("The source server refused the SDK query.");
				return u && (f.pathname.endsWith("/query") ? (s++, c = u.exceededTransferLimit === !0 || u.exceededTransferLimit === void 0 && u.features?.length === 2e3) : l = !0), a;
			}
			throw Error("The Studio session cannot authorize this source query.");
		}
	}), `${i}${a}`, {
		definitionExpression: e.searchParams.get("where") ?? "1=1",
		outFields: (e.searchParams.get("outFields") ?? "*").split(","),
		outSR: 4326,
		pageSize: 2e3,
		maxPages: 32,
		signal: n
	});
	if (n.throwIfAborted(), o !== t.getAuth()) throw Error("The Studio source authority changed.");
	if (!l) throw Error("Authorized source metadata could not be loaded.");
	return s >= 32 && c && t.onIncomplete?.(`Showing ${u.data.features.length} features at the 32-page preview limit; additional features remain.`), u.data;
}
//#endregion
//#region src/map/authenticated-source-protocol.ts
var bu = /* @__PURE__ */ e({
	fetchAuthenticatedFeatureCollection: () => Cu,
	installAuthenticatedSourceProtocol: () => wu,
	trustedSourceUrl: () => Su
}), xu = 0;
function Su(e, t, n, r) {
	try {
		let i = new URL(e, n);
		if (!/^https?:$/.test(i.protocol) || i.username || i.password || /%(?:2f|5c|25)/i.test(i.pathname)) return;
		let a = [{
			value: t || "/",
			stripApi: !0
		}];
		r !== void 0 && a.push({
			value: r,
			stripApi: !1
		});
		for (let e of a) {
			let t = new URL(e.value, n);
			if (!/^https?:$/.test(t.protocol) || i.origin !== t.origin) continue;
			let r = (e.stripApi ? t.pathname.replace(/\/api\/?$/, "") : t.pathname).replace(/\/$/, "");
			if (!i.pathname.startsWith(`${r}/`)) continue;
			let a = i.pathname.slice(r.length);
			if (/^\/rest\/services\/(?:[^/]+\/)+FeatureServer\/\d+\/query$/.test(a) || /^\/ogc\/features\/collections\/[^/]+\/items$/.test(a)) return i;
		}
	} catch {}
}
async function Cu(e, t, n, r = "Map source") {
	let i = Su(e, t.sourceBaseUrl, t.documentUrl, t.legacySourceBaseUrl);
	if (!i) throw Error(`${r} is outside the trusted Honua feature endpoints.`);
	for (let e = 0; e < 2; e++) {
		n.throwIfAborted();
		let a = t.getAuth(), o = await a?.getAccessToken(e === 1 ? { forceRefresh: !0 } : void 0);
		if (n.throwIfAborted(), a !== t.getAuth()) throw Error(`The Studio session changed during the ${r.toLowerCase()} request.`);
		let s = await (t.fetchImpl ?? a?.fetchRequest ?? fetch)(i.href, {
			method: "GET",
			headers: o ? { Authorization: `Bearer ${o}` } : {},
			signal: n,
			redirect: "error",
			credentials: "omit",
			cache: "no-store"
		});
		if (s.redirected) throw Error(`${r} redirects are not allowed.`);
		let c = s.ok ? await s.json() : void 0;
		if (n.throwIfAborted(), a !== t.getAuth()) throw Error(`The Studio session changed during the ${r.toLowerCase()} request.`);
		let l = c && typeof c == "object" ? c : void 0, u = l?.error;
		if (s.status === 401 || u?.code === 499) {
			if (e === 0 && a) continue;
			throw Error(`The Studio session cannot authorize this ${r.toLowerCase()}.`);
		}
		if (!s.ok) throw Error(`${r} request failed (${s.status}).`);
		if (l?.type !== "FeatureCollection" || !Array.isArray(l.features)) throw Error(`${r} did not return a GeoJSON FeatureCollection.`);
		return l.exceededTransferLimit === !0 && t.onIncomplete?.(`Showing ${l.features.length} returned features; the server reports additional features. This preview is incomplete.`), l;
	}
	throw Error(`${r} authorization failed.`);
}
function wu(e, t) {
	let n = `honua-session-${++xu}`, r = `${n}://`, i = /* @__PURE__ */ new Set(), a = !1;
	return e.addProtocol(n, async (e, n) => {
		try {
			if (a) throw Error("Map source transport is disposed.");
			if (n.signal.throwIfAborted(), !e.url.startsWith(r)) throw Error("Invalid map source protocol.");
			let o = Su(decodeURIComponent(e.url.slice(r.length)), t.sourceBaseUrl, t.documentUrl, t.legacySourceBaseUrl);
			if (!o) throw Error("Map source is outside the trusted Honua feature endpoints.");
			i.add(n);
			let s = t.pagedFeatureServices && /\/FeatureServer\/\d+\/query$/.test(o.pathname) ? await yu(o, t, n.signal) : await Cu(o.href, t, n.signal);
			if (a) throw Error("Map source transport is disposed.");
			return { data: s };
		} catch (e) {
			let r = e instanceof Error ? e : Error(String(e));
			throw !n.signal.aborted && !a && t.onError?.(r), r;
		} finally {
			i.delete(n);
		}
	}), {
		transformRequest: (e, n) => {
			let i = n === "Source" && Su(e, t.sourceBaseUrl, t.documentUrl, t.legacySourceBaseUrl);
			return { url: i ? `${r}${encodeURIComponent(i.href)}` : e };
		},
		dispose() {
			if (!a) {
				a = !0;
				for (let e of i) e.abort();
				i.clear(), e.removeProtocol(n);
			}
		}
	};
}
//#endregion
export { ie as i, wu as n, Su as r, bu as t };
