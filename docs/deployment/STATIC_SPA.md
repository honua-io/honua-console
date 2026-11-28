---
type: guide
title: "Deploy the static Console bundle"
description: "Package and host the standalone browser application beneath /console/."
---
# Static Console bundle

The standalone Console targets `/console/`. Its static files call the configured Honua API;
there is no Console application server in this deployment.

```sh
dotnet publish src/Honua.Console.Web/Honua.Console.Web.csproj -c Release -o artifacts/console-publish
python3 scripts/package-console-static.py artifacts/console-publish/wwwroot/console --version 0.1.0
```

The zip contains the contents of `wwwroot/console` plus `LICENSE`, `NOTICE` and `licenses/`, ready to
extract beneath `/console/`. Keep the license files with every copy you host or redistribute. The companion
JSON records the release SHA-256, file count, source size and compressed bundle size. Keep the
precompressed `.br` and `.gz` files; serve them with the matching `Content-Encoding` when the
request accepts it, and `Vary: Accept-Encoding`. The release zip has no precompressed `config.json`,
because you edit it after extraction. Do not add one: a host that serves `.gz` or `.br` files would send the
stale copy instead of your edit. The raw `dotnet publish` output still has both copies; delete them before you
serve an edited `config.json` from it.

`config.json` contains public deployment settings only:

```json
{
  "apiBaseUrl": "/",
  "managementApiBaseUrl": null,
  "auth": { "mode": "builtin", "authority": null, "clientId": null, "scopes": [] },
  "v4Enabled": true
}
```

The single v4 flag applies to every v4 surface. It is on by default; set `"v4Enabled": false`
to roll back to the earlier Console presentation without redeploying the bundle. Do not put an
admin API key, password, client secret or operator token in this file. Tokens remain in browser memory. Cross-origin API
access requires an operator bearer token and an explicit API CORS allowlist. Same-origin
mutations require the API's session cookie and issued CSRF token.

`managementApiBaseUrl` is optional. Omit it, or set it equal to `apiBaseUrl`, and approval,
deployment and management health calls stay on the serving API. For a separate Compose manager,
set it to that manager's base URL, for example `http://127.0.0.1:18082/` on a local quickstart.
It applies only to the configured serving API. Those calls send the operator bearer and do not
send the serving session cookie or CSRF token when the manager is another origin. The manager
must allow the Console origin and accept that bearer. A credential, query or fragment in either
URL is rejected. A remote manager must be HTTPS. Loopback HTTP is limited to `localhost`
and `127.0.0.1`, which the shipped `connect-src` already allows. IPv6 literals are rejected.
On a same-origin
`apiBaseUrl` (`/`), builtin password sign-in also requests that bearer and keeps it in memory
for the manager only; reload the page and sign in again after a refresh, because the bearer is
not restored from the session cookie. Same-origin OIDC cannot call a separate manager.

## S3 and CloudFront (AWS Launch Stack deployment models)

Console is packaged and deployed as a version-locked static SPA — there is no
Console Docker container. On AWS CloudFormation Launch Stack, two deployment
models are supported:

1. **Per-instance Static SPA (Model 1 — default):** Leave
   `ConsoleBundleVersion` empty. The stack deploys the template's version-locked
   static Console bundle into a private S3 bucket with CloudFront Origin Access
   Control and serves it at `/console/` on the same domain as the Honua Server
   API. Same-origin hosting keeps API session cookies, CSRF protection, and
   server-brokered OIDC sign-in on one origin without CORS.
2. **Multi-Environment Hub (Model 2 — optional):** Organizations managing
   multiple Honua Server instances from one central Console set
   `ConsoleBundleVersion=off` on spoke stacks to install the API without a
   per-instance Console bucket or `/console/*` routes, and configure
   `AllowedBrowserOrigins` where cross-origin browser access is required.

For custom static hosting outside Launch Stack, use a private S3 origin with
CloudFront Origin Access Control. Upload the extracted files under `console/`;
route `/console/*` to that origin and API paths to the Honua Server origin. The
same public domain keeps the API session cookie same-origin. Limit SPA fallback
to Console navigation requests: return `/console/index.html` for extensionless
Console routes, and keep missing API routes and missing assets as errors. Do not
rewrite API errors to HTML.

