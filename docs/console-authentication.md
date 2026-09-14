---
type: concept
title: "How Console authenticates operators"
description: "How the Console authenticates an operator and forwards that identity to honua-server, so an operator knows what is being sent on their behalf."
resource: "honua://capability/identity.oidc"
tags: [auth, identity, operators]
---
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
| **EdgeForwarded** | `EdgeForwarded.Enabled=true` or `Mode=EdgeForwarded` | An ingress / oauth2-proxy authenticates against the customer IdP and injects forwarded-identity headers; `ConsoleEdgeIdentityMiddleware` builds the operator principal per request | Operator's `X-Forwarded-Access-Token` is forwarded as `Authorization: Bearer` (real per-principal RBAC). If the proxy supplies no token, the operator obtains a server bearer through the same-origin BFF; that bearer persists across subsequent identity-only requests (honua-console#306). An edge-supplied token still overrides a stored bearer. |
| **Dev** | `Development` environment, or explicit `Mode=Dev` | `/auth/login` signs in a developer cookie | All interactive reads and mutations require a forwardable operator bearer; dev login alone grants no server access. |
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
   `X-API-Key` the inner client added, so the request runs as the real operator principal.
   The browser host adds `ConsoleOperatorCredentialHandler` immediately before transport. It strips
   every configured key, resolves/refreshes the operator bearer, verifies its recorded server target,
   and returns 401 with a sign-in message before transport when no valid credential is available.

`HonuaServerClientFactory.Create(...)` builds the Family-A `HttpClient`s with this handler; the DI
registrations in `HonuaConsoleShellServiceCollectionExtensions` now use it.

Family-B clients that construct absolute requests per active profile also use this browser transport
boundary, including release/version, observability, health, proposals and diagnostics. Proposal decisions, deploy submit/rollback, and
ops-finding proposals use the stricter `AttachMutationAuthenticationAsync(...)`: interactive mode
requires a forwardable bearer, and a missing, sentinel, or expired bearer returns a clear sign-in state
without sending any request. Honua-server derives the audit actor from bearer claims; the Console does
not send an actor header.

The browser transport never permits a service key, including when `HeadlessService` is configured.
For non-browser service callers, `Honua:Server:CredentialMode` / `HONUA_SERVER_CREDENTIAL_MODE` defaults to `Interactive`. The only
value that enables API-key mutation fallback is the exact `HeadlessService` opt-in. Even then, the key
is usable only when no account session exists and the host supplied an explicit `ServiceApiKey`
environment profile. Interactive profile creation does not offer that account mode, and signing in on
such a profile converts it to `AccountRbac`. A signed-in human with a sentinel or expired bearer still
fails closed, so headless mode cannot silently change that human's audit actor.

### The session sentinel

When an operator is authenticated to the Console but no forwardable honua-server bearer exists yet
(dev login, or an edge proxy that passes no access token), the account session stores a
non-forwardable sentinel token (`profile-session:<id>`). It marks the session "signed in" for
client-side read context but is **never** forwarded to honua-server. Interactive reads and mutations
require exchange or reauthentication; neither may fall back to a shared key. A real operator bearer never carries this prefix.

For an interactive server request, the sentinel triggers the configured operator-bearer provider. A successful
exchange replaces it with the short-lived bearer and expiry in the profile-partitioned protected
session store. Exchange denial, an expired bearer, or an unconfigured exchange returns a re-sign-in
message and never falls back to `X-API-Key`.

`ConsoleEdgeIdentityMiddleware` re-establishes the edge operator identity on **every** request and
re-syncs the session. Absence of `X-Forwarded-Access-Token` means the edge manages the operator's
identity, not their server credentials — so the per-request sync **preserves** a forwardable bearer the
operator obtained out-of-band through the server-session BFF (below), together with its expiry, instead
of overwriting it with the sentinel. The sentinel is written only when no forwardable bearer exists yet.
An edge-supplied `X-Forwarded-Access-Token` remains the edge-owned credential and still takes precedence
over any stored bearer. Downstream, `ConsoleOperatorBearerProvider` continues to enforce expiry and
re-exchange, so a preserved bearer is honoured only until it expires or the operator signs out.

## Operator bearer exchange and deployment topology

Honua-server ships `POST /api/v1/admin/auth/bearer`. It accepts the server's HttpOnly admin-session
cookie and returns a short-lived bearer carrying the same RBAC claims. Console includes a tested,
internal client for that wire contract and refreshes before expiry through
`ConsoleOperatorBearerProvider`. Browser-host sessions are partitioned by operator and environment in
server memory; the native host uses its platform secret store. Tokens are never written to browser
`localStorage`.

The browser host now registers a same-origin BFF by default. It owns a bounded, process-local
`CookieContainer` for each authenticated Console operator + environment profile + server origin.
The cookie jar is never shared by typed clients, never copied into configuration, and never exposed
to browser JavaScript. One-time OAuth state is bound to the same operator/profile partition; a
callback from another operator cannot consume or invalidate the owner's flow. Only the issued bearer
and expiry enter the existing operator/profile session store.

