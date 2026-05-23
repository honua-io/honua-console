# Operate Embed Contract

Status: filed 2026-05-23.

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).

Implementing ticket: [honua-console#6](https://github.com/honua-io/honua-console/issues/6).

Disposition table: [Legacy Admin Route Disposition](../migration/legacy-admin-route-disposition.md).

## Scope

This document specifies the transitional contract Honua Console implements at `/operate/*` so that legacy `honua-server-admin` (Blazor WebAssembly) workflows can be reached from the unified Console shell while native React Operate views are built. It binds:

- Console route shape under `/operate/*`.
- The same-origin iframe embed at `/operate/legacy/*`.
- Redirect behavior for duplicate-builder routes.
- Auth / RBAC posture.
- Error and empty-state surfaces.
- The degraded posture before single-artifact deploy lands.

`honua-console#2` (scaffold) and `honua-console#3` (IA/RBAC) implement this contract. `honua-server-admin#96` and `honua-devops#55` provide the same-origin precondition.

## Route Shape

Console exposes three kinds of routes under `/operate/*`:

| Pattern | Component | Purpose |
| --- | --- | --- |
| `/operate` and `/operate/<native-section>` | `OperateLanding` and native React views | Operate landing and any native React replacements that have shipped. |
| `/operate/legacy/*` | `OperateLegacyEmbed` | Single iframe host for any legacy Admin path in the embed allowlist (every `EMBED` row in the disposition table plus the duplicate-builder "Legacy reference target (embed)" entries). |
| `/operate/moved-to-studio/<legacy-segment>` | `MovedToStudioLanding` | Fallback target for `REDIRECT-TO-STUDIO` rows before the Studio port lands. |

The embed mount preserves legacy paths verbatim: a Blazor `@page "/operator/data-connections"` resolves under base href `/operate/legacy/` to `/operate/legacy/operator/data-connections`. The Console paths in the [disposition table](../migration/legacy-admin-route-disposition.md) reflect this verbatim form.

In addition, the router registers explicit redirect rules at the bare legacy paths themselves (`/operator/app-builder`, `/operator/spec`, `/operator/sql`, `/operator/annotations`) so bookmarks and external links resolve correctly. Redirect targets are determined by the disposition table. The redirect lives at the bare legacy path; the verbatim `/operate/legacy/operator/<duplicate>` location is the reference target reachable via `MovedToStudioLanding` and is NOT redirected back to Studio (otherwise `MovedToStudioLanding`'s "Open the legacy reference" would bounce).

## Legacy Link Resolution

The base-href mount alone is not sufficient: the legacy Blazor app's `Shared/NavMenu.razor`, several `NavigateTo(...)` call sites (for example, `Pages/Admin/CreateConnectionPage.razor`, `Pages/Admin/ConnectionDetailPage.razor`, `Pages/Admin/ServiceSettingsPage.razor`, `Pages/Admin/LayerPreviewPage.razor`, `Pages/Admin/LayerConfigurationPage.razor`, `Pages/Operator/DataConnections/Detail.razor`, `Pages/Operator/DataConnections/Create.razor`), and several `<a href="/...">` strings still use root-absolute paths (`/services`, `/layers`, `/admin/connection-registry`, `/operator/data-connections`, ...). Root-absolute URIs bypass the base href, so in-frame navigation would escape `/operate/legacy/` and load bare paths at the top of the origin.

The contract resolves this with two complementary rules:

- **In-frame navigation (`honua-server-admin#96` deliverable).** The legacy bundle MUST rewrite root-absolute internal navigation to base-relative form so in-frame nav resolves under the base href. Concretely: every `<NavLink Href="/X">` / `<a href="/X">` / `NavigationManager.NavigateTo("/X")` that targets another legacy page is rewritten to the base-relative form (`Href="X"` / `NavigateTo("X")`). External links (auth bounce, docs, support) MAY remain root-absolute. After this rewrite, an in-frame click on "Services" stays under `/operate/legacy/services` instead of escaping to `/services`.
- **Top-level bare-path passthrough (Console router).** For bookmarks and external links that still hit bare legacy paths at the top of the origin, the Console router registers a passthrough redirect: any top-level navigation to a bare legacy path that matches the embed allowlist (every `EMBED` row plus the duplicate-builder reference targets) `Navigate`s to `/operate/legacy/<that-path>`, preserving query string. Bare paths in the `REDIRECT-TO-STUDIO` set keep their existing Studio redirect (the duplicate-builder rule above). Bare paths not in either set fall through to Console `NotFound`.

The passthrough only applies to top-level navigation, not to iframe-internal navigation; it cannot intercept the iframe's own `window.location` because the iframe is its own browsing context. The in-frame rewrite in `honua-server-admin#96` is the load-bearing fix.

## Embed Modes

`OperateLegacyEmbed` runs in one of two modes determined at runtime by a Console-side capability check (`isLegacyMountedSameOrigin()`):

- `mode: same-origin` — the production target. The legacy Blazor bundle is served from the same origin as Console under `/operate/legacy/<legacy-path>`. The embed renders an iframe whose `src` is `/operate/legacy/<legacy-path>` with `sandbox="allow-same-origin allow-scripts allow-forms allow-popups allow-downloads"` and `referrerpolicy="same-origin"`. Cookies (session) are sent because the iframe is same-origin. `allow-downloads` is required because several embedded workflows trigger client-side downloads via `link.download = filename; link.click()` (the JS interop helpers in `honua-server-admin/src/Honua.Admin/wwwroot/js/print-service-interop.js`, `usage-analytics-interop.js`, `spatial-sql-interop.js`, `annotation-workspace-interop.js`, and `layer-style-interop.js`); the legacy `LayerStylePage`'s nested sandbox already includes this token for the same reason.
- `mode: link-out` — the degraded transitional posture used before `honua-devops#55` lands. The embed renders the Console `EmptyState` with copy explaining that the legacy surface is not yet co-hosted, plus a single explicit "Open legacy admin in a new tab" affordance pointing at the configured legacy origin. The link-out target is gated by `canSeeOperate`.

The mode check is centralized; Operate consumers do not branch on it. The contract is: `OperateLegacyEmbed` renders the right surface for the current deploy.

## Iframe Host Behavior

`OperateLegacyEmbed` is responsible for:

- Rendering an iframe whose `title` is the human-readable legacy section name from the disposition table.
- Sizing the iframe to fill the Console main panel without scroll-wrapping (`width: 100%`, height equals viewport minus shell chrome).
- A `postMessage` channel for path-sync: the legacy app may emit `{ type: 'honua.legacy.path', path: '<new-path>' }` to update the Console URL via `history.replaceState`, so deep links survive navigation inside the iframe. The channel is one-way (legacy → Console); Console does not push paths into the iframe.
- A `postMessage` channel for theme/locale broadcast: Console may emit `{ type: 'honua.console.theme', theme: 'light' | 'dark' }` and `{ type: 'honua.console.locale', locale: '<bcp47>' }` on mount and on subsequent changes. The legacy app may ignore these; behavior is best-effort.
- Treating the iframe `load` event as sufficient readiness evidence. The `honua.legacy.ready` handshake (see [Postmessage Channel](#postmessage-channel)) is OPTIONAL: if it arrives, Console MAY use it to dismiss any "loading" affordance sooner, but its absence is not an error. A watchdog fires only if the iframe never emits `load` within 30s (true load timeout) or emits an `error` event or a `honua.legacy.error` message; in those cases Console renders the `EmptyState` with retry and "Open in new tab" fallback.
- An "Open in fullscreen" affordance that hides Console chrome for operators who need uninterrupted Admin. Fullscreen state is local; it does not change the URL.

`OperateLegacyEmbed` is the only component allowed to render an iframe under `/operate/*`. No other surface embeds legacy content directly.

## Postmessage Channel

Both directions use `window.postMessage` with `targetOrigin` set to the same origin. Messages are JSON objects with a discriminator on `type`. Console rejects messages whose `event.origin` does not match the current origin.

Defined messages (initial set). All legacy → Console messages are OPTIONAL refinements: Console MUST function correctly when the legacy app emits none of them (the iframe `load` event is the load signal of record; see [Iframe Host Behavior](#iframe-host-behavior)). The contract specifies the message shape so that `honua-server-admin#96` MAY add emission incrementally as a refinement, and so any future Console code that consumes them rejects unknown shapes.

```
// legacy -> Console (all optional)
{ type: 'honua.legacy.path', path: string }            // URL bar sync via history.replaceState
{ type: 'honua.legacy.ready' }                          // optional refinement; allows Console to dismiss its loading affordance sooner than the load event
{ type: 'honua.legacy.error', reason: string }          // surface Console EmptyState

// Console -> legacy (best-effort; legacy app may ignore)
{ type: 'honua.console.theme', theme: 'light' | 'dark' }
{ type: 'honua.console.locale', locale: string }       // BCP-47
```

The channel is intentionally minimal. Anything richer (cross-app commands, shared state) is out of scope and is a sign that the workflow should be a native React port, not an embed.

## Redirect Behavior

The router registers redirect rules for each `REDIRECT-TO-STUDIO` row:

- If the Studio target route exists (Studio port has shipped) → `Navigate` to the Studio path, preserving query string.
- Else → `Navigate` to `/operate/moved-to-studio/<legacy-segment>`, which renders `MovedToStudioLanding`.

`MovedToStudioLanding` shows:

- A short explanation that the legacy path moved into Studio.
- A link to the Studio target (greyed out if not yet shipped, with the disposition row's replacement ticket inline).
- A single explicit "Open the legacy reference" affordance that opens the disposition row's "Legacy reference target (embed)" path (for example, `/operate/legacy/operator/app-builder`) in the same tab via `OperateLegacyEmbed`. This affordance is gated by `canSeeOperate` and is the only way to reach the duplicate-builder legacy paths from inside Console. The reference-target paths are part of the embed allowlist (see [Route Shape](#route-shape) and the disposition table's "Legacy reference targets" note), so `OperateLegacyEmbed` accepts them without rendering `NotFound`.

Duplicate-builder paths do not appear in Operate navigation. The redirect rule is the only place they are reachable from a typed URL or external link.

## Auth And RBAC

All `/operate/*` routes are wrapped by a Console `ProtectedRoute` that requires the `canSeeOperate` predicate.

`canSeeOperate` is composed from the existing `canSeeOperatorLinks` (scopes `operator` / `admin`) sourced from the portal pattern and ported into Console by `honua-console#3`. This ticket defines the predicate name and seam; #3 implements it.

- Unauthenticated → redirect to `/auth/signin` with `returnTo` preserved.
- Authenticated but lacks Operate scope → render Console `Forbidden`. URL is preserved so a refresh after permission change resolves naturally.
- Authenticated with Operate scope → render the matched route.

The embed contract assumes same-origin + same-session-cookie. The iframe does not handle its own OIDC bounce. If `honua-server-admin#96` has not landed its OIDC swap, the embed runs in `link-out` mode (see above), where the new tab handles the legacy auth bounce.

## Error And Empty-State Surfaces

The Operate area must render exactly these surfaces in these conditions, using the Console primitives `honua-console#2`/`#3` establish (`NotFound`, `Forbidden`, `EmptyState`):

| Condition | Surface | Notes |
| --- | --- | --- |
| Path under `/operate/legacy/<x>` does not match the embed allowlist (every `EMBED` row plus the duplicate-builder "Legacy reference target (embed)" entries in the disposition table) | `NotFound` | URL preserved so disposition-table fixes are reachable by refresh. |
| Authenticated user lacks Operate scope | `Forbidden` | Single shared component; URL preserved. |
| Same-origin precondition unmet (`link-out` mode) | `EmptyState` with explanation and "Open in new tab" | Treated as a normal state, not an error. |
| Iframe `error` event, `honua.legacy.error` message, or no `load` event within 30s | `EmptyState` with retry and "Open in new tab" fallback | Retry recreates the iframe with a cache-busting query parameter. Missing `honua.legacy.ready` is NOT an error condition (the message is an optional refinement; see Postmessage Channel). |
| Legacy surface reports unsupported service metadata or unsupported package binding | Owned by legacy inside the iframe; Console does not handle | Mentioned for completeness; aligns with the consistent-error-surface project constraint at the page level, not the embed level. |

## Performance Posture

- The Operate area is lazy-loaded (`React.lazy`). The Blazor WASM bundle is not fetched until the user navigates to `/operate/legacy/*`. Console startup, catalog, viewer, Studio generation, share, and embed paths are unaffected.
- `OperateLegacyEmbed` mounts a single iframe at most. Navigating between two legacy paths reuses the iframe and updates `src` rather than remounting where possible, to avoid a full Blazor cold start per click.
- `MovedToStudioLanding` is static and lazy.

## Telemetry And Smoke

The smoke test specified in the disposition document is the canonical evidence for this contract. The contract also requires that, when behavior here changes, the publish, catalog, viewer, Studio generation, share, and embed smoke chains continue to pass — per the project's telemetry constraint.

## Out Of Scope

- A Blazor↔React micro-frontend or web-component integration. The embed is an iframe; deeper integration is explicitly not pursued during the transition.
- A cross-origin embed with credentialed requests. Rejected for cookie / CSP / OIDC bounce reasons.
- Server-side reverse-proxying of the legacy app with no iframe (`/operate/legacy/*` IS the Blazor page). Considered as a fallback in [Open Questions of the disposition doc](../migration/legacy-admin-route-disposition.md#open-questions); not the default.

## Consumer Tickets

This contract is the input for the following Console tickets:

- `honua-console#2` — implements `OperateLanding`, `OperateLegacyEmbed`, `OperateRedirect`, `MovedToStudioLanding`, the `canSeeOperate` seam, and the Operate area's lazy loading.
- `honua-console#3` — defines Operate navigation, RBAC predicates, and shared `NotFound` / `Forbidden` / `EmptyState` primitives.
- `honua-console#5` — replaces `REDIRECT-TO-STUDIO` redirect targets when each Studio port ships.
- `honua-console#9` — extends the cross-surface smoke with the Operate scenarios listed in the disposition doc.

This contract is the input for the following cross-repo tickets:

- `honua-server-admin#96` — adjusts the legacy bundle's base href, frame-ancestors policy, and CSP so it can be embedded under `/operate/legacy/` same-origin, AND rewrites root-absolute internal navigation (`NavMenu.razor`, `NavigateTo("/...")` call sites, in-page `<a href="/...">`) to base-relative form so in-frame nav stays under `/operate/legacy/` (see [Legacy Link Resolution](#legacy-link-resolution)). The `honua.legacy.*` postMessage emission (path-sync, ready, error) is NOT required for #96 to land; it is an optional refinement that the legacy app MAY add later, and Console MUST function with no legacy-side messages (iframe `load` event is the load signal of record).
- `honua-devops#55` — builds the single deployable artifact that serves Console and the legacy bundle from one origin.
