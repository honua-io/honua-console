# Sharing and Embed MVP — Smoke Evidence (`honua-console#4`)

This document records the smoke evidence for the sharing/embed slice ported
into Honua Console. It maps each acceptance criterion to concrete tests in
this repo. The Console slice lands behind `FixtureShareClient`; production
wiring requires the recorded server and SDK child tickets below.

Source-repo history is `honua-portal#15`; ongoing ownership is
`honua-console#4`. The cross-surface "publish → catalog → Studio → share /
embed" smoke chain belongs to `honua-console#9`; this file is the per-AC
test map for the share/embed slice ported by `#4`.

## Run

```sh
npm install
npm run typecheck
npm test
```

## Acceptance Criteria → Evidence

| AC | Description | Evidence |
| --- | --- | --- |
| **AC1** | Private, organization, and public-link access behave differently in tests or smoke validation. | `src/share/__tests__/policy.test.ts` → tier-ordering, escalation matrix, blocker-order parity. `src/share/__tests__/client.test.ts` → per-tier `patchAccess` round-trip + 403 forbidden + 409 closureBlocked + network-error. |
| **AC2** | Public embeds work only when map dependencies are shareable by the embed audience. | `src/share/__tests__/policy.test.ts` → `canEmbedAudienceAccess` matrix. `src/embed/__tests__/permissions.test.ts` → public-embed-of-public-deps OK; public-embed-of-private-dep renders the per-layer `unauthorized` cell while the rest of the map still loads. |
| **AC3** | A user can copy an embed snippet and load the map in an iframe-compatible route. | `src/share/__tests__/snippet.test.ts` → snippet shape (iframe, `loading="lazy"`, `allow="fullscreen"`, `referrerpolicy`), default `chrome=minimal&legend=on&zoom=on`, custom chrome variants, fragment-only embed token. `src/embed/__tests__/route.test.ts` → defensive parsing for `chrome`, `legend`, `zoom`, `extent`, fragment token. `src/routes/Maps.test.tsx` and `src/viewer/init.test.ts` → parsed params are threaded into `/embed/maps/:id`, `zoom=off` suppresses navigation controls, query extent overrides persisted extent, invalid extents fall back, and `#embedToken` is not overwritten by viewer hash state. |

## Design AC → Evidence (extends issue ACs)

