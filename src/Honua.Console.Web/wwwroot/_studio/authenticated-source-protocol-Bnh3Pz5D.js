//#region src/map/authenticated-source-protocol.ts
var e = 0;
function t(e, t, n, r) {
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
async function n(e, n, r, i = "Map source") {
	let a = t(e, n.sourceBaseUrl, n.documentUrl, n.legacySourceBaseUrl);
	if (!a) throw Error(`${i} is outside the trusted Honua feature endpoints.`);
	for (let e = 0; e < 2; e++) {
		r.throwIfAborted();
		let t = n.getAuth(), o = await t?.getAccessToken(e === 1 ? { forceRefresh: !0 } : void 0);
		if (r.throwIfAborted(), t !== n.getAuth()) throw Error(`The Studio session changed during the ${i.toLowerCase()} request.`);
		let s = await (n.fetchImpl ?? t?.fetchRequest ?? fetch)(a.href, {
			method: "GET",
			headers: o ? { Authorization: `Bearer ${o}` } : {},
			signal: r,
			redirect: "error",
			credentials: "omit",
			cache: "no-store"
		});
		if (s.redirected) throw Error(`${i} redirects are not allowed.`);
		let c = s.ok ? await s.json() : void 0;
		if (r.throwIfAborted(), t !== n.getAuth()) throw Error(`The Studio session changed during the ${i.toLowerCase()} request.`);
		let l = c && typeof c == "object" ? c : void 0, u = l?.error;
		if (s.status === 401 || u?.code === 499) {
			if (e === 0 && t) continue;
			throw Error(`The Studio session cannot authorize this ${i.toLowerCase()}.`);
		}
		if (!s.ok) throw Error(`${i} request failed (${s.status}).`);
		if (l?.type !== "FeatureCollection" || !Array.isArray(l.features)) throw Error(`${i} did not return a GeoJSON FeatureCollection.`);
		return l;
	}
	throw Error(`${i} authorization failed.`);
}
function r(r, i) {
	let a = `honua-session-${++e}`, o = `${a}://`, s = /* @__PURE__ */ new Set(), c = !1;
	return r.addProtocol(a, async (e, r) => {
		try {
			if (c) throw Error("Map source transport is disposed.");
			if (r.signal.throwIfAborted(), !e.url.startsWith(o)) throw Error("Invalid map source protocol.");
			let a = t(decodeURIComponent(e.url.slice(o.length)), i.sourceBaseUrl, i.documentUrl, i.legacySourceBaseUrl);
			if (!a) throw Error("Map source is outside the trusted Honua feature endpoints.");
			s.add(r);
			let l = await n(a.href, i, r.signal);
			if (c) throw Error("Map source transport is disposed.");
			return { data: l };
		} catch (e) {
			let t = e instanceof Error ? e : Error(String(e));
			throw !r.signal.aborted && !c && i.onError?.(t), t;
		} finally {
			s.delete(r);
		}
	}), {
		transformRequest: (e, n) => {
			let r = n === "Source" && t(e, i.sourceBaseUrl, i.documentUrl, i.legacySourceBaseUrl);
			return { url: r ? `${o}${encodeURIComponent(r.href)}` : e };
		},
		dispose() {
			if (!c) {
				c = !0;
				for (let e of s) e.abort();
				s.clear(), r.removeProtocol(a);
			}
		}
	};
}
//#endregion
export { n as fetchAuthenticatedFeatureCollection, r as installAuthenticatedSourceProtocol, t as trustedSourceUrl };
