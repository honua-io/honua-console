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
server-rendered deployment. The old authentication and proxy code remains in a non-publishable
test fixture while its replacement endpoints are verified. A successful static build alone does
not prove sign-in, Studio, map resources or mutation workflows.

The static-bundle workflow replaces Console container publication. Before rollout, replace the
published-DLL browser harness with static hosting, remove the Console service from deployment
compositions, and validate the
API's session and resource contracts in same-origin and cross-origin browsers. Browser transport
tests and the retained host tests cover boundaries independently; neither substitutes for those
end-to-end journeys.