Use `Cache-Control: no-store` for `config.json` and `index.html`. Cache fingerprinted assets
with `public, max-age=31536000, immutable`; use revalidation for assets without fingerprints.
Configure response headers at the CDN or static host: `X-Content-Type-Options: nosniff` and a
restrictive `Referrer-Policy`.

### Content-Security-Policy

`config/console-csp.json` is the only place the Console policy is defined; each directive records
why it is there. `index.html` carries it as a `<meta http-equiv="Content-Security-Policy">`
fallback, so a bucket served without headers is still covered. Browsers ignore
`frame-ancestors`, `report-uri` and `sandbox` in a meta policy, so the CDN must also send a
header form. There are two, and each needs its own CDN behaviour:

| Path | Header form | Framing |
| --- | --- | --- |
| `/console/embed/*` (more specific, so list it first) | `--header=embed` | Any site may frame public map embeds |
| `/console/*` | `--header` (the `app` form) | `frame-ancestors 'self'`; only the Console frames its own pages |

```sh
node scripts/console-csp.mjs --header                  # app form for /console/*
node scripts/console-csp.mjs --header=embed            # embed form for /console/embed/*
node scripts/console-csp.mjs --write                   # restamp index.html after editing the JSON
node scripts/console-csp.mjs --verify-origin <origin>  # compare a deployment's served headers
```

The Launch Stack delivers these headers through CloudFront response-headers policies. If you host
the bundle behind your own CDN or web server, set the same headers on the same paths. The meta
and header policies both apply and a request must pass each, so do not widen only one.

The API origin is chosen per deployment, so `connect-src` admits `https:` and `wss:`, plus
loopback `http:` and `ws:` on `localhost` and `127.0.0.1` for a local Compose manager.
A separate-origin API must use HTTPS. A plain-HTTP API works from the Console's own origin.
Validate policy changes with the browser journeys before rollout.

## Release status

The static Blazor WebAssembly bundle is the shipping 2026.1-rc.2 Console
artifact; Console container publication has been retired. Legacy server-side
authentication and proxy code remains only in `tests/Honua.Console.LegacyHost`,
a test-only fixture excluded from release publication. A successful static build
alone does not substitute for live end-to-end verification of sign-in, Studio,
map resources, and mutation workflows against the pinned Honua Server image.

The static-bundle workflow builds, packages, and attests the release ZIP. Its Playwright gate serves
the published files at 1280 and 390 pixels. One fixture checks unbound navigation and missing
API responses. A password-contract fixture submits the sign-in form and checks same-origin
cookie issuance with identity-bound CSRF refresh, cross-origin bearer issuance without cookies,
authorized resource rendering and links beneath `/console/`. It also checks that the bearer
is absent from browser storage. This fixture models the API contract; it does not qualify
the actual server implementation. The same-origin OIDC fixture checks provider selection,
server-broker redirects, callback correlation/replay rejection, cookie bootstrap and mounted
return paths. Separate-origin OIDC needs a registered public SPA client and remains unavailable
in this build.

For same-origin OIDC, set `auth.mode` to `oidc`. Provider labels come from
`/api/v1/admin/auth/config`; the server owns PKCE, callback correlation, provider credentials,
code exchange and the session cookie. Keep `Oidc:AdminAuthCallbackUri` and the provider's
registered redirect at `https://<public-host>/admin/auth/callback`. Route that root path to the
API server. After validating the callback, the server redirects to an allowlisted relative
Console return path (default `/console/`). Console reloads the session and obtains a fresh,
identity-bound CSRF token. No callback code, correlation state, provider credentials or tokens
are stored by the SPA. Keep callback responses uncached and use `Referrer-Policy: no-referrer`.
Legacy browser configurations launch the test-only host and preserve their existing assertions.

Before rollout, remove the Console service from deployment compositions and validate the API's
actual sign-in, session and resource contracts in same-origin and cross-origin browsers. The
password-contract fixture and retained host tests do not substitute for those end-to-end journeys.