| Design AC | Evidence |
| --- | --- |
| Tier change PATCH → updates dialog and pill | `client.test.ts` "private → org succeeds for an editor" (and the layer-a path that exercises a 409). |
| Same dialog backs non-map content items | `snippet.test.ts` "/maps vs /catalog" — share-link resolves both surfaces; the dialog itself reuses a single client API. |
| Dependency review surfaces blockers; widening blocked at UI | `policy.test.ts` "dependency-closure block when escalating to public with a private dep" + `client.test.ts` "public escalation with private dep returns 409". |
| Group sharing falls back to `unsupported` when groups API absent | `client.test.ts` "falls back to unsupported when no groups surface". |
| Copy link / copy embed produce the documented strings | `snippet.test.ts` covers the default snippet shape and the URL-fragment token rule. |
| Embed route renders with chrome variants | `route.test.ts` "parses chrome=minimal\|none\|full" + `Maps.test.tsx` route-param threading. |
| `extent` fallback to persisted on garbage / malformed | `route.test.ts` `parseExtent` fallbacks + `resolveEffectiveExtent`; `init.test.ts` verifies the embed mount uses a valid query extent and falls back to the persisted saved-map extent otherwise. |
| Public embed with a private dep → per-layer `unauthorized` cell | `permissions.test.ts` "per-layer unauthorized cell". |
| `embeddable=false` blocks the iframe surface independently of `sharing` | `permissions.test.ts` "public + embeddable:false blocks the iframe surface" + "public-link + embeddable:false". The result reports `rootBlockedBy: "embeddable"` so the embed page can render the `unsupported` empty-state instead of `unauthorized`. |
| Root authorization distinguishes tier denial from embeddable / unsupported | `permissions.test.ts` "tier denial" + "unsupported root" cases assert `rootBlockedBy ∈ {tier, embeddable, unsupported}`. |
| Embed route refuses an expired/invalid token | `auth.test.ts` "expired token surfaces as `unauthorized`" + "invalid token …". |
| Token in URL fragment, not query | `snippet.test.ts` "places the embed token in the URL fragment, not the query" + `route.test.ts` `parseEmbedTokenFragment` + `init.test.ts` token-fragment preservation in embed mode. |
| Token fragment parser never throws on opaque tokens (incl. literal `%`) | `route.test.ts` "handles tokens containing literal '%' without throwing" + "never throws on malformed percent-encoded fragments" + `snippet.test.ts` "round-trips tokens with literal '%' through build → parse". |
| Schema parity with #10 wire shape | `schema-parity.test.ts` confirms `share-access-v1` enum and required fields stay in sync with `SHARING_TIER_ORDER`. |
| Closure walker capped at depth ≤ 5 / ≤ 200 nodes (per #10) | `closure.test.ts` `maxDepth` and `maxNodes` cases. |

## Manual Demo Path

This is the manual smoke a reviewer can walk against the fixture client
once #14 + #9 land on trunk and the React surfaces wire up:

1. Sign in as `user-1`. Open `/maps/map-clean` (a saved map whose only
   dep is the org-tier `service-a`).
2. Click **Share**. Pick `Organization` → save → confirm the pill flips
   to "Org" and the dependency review is empty (the only dep is already
   org-tier).
3. Open `/maps/map-1` (the mixed-deps fixture). Click **Share** and pick
   `Public link` → save. The dialog runs the closure check and surfaces
   every typed dep narrower than public-link as a blocker — `layer-a`
   (org), `layer-b` (private), `service-a` (org), and `service-b`
   (private). `style-1` renders as the `unsupported` cell and never
   blocks.
4. Inline-escalate each blocker to `Public link` (layer-a, layer-b,
   service-a, service-b). Re-issue the map patch → success; the share
   link is copyable.
5. Toggle the **Embeddable** checkbox. Copy the embed snippet. Paste it
   into a fresh HTML page.
6. Open the page; the iframe loads `/embed/maps/map-1` with
   `chrome=minimal&legend=on&zoom=on` and the persisted default extent.
7. Hand-edit `?extent=foo` in the snippet — the embed page falls back to
   the persisted extent without throwing.
8. Open a public embed of `map-public` (which keeps a private layer in
   its closure). The map loads; the private layer renders as the
   per-layer `unauthorized` cell in the legend slot.
9. Open `/embed/maps/map-1#embedToken=expired-token`. The page shows the
   `unauthorized` empty-state — no chrome leak, no localStorage write.

## Cross-repo Coordination

The Console slice is demonstrable end-to-end against fixtures. Production
wiring requires these recorded bounded child tickets:

- **honua-server#1162** — Sharing PATCH semantics + dependency closure
  enforcement. `PATCH /api/v1/console/items/{id}` accepts
  `access.sharing` and `access.embeddable`. Server-side closure check
  returns 409 with the list of dep ids that block. The server reuses
  `evaluateShareEscalation` shape as a parity test fixture.
- **honua-server#1162** — Embed token mint + verify.
  `POST /api/v1/console/items/{id}/embed-tokens` (mint, ttl ≤ 7d,
  audience scope) and `GET /api/v1/console/embed-tokens/{token}`
  (verify). Tokens scoped to one item + closure snapshot. Sets
  `Content-Security-Policy: frame-ancestors *` on the embed route.
- **honua-server#1162** — Group membership read API.
  `GET /api/v1/console/groups?member=me` for the share dialog group
  selector. Until this ships, Console renders the `unsupported` group
  state.
- **honua-sdk-js#225** — Browser-safe sharing + embed client surface.
  `patchAccess`, `mintEmbedToken`, `redeemEmbedToken`, `listMyGroups`.
  Replaces `FixtureShareClient` once the SDK ships.
- **honua-console** (follow-up to `honua-sdk-js#225`) — Swap
  `FixtureShareClient` for the SDK surface. Not on `honua-console#4`.

## Bundle / Performance Posture

- Sharing modules in `src/share/` are pure-TS with no top-level side
  effects, so bundlers tree-shake them out of surfaces that don't open
  the share dialog.
- Embed modules in `src/embed/` parse strings only — no MapLibre import
  in the parser layer. The React embed page (lands once #9 + #13 merge
  to trunk) dynamic-imports the viewer module to keep MapLibre off the
  catalog/list bundles.
- Closure walker is BFS with explicit depth/node caps; the walker is
  O(V + E) over the closure subgraph and is safe to run on every share
  dialog open and every tier change.

## Security Posture

- Embed tokens are read from the URL fragment (`#embedToken=…`) so they
  never reach access logs or referer headers. Snippet builder enforces
  this — there is no API to put a token in the query string.
- Token-mode embed pages do not write to localStorage; the resolved
  posture from `resolveEmbedAuth` is the only place a token is held.
- Embed snippet emits `referrerpolicy="strict-origin-when-cross-origin"`
  to limit referer leakage from embedder pages.
- The embed page owns a `<meta name="robots" content="noindex,nofollow" />`
  tag (added by the React route in a later integration ticket). The
  non-embed `/maps/:id` page is the indexable canonical.

## What This Slice Does NOT Cover

- React UI components (lazy-loaded `ShareDialog`, `EmbedPage`) — those
  land once #9 (portal shell) and #13 (viewer) converge on trunk.
- Network-layer `CatalogClient` — replaced by the SDK child ticket. The
  fixture client mirrors the wire shape so the swap is mechanical.
- Server enforcement and embed-token mint/verify — recorded child
  tickets above.