The server cookie remains scoped to the honua-server origin, so the external auth topology must use a
shared public origin:

1. Set honua-server `Public:BaseUrl` / `PUBLIC_BASE_URL` to the external Console origin.
2. Route `/admin/auth/callback` on that origin to Console. Console consumes the query callback;
   its private client calls the server's `/providers/{provider}/token` endpoint with the pending
   cookie held in that operator/profile jar.
3. Keep the server API reachable from Console through the profile's `ServerBaseUri`. The internal
   server URL may differ from the public callback origin.
4. Register the exact shared-origin `/admin/auth/callback` URI with the OIDC provider. Use HTTPS
   outside local development. The Console auth cookie is HttpOnly and Lax so it returns on the
   top-level OIDC GET callback; honua-server's pending/auth cookies remain HttpOnly, Strict, and
   server-side inside the BFF jar.

An operator starts the flow at `/auth/server/login?profileId=...`. If the server exposes more
than one provider, Console renders a provider-selection page. The one-time callback state and the
authenticated Console cookie provide login-CSRF protection; callback state expires after ten minutes
and is consumed once. Sign-out clears only the current operator's Console bearers, pending flows, and
server cookie jars.

Cookie jars are deliberately not durable. A Console restart, eight hours of inactivity, profile
origin change, or server-session expiry drops the upstream session and requires sign-in again. This
keeps server cookies out of storage while preserving bounded refresh during a live operator session.
Deployments may also use the trusted-edge topology:

- use the built-in same-origin Console BFF described above; or
- use a trusted edge that supplies a forwardable operator access
  token to Console; or
- front the Console with a trusted edge that forwards **identity headers only** (no
  `X-Forwarded-Access-Token`) and let each operator obtain their server bearer through the same-origin
  BFF. This combination is supported: the BFF-exchanged bearer persists across subsequent edge-identity
  requests until it expires or the operator signs out, and an edge-supplied access token still overrides
  it when present (honua-console#306).

Do not replace the partitioned store with a process-wide `HttpClient`/`CookieContainer`, copy the
server cookie into application config, relax it to a script-readable cookie, or add an actor header.
When the shared-origin route is absent or the server rejects exchange, human mutations still fail
closed with a re-sign-in state and never fall back to the shared admin key.

## Map-proxy

The map-preview BFF endpoints (`/map-proxy/*`) require an authenticated operator and the same
`ConsoleOperatorCredentialHandler` as privileged typed clients. No configured admin key reaches
transport. Missing, expired, unexchangeable or target-mismatched credentials return 401. The proxy
checks its configured upstream against the active profile before forwarding a bearer; switching or
editing a profile cannot forward that profile's token to another server.

Styles, feature rows and tiles use `Cache-Control: no-store` to prevent cross-operator cache reuse.
Redirects and pooled cookies are disabled on interactive upstream clients. Public catalog/style
clients may send anonymous requests, but strip shared keys as well.

Configured server URLs initialize an isolated profile for each operator in Production and Development.
This creates no session or server privilege; the operator must sign in. Without a configured URL the
host retains its explicit missing-binding state.

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
    rendering for anonymous visitors by design (anonymous requests carry no service key), never a sentinel.

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

- **Full Option C host topology.** The honua-server endpoint and Console's partitioned BFF are shipped.
  Deployment must route the shared-origin callback and configure the server public base URL as
  described above. Missing/misrouted topology fails closed.
- **Built-in OIDC (Option B) in the Console host.** `Microsoft.AspNetCore.Authentication.OpenIdConnect`
  can run the auth-code flow directly against the configured IdP and capture the access token for the
  same forwarding path. Not wired here to avoid adding the package/CI surface in this pass; the
  `Honua:Console:Auth:Mode` switch and the bridge are structured to accept it.
- **Native MAUI host parity (#234, third bullet).** The binding handler is the mechanism, but the
  Family-A clients are still only registered when a startup base URL is configured. Registering them
  unconditionally (relying on the handler to retarget to the active profile) is the remaining step to
  bring the native host — where the URL is known only after connect — to parity. Deferred to keep the
  missing-binding UX guarantees intact in this pass.
- **Native/headless legacy clients.** Their service-credential mode remains separate from the browser
  host. Every browser server client, including Family-B reads, passes the operator-only transport boundary.

Admin realtime connections also require an operator bearer, re-resolve it on reconnect, and never
attach the shared key. Proposal, deployment and health subscriptions are scoped per circuit rather
than shared between operators.


## Focused presentation

`Honua:Console:Mode` / `HONUA_CONSOLE_MODE` accepts `full` (default) or `witness`.
Witness mode limits the primary and Operate navigation to focused inspection, approval and recovery
surfaces. Full mode keeps the broader navigation and labels those entries Preview. Direct navigation
to a broader surface shows the Preview notice in either mode; this is presentation, never a client-side
permission or an Admin API parity claim. Server authorization is identical in both modes.

See [the acceptance evidence matrix](focused-client-2026.1-evidence.md) for what is implemented and
what still requires server dependencies and exact-candidate qualification.
