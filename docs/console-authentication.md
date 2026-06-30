# Console authentication & server binding (#233 / #234)

This document describes how the Honua Console authenticates operators and how it forwards that
operator identity to honua-server. It implements the decision recorded on honua-console#233.

## Problem

Before this change the Console had **no authentication**: every route was open, the host wired no
`UseAuthentication`/`UseAuthorization`, and `ConsoleRoutes.razor` had no `AuthorizeRouteView`. Every
server call used one shared admin `X-API-Key`, which also defeated honua-server's per-principal RBAC
(#234). The "Family A" clients (catalog/content, share, RBAC, Studio, all Operate mutations,
temporal) were bound once at DI time to the startup `honuaServerBaseUrl`, so switching the active
environment profile silently mis-targeted mutations, and the native MAUI host (no startup URL) froze
every Family-A surface into the "Unsupported" state.

## Chosen option: A (interim) with an Option-C-ready forwarding path

The #233 recommendation was **Option C — server-delegated operator auth**: authenticate the operator
against honua-server's own auth, obtain an operator session/bearer, and forward it. Investigation of
honua-server shows it is **not yet able to issue a console-consumable operator bearer**:

- Interactive operator login is **OIDC, cookie-session bound to honua-server's own admin origin**
  (`/api/v{version}/admin/auth/*` in `AdminAuthEndpoints.cs`, redirect `/admin/auth/callback`). The
  OIDC access token is held server-side; the endpoint returns only a session cookie scoped to the
  server origin — it does not hand a forwardable operator bearer back to a separately-deployed
  Console.
- The only forwardable-bearer issuance is the **ArcGIS-shaped Portal OAuth2 named-user bridge**
  (`PortalOAuthTokenService`, `POST /sharing/rest/oauth2/token`) — an Esri-compatibility surface, not
  a general "console login → operator bearer" contract.

So per the #233 branch ("if the server can't yet issue interactive operator sessions, ship A as an
interim and migrate to C"), the Console authenticates the operator **at its own edge** and forwards
the operator identity/bearer to honua-server. The forwarding plumbing is identical to what full
Option C needs, so migrating later is a configuration change, not a rewrite.

## Authentication model (fail-closed)

`Honua.Console.Web/Auth/ConsoleAuthentication.cs` wires real ASP.NET authentication:

- A cookie authentication scheme (`ConsoleOperatorCookie`).
- An authorization **fallback policy** of `RequireAuthenticatedUser()` — every endpoint requires an
  authenticated operator unless it explicitly opts out with `[AllowAnonymous]` (the auth endpoints,
  `version.json`, static assets, error pages).
- `AddCascadingAuthenticationState()` so `AuthorizeRouteView` in `ConsoleRoutes.razor` resolves the
  operator for the interactive render path (defense-in-depth behind the HTTP gate).

Operators sign in one of three ways, selected by `Honua:Console:Auth:Mode` / config:

| Mode | When | How the operator is established | Server credential |
| --- | --- | --- | --- |
| **EdgeForwarded** | `EdgeForwarded.Enabled=true` or `Mode=EdgeForwarded` | An ingress / oauth2-proxy authenticates against the customer IdP and injects forwarded-identity headers; `ConsoleEdgeIdentityMiddleware` builds the operator principal per request | Operator's `X-Forwarded-Access-Token` forwarded as `Authorization: Bearer` (real per-principal RBAC). If the proxy supplies no token, the admin-key fallback applies. |
| **Dev** | `Development` environment, or explicit `Mode=Dev` | `/auth/login` signs in a developer cookie | Admin-key fallback |
| _unset_ (non-Development) | default | **Fail-closed** — `/auth/login` returns 401; no anonymous access | n/a |

### Trusting the edge

`ConsoleEdgeIdentityMiddleware` only honours the forwarded-identity headers when either:

- `Honua:Console:Auth:EdgeForwarded:SharedSecret` is set and the proxy presents it in
  `X-Honua-Edge-Auth` (so a direct caller that reaches the Console origin cannot spoof an operator); or
- no secret is set — in which case the deployment **must** guarantee the proxy strips
  client-supplied identity headers and is the only network path to the host (a startup warning is
  logged).

## Server binding unification (#234)

`Honua.Console.Shell/Services/HonuaServerBindingHandler.cs` is one `DelegatingHandler` that gives the
Family-A clients the same profile/session-aware, request-time binding the Family-B observability
client already had — without rewriting ~20 typed clients. On every outbound request it:

1. rewrites the request authority to the **active environment profile's** `ServerBaseUri` (fixing the
   "mutations mis-target the startup server" bug); and
2. attaches the active operator's **bearer** as `Authorization: Bearer` and removes any shared
   `X-API-Key` the inner client added, so the request runs as the real operator principal. Only when
   there is no operator bearer does the admin-key fallback remain.

`HonuaServerClientFactory.Create(...)` builds the Family-A `HttpClient`s with this handler; the DI
registrations in `HonuaConsoleShellServiceCollectionExtensions` now use it.

### The session sentinel

When an operator is authenticated to the Console but no forwardable honua-server bearer exists yet
(dev login, or an edge proxy that passes no access token), the account session stores a
non-forwardable sentinel token (`profile-session:<id>`). It marks the session "signed in" for
client-side read context but is **never** forwarded to honua-server — the admin-key fallback applies.
A real operator bearer never carries this prefix.

## Map-proxy

The map-preview BFF endpoints (`/map-proxy/*`) act with honua-server privileges. They require an
authenticated operator and forward the operator's bearer to honua-server when one exists
(`MapProxySupport.ResolveOperatorBearerAsync` + `ApplyUpstreamCredential`), falling back to the shared
admin key only when no operator bearer is available.

