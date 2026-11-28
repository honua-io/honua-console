---
type: concept
title: "How Console authenticates operators"
description: "How the static Console uses Honua Server sessions, CSRF protection and operator bearers."
resource: "honua://capability/identity.oidc"
tags: [auth, identity, operators]
---
# How Console authenticates operators

Console is a static Blazor WebAssembly SPA mounted at `/console/`. Honua Server
provides authentication and enforces authorization on API requests. The browser
application has no Console authentication server, server-side cookie jar or BFF.
Navigation visibility does not grant permission to read or change server resources.

Configure the public `config.json` as described in
[Static SPA deployment](deployment/STATIC_SPA.md). Use
[Local and staging startup](deployment/LOCAL_AND_STAGING.md) to run the browser
bundle. Never put a password, admin API key, client secret or operator token in
configuration or static files.

## Choose the authentication topology

OIDC is the primary operator sign-in method for production deployments —
Amazon Cognito by default on AWS Launch Stack, or an external OIDC provider
such as Keycloak, Microsoft Entra ID, or Okta. Local `admin` password sign-in
serves local development and emergency break-glass recovery (enabled on AWS
Launch Stack only when updated with `AdminCredentialAccess=BreakGlass`).

| Deployment | Browser credential | Sign-in |
| --- | --- | --- |
| API and Console share an origin | Server session cookie; `X-Honua-CSRF` on mutations | Server-brokered OIDC (primary) or builtin/break-glass password |
| API and Console have separate origins | Short-lived operator bearer held in browser memory; cookies omitted | Builtin password |
| Separate management API | Operator bearer held in browser memory; serving cookie and CSRF token omitted | See the management endpoint section below |

An origin includes scheme, host and port. Route `/console/` to the static bundle
and the API/authentication paths to Honua Server for a same-origin deployment.
A separate API origin requires HTTPS, a matching Console CSP and an explicit
server CORS allowlist for the Console origin. See the deployment guide for the
loopback HTTP rules and permitted configuration values.

## Same-origin password sign-in

Set `auth.mode` to `builtin` and point `apiBaseUrl` at the same origin (normally
`/`). The browser:

1. Gets a CSRF request token from `GET /api/v1/admin/auth/csrf`.
2. Submits the deployment password to `POST /api/v1/admin/auth/session`, with
   the CSRF header. Honua Server establishes its session cookie.
3. Reads `GET /api/v1/admin/auth/session` to obtain the authenticated principal
   and expiry, then obtains a fresh CSRF token for that identity.
4. Sends same-origin cookies on API calls and `X-Honua-CSRF` on mutations.

The browser does not read or copy the server cookie into configuration. Reloading
can restore an unexpired cookie session through the session endpoint; it obtains
a fresh CSRF token before authenticated mutations. A CSRF token obtained before
sign-in must not be reused for the newly authenticated identity.

On AWS Launch Stack deployments, `AdminCredentialAccess=Sealed` (the default)
restricts the generated administrator password credential to loopback inside the
ECS task so operators sign in through OIDC. When the identity provider is
unavailable, updating the stack to `AdminCredentialAccess=BreakGlass` allows
emergency sign-in with the local `admin` break-glass password on the Console
sign-in page (as well as `X-API-Key` API access); rotate the secret and return
to `AdminCredentialAccess=Sealed` after recovery.

## Separate-origin password sign-in

Set `auth.mode` to `builtin` and `apiBaseUrl` to the HTTPS API origin. The browser
submits the password to `POST /api/v1/admin/auth/password-token` and uses the
returned bearer to read the session and call that API. Requests omit cookies.
The API must accept the bearer and permit the Console origin through CORS.

The bearer and its expiry remain in browser memory, never `localStorage` or
`sessionStorage`. Reloading requires sign-in again. Expired or missing credentials
cannot fall back to an admin API key.

## Same-origin OIDC

