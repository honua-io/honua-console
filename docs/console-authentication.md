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

## Chosen option: A-compatible edge auth with an Option-C forwarding path

The #233 recommendation was **Option C — server-delegated operator auth**: authenticate the operator
against honua-server's own auth, obtain an operator session/bearer, and forward it. At the time #234
shipped, honua-server could **not yet issue a console-consumable operator bearer**:

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

Honua-server #2258 has since shipped `POST /api/v1/admin/auth/bearer`. It mints a short-lived,
forwardable bearer from an authenticated admin session and validates it on the admin/control-plane
request path. The Console request pipeline accepts that server-issued token through the same account
session slot used for a trusted edge-forwarded access token. For approval audit attribution, the
authenticated principal must carry `ClaimTypes.NameIdentifier` or `sub`; honua-server resolves those
claims before any API-key identity fallback. The Console forwards the bearer and never supplies an
actor override header.

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
| **EdgeForwarded** | `EdgeForwarded.Enabled=true` or `Mode=EdgeForwarded` | An ingress / oauth2-proxy authenticates against the customer IdP and injects forwarded-identity headers; `ConsoleEdgeIdentityMiddleware` builds the operator principal per request | Operator's `X-Forwarded-Access-Token` is forwarded as `Authorization: Bearer` (real per-principal RBAC). If the proxy supplies no token, human mutations require server bearer exchange or reauthentication. |
| **Dev** | `Development` environment, or explicit `Mode=Dev` | `/auth/login` signs in a developer cookie | Reads may use the configured admin key; human approval/recovery mutations fail closed without a forwardable bearer. |
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

Family-B clients that construct absolute requests per active profile use the same read decision through
`ConsoleServerHttp.AttachAuthenticationAsync(...)`. Proposal decisions, deploy submit/rollback, and
ops-finding proposals use the stricter `AttachMutationAuthenticationAsync(...)`: interactive mode
requires a forwardable bearer, and a missing, sentinel, or expired bearer returns a clear sign-in state
without sending any request. Honua-server derives the audit actor from bearer claims; the Console does
not send an actor header.

`Honua:Server:CredentialMode` / `HONUA_SERVER_CREDENTIAL_MODE` defaults to `Interactive`. The only
value that enables API-key mutation fallback is the exact `HeadlessService` opt-in. Even then, the key
is usable only when no account session exists and the host supplied an explicit `ServiceApiKey`
environment profile. Interactive profile creation does not offer that account mode, and signing in on
such a profile converts it to `AccountRbac`. A signed-in human with a sentinel or expired bearer still
fails closed, so headless mode cannot silently change that human's audit actor.

### The session sentinel

When an operator is authenticated to the Console but no forwardable honua-server bearer exists yet
(dev login, or an edge proxy that passes no access token), the account session stores a
non-forwardable sentinel token (`profile-session:<id>`). It marks the session "signed in" for
client-side read context but is **never** forwarded to honua-server. Read-only clients may apply their
documented admin-key fallback; human mutations do not. A real operator bearer never carries this prefix.

For a human mutation, the sentinel triggers the configured operator-bearer provider. A successful
exchange replaces it with the short-lived bearer and expiry in the profile-partitioned protected
session store. Exchange denial, an expired bearer, or an unconfigured exchange returns a re-sign-in
message and never falls back to `X-API-Key`.

## Operator bearer exchange and deployment topology

Honua-server ships `POST /api/v1/admin/auth/bearer`. It accepts the server's HttpOnly admin-session
cookie and returns a short-lived bearer carrying the same RBAC claims. Console includes a tested,
internal client for that wire contract and refreshes before expiry through
`ConsoleOperatorBearerProvider`. Browser-host sessions are partitioned by operator and environment in
server memory; the native host uses its platform secret store. Tokens are never written to browser
`localStorage`.

The default exchange registration is intentionally unavailable. The server cookie is scoped to the
honua-server origin and cannot be read or forwarded by a separately deployed Blazor Server host.
Deployments need one of these trust topologies before server-session exchange can be enabled safely:

- serve the bearer exchange through a same-origin Console BFF whose upstream client owns the
  authenticated honua-server session; or
- use a trusted edge that establishes the server session or supplies a forwardable operator access
  token to Console.

