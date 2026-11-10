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
  "v4Enabled": false
}
```

The single v4 flag applies to every v4 surface. Do not put an admin API key, password, client
secret or operator token in this file. Tokens remain in browser memory. Cross-origin API
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
Configure response headers at the CDN or static host: `X-Content-Type-Options: nosniff`, a
restrictive `Referrer-Policy`, and a CSP permitting the WASM runtime, the configured API origin,
and the map/Studio resources used by that deployment. Validate CSP with the actual browser
journeys before rollout; do not add unrestricted origins to fix a blocked request.

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

For same-origin OIDC, set `auth.mode` to `oidc` and provide the public authority, client ID and
scopes. Provider labels come from `/api/v1/admin/auth/config`; the server owns PKCE, provider
credentials and token exchange. Keep the server's `Oidc:AdminAuthCallbackUri` and the provider's
registered redirect at `https://<public-host>/admin/auth/callback`; the server validates that root
path. Configure the edge to redirect this URL to `/console/admin/auth/callback`, preserving its
query string, before serving the SPA. `/console/auth/callback` is also accepted by the SPA. Serve callbacks using
the Console navigation fallback and a `Referrer-Policy: no-referrer` response header. Callback
query parameters are removed from browser history before exchange. Only short-lived provider,
state and return-path metadata is stored in session storage; no verifier or provider tokens
reach browser storage.
Legacy browser configurations launch the test-only host and preserve their existing assertions.

Before rollout, remove the Console service from deployment compositions and validate the API's
actual sign-in, session and resource contracts in same-origin and cross-origin browsers. The
password-contract fixture and retained host tests do not substitute for those end-to-end journeys.