As of honua-console#254 the operator gate is **fail-closed by construction**: each endpoint resolves the
request's scoped `IConsoleOperatorScope` and denies (401) when it yields `null`. The code path that
builds the admin-keyed upstream request is unreachable without a non-null `ConsoleOperatorIdentity`, so
"no resolved operator" cannot silently proceed.

## Multi-operator isolation (fail-closed by construction, #254)

The browser host serves MANY operators from one process, so any process-wide operator state would bleed
across operators. Two complementary mechanisms keep operators isolated:

- **Operator-partitioned stores (legacy seam, #233/#252/#253).** The profile/session singletons are
  decorated so every read/write routes to a per-operator backing store selected by
  `IConsoleOperatorContext`. The partition key is resolved from `HttpContext.User` (request pipeline) or,
  on the interactive circuit, from the circuit `AuthenticationStateProvider` stamped onto an ambient by
  `CircuitOperatorContextHandler`. Writes fail closed (`RequireOperatorKey`).
- **Scoped operator accessor (`IConsoleOperatorScope`, #254).** The fail-closed-by-construction
  replacement on the server-bound call path. It is a per-circuit/per-request **scoped** DI service that
  reads its scope's OWN authoritative identity and returns a strongly-typed `ConsoleOperatorIdentity` or
  `null` — there is no shared mutable singleton, no `AsyncLocal` ambient a missed context could leave
  unset, and no `__anonymous__` sentinel that stands in for both "anonymous" and "unresolved". Server-bound
  callers take it explicitly (parameter injection) and treat `null`/`RequireAsync` as a hard deny. The
  map-proxy endpoints are converted to this seam; converting the broad typed-client surface is tracked
  below.

## What is deferred (follow-ups)

- **Convert the typed honua-server clients to the scoped seam (honua-console#254 remainder).** The
  Family-A typed clients are singletons whose `HonuaServerBindingHandler` resolves the operator-scoped
  stores via `IConsoleOperatorContext` (HttpContext + circuit ambient). Because that handler also serves
  the legitimately-anonymous public open-data surfaces (e.g. `IConsoleCatalogClient` on `/public`), it
  cannot blanket-deny; moving it fully onto `IConsoleOperatorScope` requires constructing the clients per
  circuit/request scope (or an equivalent scoped-services accessor) so the operator is injected rather
  than bridged through execution context. No fail-open path is introduced on the converted surface; the
  legacy seam retains its #253 write-guard behaviour unchanged.

  This remainder is intentionally NOT attempted as a single PR because a correct conversion is broad and
  lifecycle-sensitive, and a rushed version would regress the carefully-built isolation seam:

  1. **HttpClient lifecycle.** The typed clients are singletons over a pooled `SocketsHttpHandler`
     (`HonuaServerClientFactory`) precisely so the multi-circuit host shares one connection pool.
     Making them per-circuit/request scoped (the only way to inject `IConsoleOperatorScope` at
     construction with no ambient) means a new client + handler + connection pool per scope unless the
     whole factory is moved onto `IHttpClientFactory` typed clients with a shared primary handler and a
     scoped binding handler — a rewrite of the factory, all ~20 `IConsole*Client` registrations, and
     their `Server*DataSource` consumers.
  2. **The partition seam is regression-locked.** `OperatorScopedAccountSessionStore` /
     `OperatorScopedEnvironmentProfileStore` + `IConsoleOperatorContext` + the
     `CircuitOperatorContextHandler` ambient are covered by `ConsoleAuthHardeningTests` and
     `ConsoleOperatorCircuitPartitionTests` (the #252/#253/#256 isolation guarantees). Any conversion
     must keep those green.
  3. **Residual is circuit-bound.** Even a singleton-accessible scoped accessor shares the one residual
     fail-open it would aim to close — a circuit activity whose execution context is detached from the
     inbound-activity ambient. Only per-scope client construction (operator injected at construction)
     removes that, which is exactly (1).

  Until then, the chokepoint's fail-closed contract is regression-locked by
  `tests/Honua.Console.IntegrationTests/ConsoleServerBindingFailClosedTests.cs`: each operator's outbound
  call carries that operator's own bearer (never another's), an unresolved operator never inherits an
  operator bearer (it falls back to the documented anonymous/admin-key path), and an unresolved operator
  cannot write an operator session.

- **Full Option C — honua-server dependency.** honua-server needs a console-consumable endpoint that,
  after the operator authenticates, returns a forwardable operator bearer (or accepts an OIDC bearer
  on the admin API path and maps it to RBAC). Tracked as a honua-server issue; once available the
  Console swaps the edge access token for that bearer with no architectural change.
- **Built-in OIDC (Option B) in the Console host.** `Microsoft.AspNetCore.Authentication.OpenIdConnect`
  can run the auth-code flow directly against the configured IdP and capture the access token for the
  same forwarding path. Not wired here to avoid adding the package/CI surface in this pass; the
  `Honua:Console:Auth:Mode` switch and the bridge are structured to accept it.
- **Native MAUI host parity (#234, third bullet).** The binding handler is the mechanism, but the
  Family-A clients are still only registered when a startup base URL is configured. Registering them
  unconditionally (relying on the handler to retarget to the active profile) is the remaining step to
  bring the native host — where the URL is known only after connect — to parity. Deferred to keep the
  missing-binding UX guarantees intact in this pass.
- **Family-B (observability/metrics/gitops/support) admin-key fallback.** Those clients already do
  request-time bearer binding; they share the same admin-key fallback semantics and benefit from a
  real operator bearer once Option C lands.