Stock Console intentionally does not register a cookie-owning exchange client. A process-wide
`HttpClient`/`CookieContainer` would bind multiple operators to one server session and create an
identity bleed. Trusted-edge forwarded operator bearers work today; server-session exchange stays
fail closed until a per-operator, per-profile BFF owns isolated server sessions. Do not copy the
server cookie into application config, relax it to a script-readable cookie, or add an actor header.
Until that BFF exists, reads remain available under their existing policy while human mutations fail
closed with a re-sign-in message.

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
  map-proxy endpoints use this seam directly (they run in the request scope, #257).
- **IHttpClientFactory server-bound client surface (#254).** The Family-A typed clients no longer build
  a self-contained `HttpClient` per singleton. On the browser Web host they are obtained from
  `ConsoleServerBoundClients` — two `IHttpClientFactory` named clients over a shared, managed connection
  pool (`ConfigurePrimaryHttpMessageHandler` with a bounded `SocketsHttpHandler`):
  - **`honua-server-bound` (privileged).** Handler chain = `ConsoleServerBoundOperatorGuardHandler`
    (outermost) → `HonuaServerBindingHandler` → pooled primary. The guard **fails closed** for an
    unresolved operator (`ConsoleOperatorContextUnresolvedException`) BEFORE any credential/retarget, so
    the `__anonymous__` sentinel can never yield a usable server-bound identity. Every privileged Family-A
    client (share, RBAC, admin operate, temporal, version management, Studio package/lifecycle/generation,
    content publication, collaboration, catalog discovery) funnels through this one chain.
  - **`honua-server-public` (anonymous-capable).** Same binding but NO guard, so the legitimately-anonymous
    `/public` open-data catalog reads (`IConsoleCatalogClient`) and the public OGC `/ogc/styles` list keep
    rendering for anonymous visitors by design (documented admin-key / anonymous fallback), never a sentinel.

  The typed-client registrations are unchanged in shape: they still call `HonuaServerClientFactory.Create`
  / `.CreatePublic`, which delegate to the `IHonuaServerBoundClientFactory` when the host registers one and
  otherwise fall back to the self-contained pooled client (native single-operator host, host-independent
  tests). This end-state is regression-locked by
  `tests/Honua.Console.IntegrationTests/ConsoleServerBoundClientFactoryFailClosedTests.cs`: an unresolved
  operator hard-denies on the privileged client, concurrent circuits stay isolated (each carries its own
  bearer, no bleed), and the public client tolerates anonymous.

## What is deferred (follow-ups)

- **Why the privileged guard reads `IConsoleOperatorContext` and not `IConsoleOperatorScope` directly.**
  `IHttpClientFactory` handler chains are pooled/rotated on their own lifetime and are NOT resolved from
  the consuming circuit/request DI scope, so a constructor-injected scoped `IConsoleOperatorScope` in the
  handler would capture the wrong (handler-rotation) `AuthenticationStateProvider` during interactive
  rendering. The guard therefore uses the ambient-bridged `IConsoleOperatorContext`, which resolves
  correctly in every execution context (HttpContext.User on the request pipeline; the circuit operator
  ambient established by `CircuitOperatorContextHandler` for each inbound activity, #256) and fails closed
  via `RequireOperatorKey`. The one residual fail-open this shares with the legacy seam is a circuit
  activity whose execution context is detached from the inbound-activity ambient; removing it entirely
  requires per-scope client construction (a client + binding handler built from the consuming scope),
  which the `IConsoleOperatorScope`-parameter map-proxy path already models for request-scoped callers.
  The chokepoint remains regression-locked by
  `tests/Honua.Console.IntegrationTests/ConsoleServerBindingFailClosedTests.cs`.

- **Full Option C host topology.** The honua-server endpoint is shipped. A deployment still has to
  implement a same-origin, per-operator/per-profile BFF for server-session exchange. Console
  intentionally does not guess how a cookie scoped to another origin should be propagated and does
  not register a process-wide cookie jar.
- **Built-in OIDC (Option B) in the Console host.** `Microsoft.AspNetCore.Authentication.OpenIdConnect`
  can run the auth-code flow directly against the configured IdP and capture the access token for the
  same forwarding path. Not wired here to avoid adding the package/CI surface in this pass; the
  `Honua:Console:Auth:Mode` switch and the bridge are structured to accept it.
- **Native MAUI host parity (#234, third bullet).** The binding handler is the mechanism, but the
  Family-A clients are still only registered when a startup base URL is configured. Registering them
  unconditionally (relying on the handler to retarget to the active profile) is the remaining step to
  bring the native host — where the URL is known only after connect — to parity. Deferred to keep the
  missing-binding UX guarantees intact in this pass.
- **Other Family-B admin-key fallbacks.** Proposal decisions, deploy submit/rollback, and finding
  proposals are bearer-only in interactive mode. Read-only and other legacy Family-B paths retain
  their documented fallback semantics pending separate mutation-by-mutation hardening.