Set `auth.mode` to `oidc` with the same-origin API. Also configure `auth.authority`
as an absolute HTTPS URL without user information, query or fragment, set a
nonblank `auth.clientId`, and supply a nonempty `auth.scopes` array with no blank
entries. The SPA validates these public settings at startup, before fetching the
server configuration. Keep client secrets on Honua Server.

Console gets provider labels from `GET /api/v1/admin/auth/config`. After refreshing
CSRF, it requests a sign-in
URL from `POST /api/v1/admin/auth/providers/{provider}/authorize-url` and navigates
the browser to that URL. Honua Server owns PKCE, callback correlation, provider
credentials, code exchange and session-cookie issuance.

Route `/admin/auth/callback` to **Honua Server**. Keep the server's
`Oidc:AdminAuthCallbackUri` and the provider's registered redirect URI at
`https://<public-host>/admin/auth/callback`. The server returns to an allowlisted
relative Console path beneath `/console/`. Console then reloads the session and
obtains an identity-bound CSRF token. Keep callback responses uncached and use
`Referrer-Policy: no-referrer`.

Setting `auth.authority`, `clientId` or `scopes` does not enable a separate-origin
OIDC flow in this build. Separate-origin OIDC is unavailable; it needs a supported
public SPA client flow. The shipped same-origin broker keeps provider tokens,
callback codes and correlation state out of SPA storage.

## Separate management endpoint

The optional `managementApiBaseUrl` routes management calls to a trusted manager
for the configured serving API. If omitted, calls stay on the serving API. The
manager must accept the operator bearer and allow the Console origin through CORS.
See [Static SPA deployment](deployment/STATIC_SPA.md) for URL and CSP restrictions.

When the serving API shares Console's origin but the manager has another origin,
builtin password sign-in also requests an operator bearer for the manager. The
serving API continues to use its cookie and CSRF token. Manager requests use the
bearer and omit the serving cookie and CSRF token. Reloading can restore the
serving cookie session, but the operator must sign in again to restore the manager
bearer. Same-origin OIDC does not supply that bearer and cannot use a separate
manager in this build.

The bearer request omits the serving cookie and CSRF token, so the serving API
must enable its operator bearer and list the Console origin as an allowed origin.
If the serving API issues no bearer, sign-in still completes for serving pages,
and management calls report the HTTP status they got instead of asking for
another sign-in. If sign-in fails after the server session starts, Console ends
that session before showing the error.

## Credential and permission boundaries

The browser transport strips caller-supplied API keys, cookies, authorization and
CSRF headers before applying its own session credentials. It restricts requests
to the configured API or management target and rejects redirects. Privileged
clients reject missing or expired sessions; public clients may make anonymous
reads. Honua Server remains responsible for RBAC and audit attribution. Console
does not substitute a shared service key or an actor override for the operator.

For a sign-in or approval failure, check the deployed runtime API origins, the
session response, CSRF refresh after sign-in, bearer expiry and CORS, as applicable.
Record the installed server and Console bundle identities with the live journey.
A build or password/OIDC contract fixture does not prove the installed server
accepts the browser session or permits an approval.

## Retained server-host tests

[Honua.Console.LegacyHost](../tests/Honua.Console.LegacyHost/README.md) is a test-only
fixture excluded from release publication. Its `EdgeForwarded`/`Dev` modes,
`ConsoleOperatorCookie`, `/auth/server/login`, partitioned server-side cookie jars
and `/map-proxy/*` endpoints describe that retained host. They do not configure
the static SPA. On that host, a separate manager keeps its own human session:
serving sign-in continues against the manager when the manager has a browser
identity provider, and a `manager:` proposal approval uses that session cookie
with no bearer and no API key. `HONUA_SERVER_BASE_URL`, `HONUA_ADMIN_API_KEY` and
server-side Console authentication environment variables do not configure the
published browser bundle. Use `config.json` and the Honua Server authentication
endpoints described above.
