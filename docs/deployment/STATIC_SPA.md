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

The zip contains the contents of `wwwroot/console`, ready to extract beneath `/console/`. The companion
JSON records the release SHA-256, file count, source size and compressed bundle size. Keep the
precompressed `.br` and `.gz` files; serve them with the matching `Content-Encoding` when the
request accepts it, and `Vary: Accept-Encoding`.

`config.json` contains public deployment settings only:

```json
{
  "apiBaseUrl": "/",
  "auth": { "mode": "builtin", "authority": null, "clientId": null, "scopes": [] },
  "v4Enabled": true
}
```

The single v4 flag applies to every v4 surface. It is on by default; set `"v4Enabled": false`
to roll back to the earlier Console presentation without redeploying the bundle. Do not put an
admin API key, password, client secret or operator token in this file. Tokens remain in browser memory. Cross-origin API
access requires an operator bearer token and an explicit API CORS allowlist. Same-origin
mutations require the API's session cookie and issued CSRF token.

## S3 and CloudFront

Use a private S3 origin with CloudFront origin access control. Upload the extracted files under
`console/`; route `/console/*` to that origin and API paths to the Honua Server origin. The
same public domain keeps the API session cookie same-origin. Limit SPA fallback to Console
navigation requests: return `/console/index.html` for extensionless Console routes, and keep
missing API routes and missing assets as errors. Do not rewrite API errors to HTML.

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

The API origin is chosen per deployment, so `connect-src` admits `https:` and `wss:` but not
`http:`. A separate-origin API must use HTTPS; a plain-HTTP API works only from the Console's
own origin. Validate policy changes with the browser journeys before rollout.

## Migration status

The browser host is under integration. The API must provide browser sign-in, session/CSRF
bootstrap, authorized resource endpoints and static fallback before this bundle replaces the
server-rendered deployment. The old authentication and proxy code remains in a test-only
host while its replacement endpoints are verified. It is excluded from release publication;
the legacy customer regression explicitly packages it into a test container. A successful static build alone does
not prove sign-in, Studio, map resources or mutation workflows.

The static-bundle workflow replaces Console container publication. Its Playwright gate serves
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
