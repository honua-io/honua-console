---
type: reference
title: "Embedding contract"
description: "What another application may rely on when embedding Console surfaces, and what is explicitly not part of that contract."
resource: "https://github.com/honua-io/honua-console/blob/trunk/src/Honua.Console.Shell/Pages/EmbedMapPage.razor"
tags: [embed, integration, contract]
---
# Embedding contract

Console has one surface that another site may frame: the public map embed at
`/embed/maps/{mapId}` under the Console base path (for example
`https://example.org/console/embed/maps/<map id>`). No other Console page is
meant to be framed by another origin.

## What you may rely on

**Route.** `/embed/maps/{mapId}` stays at that path. It is one of the frozen
URLs in the [route map](../console-route-map.md) because embeds are already
inlined in customer pages, and it answers with the Console page (HTTP 200)
rather than a redirect.

**Anonymous viewers.** The route needs no sign-in. Console reads the map through
the server catalog and renders it only when the item is a map, the server grants
the embed action on it, and its sharing is public. Anything else shows a typed
state instead of the map:

| Situation | State shown |
| --- | --- |
| No such item (404), or the item is not a map | Embedded map not found |
| The server refuses the read (401 or 403) or fails | Embed unavailable |
| The server does not grant the embed action | Embed unavailable |
| The map is not public | Unsupported map binding (token-authorized embeds are not bound yet; see below) |
| A token is passed in the query string | Embed unavailable |

"Embed unavailable" does not say which of its causes applied.

**Query parameters.**

| Parameter | Values | Default |
| --- | --- | --- |
| `chrome` | `0`, `false`, `no`, `off`, `none` hide the title bar; `1`, `true`, `yes`, `on`, `minimal`, `full` show it | shown |
| `legend` | `0`, `false`, `no`, `off` hide it; `1`, `true`, `yes`, `on` show it | shown |
| `zoom` | same values as `legend` | shown |
| `extent` | `W,S,E,N` in WGS84 longitude and latitude | the map's saved extent |

An unrecognised value, a malformed `extent` and unknown parameters fall back
to the default.

**Tokens stay out of the request.** An embed token belongs in the URL fragment
(`#embedToken=<value>`), which the browser never sends to a server. Console
rejects `token` or `embedToken` in the query string.

**Framing and content security.** The host in front of Console must serve
`/console/embed/*` with the `embed` form of the Console Content-Security-Policy, which has no
`frame-ancestors` restriction, so any site may frame it. All other Console paths
must use the `app` form with `frame-ancestors 'self'`; a host that sends no
header leaves every page frameable. Print both forms with
`node scripts/console-csp.mjs --header=app` and `--header=embed`, and check a
deployed origin with `node scripts/console-csp.mjs --verify-origin <origin>`.
[Static SPA deployment](../deployment/STATIC_SPA.md) shows where each form goes.
The `<meta>` policy in `index.html` is a fallback; browsers ignore
`frame-ancestors` in it.

## What is not part of the contract

- **The map is a preview.** The embed renders the map title, extent and layer
  legend as a static schematic. No interactive map runtime is mounted, so the
  zoom buttons are shown disabled.
- **Token-authorized embeds.** Console reads `#embedToken=` but does not resolve
  it yet; non-public maps show the unsupported-binding state.
- **Messages to the host page.** The embed posts no `postMessage` events and
  accepts none.
- **Other Console pages.** Operate, Studio, catalog and settings pages are not
  embeddable, and Console does not frame the retired `honua-server-admin`
  application. The `/operate/legacy/*` embed planned in the
  [legacy route disposition](../migration/legacy-admin-route-disposition.md)
  was never built.

## Source

- `src/Honua.Console.Shell/Pages/EmbedMapPage.razor`: route, states, preview.
- `src/Honua.Console.Contracts/SdkShims.cs` (`EmbedRouteOptions`): query and
  fragment parsing.
- `src/Honua.Console.Shell/Services/HonuaServerConsoleCatalogClient.cs`
  (`AuthorizeEmbedAsync`): the read and authorization rules above.
- `config/console-csp.json`: the `app` and `embed` policy forms.
