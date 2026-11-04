# Console redesign: phase 0–1 journey and state acceptance

Baseline: `origin/trunk` at `0ea1f168757e12f167d9220ca41022d928c642fc`.
This is a required-evidence matrix, not a passing test report. Pair it with
[routes](console-redesign-route-compatibility.md),
[identities](console-redesign-identities.md) and
[endpoint ownership](console-redesign-capabilities.md).
Only phases 0 and 1 are specified. Ship each visual increment with screenshots
and regression evidence from that head; do not certify new source against an old
Console image.

## Journey matrix

| Journey | Phase 0 acceptance | Phase 1 acceptance | Existing test / evidence owner |
|---|---|---|---|
| Sign-in / Home / setup | Browser HTML sign-in, API JSON negotiation, safe return URL and first-run links retained; one H1 | Connected Home leads with real approvals/health/findings/recent visits; no dead search, empty Actions slot or fabricated metrics; no-profile and stale-profile recovery remain | `SignInRequiredResponseTests`, `ConsoleHomePageTests`, `ConsoleHomePageRenderTests`, `OpsSummaryStripTests` |
| Connections / import | Populated legacy connection and table views readable and scrollable by keyboard; no service/source identity change | Same current entry points, source selection, file inputs, task revisions, upload receipts and retries survive drawer navigation | Existing publishing/import tests, `agent-workspace-turn.test.mjs`, live publish journey; do not edit files owned by an open publishing PR |
| Publish / browse / view | Data tree uses valid list/tree structure and reachable controls; actual served `LayerMapPreview` retained; ambiguous layer IDs remain explicit | Current routes, viewer/editor boundaries, service-wide protocol/access review, partial failure and separate catalog capability retained | `OperateLayerLookupTests`, `LayerPreviewPaneRenderTests`, `ShellRouteUniquenessTests`, catalog/share tests, live publish and customer journey |
| Operations / approval / evidence | Read failures and neutral states retain meaning; embedded states do not add H1s | Approvals reachable from Home/rail; immutable source+proposal/operation target, separate approver, audit reason and credential boundary unchanged | Approval/deploy tests, `operator/credential-boundary.spec.ts`, customer journey approval/readback |
| Studio / Processes | CSS scoped away from vendored controls; real painted canvas remains usable; lifecycle error distinct from auth expiry | Drawer opens on Studio entry, manual close respected for that visit; Processes distinct; file inputs, pending replies, cancel, progress, saved map/version reopening and publication survive; no bundle/API change | `studio-assets.test.mjs`, `studio-conversation.test.mjs`; `workspace/shared-workspace.spec.ts`, `workspace-live`; real `studio-customer-journey.live.spec.ts` |
| Settings / access / environments | Populated settings/member tables scroll without clipping or inaccessible scroll regions; permission denial distinct from sign-in expiry | Existing destinations and grants; operator/environment switch clears local cache and stale replies; witness/product-mode boundaries retained | `OperateAccessPageRenderTests`, environment tests, `ManifestBackedConsoleCapabilityManifestTests`, credential-boundary tests |

Test directories: [Shell tests](../../tests/Honua.Console.Shell.Tests/),
[integration/render tests](../../tests/Honua.Console.IntegrationTests/),
[Node tests](../../scripts/__tests__/),
[Playwright suites](../../e2e/playwright/).
MAUI/Native.Core is not a current test owner.

## State and heading matrix

| State / trigger | Page-level ownership | Embedded/card ownership | Required behavior in both phases |
|---|---|---|---|
| Loading | Page identity H1 remains | H2/H3 under page H1 | Bounded reads; shell/navigation usable; no forever spinner |
| Allowed but empty | Page H1 and truthful empty copy/action | Appropriate section heading | Empty requires a successful read; zero cannot replace denial or partial failure |
| Missing binding | Whole-page wrapper supplies H1 when body absent | Section heading when page already exists | Connect/choose-environment action; no mock/seed fallback; first-run setup retained |
| Forbidden | Page identity or generic protected-page H1 | Section heading; do not mount forbidden children | Valid viewer lacks permission: request access; do not prescribe sign-in for every denial |
| Expired authentication | Page H1 retained | Auth recovery within current section | Offer safe sign-in/return only for expired or missing auth; preserve pending task identity |
| Unsupported / absent capability | Capability wrapper supplies H1 if entire page | Explicit embedded heading level | Preserve capability gate and actions; no hidden child loads; retain neutral status vocabulary |
| Missing / ambiguous object | MissingItemView retains page H1 | Section-specific heading under existing H1 | Unknown ID does not choose another record; integer collisions offer canonical choices |
| Error / timeout / unreadable response | Page H1 plus safe recovery | Card-specific error and Retry | Structured safe detail/correlation ID; retain last successful timestamp; never arbitrary upstream messages |
| Partial / stale / disconnected | Page identity unchanged | Source-scoped annotations | Reachable sources still render; missing source named; no false complete count/healthy/Live claim |
| Busy / applying / cancelled | Page remains reachable | Local progress/live region | Cancel stops a turn, not an already started server job; preserve readback and no rollback promise |

Keep `ConsoleStateView`, `EmptyState`, `MissingItemView`, capability gates and
`OperateSectionStatusPanel` as compatibility wrappers while sharing presentation.
Heading level must be explicit at composition sites: never change every state to
H3 or make every card H1. Preserve wrapper action slots, diagnostics and gates.
One H1 must exist in populated, whole-page denied/missing/unsupported, unbound and
404 cases so `FocusOnNavigate Selector="h1"` continues to orient the reader.

## Visual, keyboard and lifecycle evidence

Phase 0 uses a consistently light theme, including system-dark browsers. Do not
ship a dark toggle until legacy and Studio routes pass equivalent coverage.
Replace conflicting root token definitions with one owner and compatibility
aliases; migrating literal component colours is deliberate. Scope selectors away
from the embedded/vendored editors. Do not use stylesheet length as a gate.

Compute WCAG sRGB contrast for actual rendered foreground/background pairs in
normal, hover, selected and focus states. Normal text requires at least 4.5:1;
large text and non-text controls require 3:1 where applicable. Record disabled
states as disabled, not a substitute passing text sample. White on the proposed
`#159583`, `#34b29d` and `#ef6a63` fails normal text: mockup comments are not evidence.
Audit Add data, rail, links, badges, danger buttons and focus rings with computed
styles. Check forced colours and reduced motion. Run axe serious/critical checks
on area routes plus Data & Layers and representative populated legacy routes.

Each visual PR attaches inspected desktop (1440×900) and mobile (390px)
screenshots for populated Data & Layers, service settings, health/observability,
access/settings, connected Home and Studio as affected, plus first-run and a
whole-page gated state. Use actual bound data for populated-route evidence;
fixture and live screenshots must be labelled separately. Show no clipped
controls; focusable labelled scroll regions work with keyboard. Keep skip link,
page focus and notification host. Ordinary linked navigation is acceptable; a
tree widget must implement its full keyboard contract rather than decorative ARIA.

Phase 1 desktop drawer is nonmodal; mobile is modal with background inert,
contained focus, Escape and focus restoration. Apply the Studio-entry exception and within-Studio
route-change rules from the identity table. Test open/close, route changes,
Studio entry/exit and manual close, hydration/enhanced navigation, resize, reload,
Back/Forward, account/environment change, delayed response, pending reply, selected
file, in-flight turn/cancel, external handoff, progress and pinned approval review.
One live host/controller owns state; duplicate listeners or replaced file inputs
fail acceptance. Follow the exact local-state rules in the identity table.

## Deliberate test migration and candidate gate

| Existing contract | Phase 0 treatment | Phase 1 deliberate update |
|---|---|---|
| `specs/workspace-layout.spec.ts` desktop details-open / checkbox / mobile nonoverlap | Preserve until drawer lands | Assert closed outside Studio, open on Studio entry, named hamburger, modal mobile focus/inert behavior and usable canvas |
| `workspace/shared-workspace.spec.ts`, `specs/workspace-execution-status.spec.ts` set `.open` | Preserve workspace behavior | Drive real drawer control/adapter; retain all task, approval and execution assertions |
| `scripts/__tests__/agent-workspace-turn.test.mjs` parses literal details host | Retain host until replacement | Test single host/controller lifecycle and real hooks, not a weakened no-op fixture |
| Shell state/capability and Home render tests | Add page/embedded heading and no-child-load coverage | Update Home/navigation expectations deliberately; preserve source, freshness, identity and first-run assertions |
| Studio lifecycle, credentials, publication, saved maps | Run from first shared CSS/shell change | Preserve all current lifecycle and credential tests; no skip or weaker persistence/approval assertions |
| `live/specs/studio-customer-journey.live.spec.ts` | Required for first shared-shell PR | Required on candidate image: real OIDC, GeoJSON/CSV import, access/protocol readback, painted features, bearer recovery, styles/labels, save/reopen, independent viewer, separate human approval, signed-out audience |

Record baseline inputs with every implementation PR: source head, Node lockfiles,
server image digest resolved by `scripts/resolve-live-server-image.mjs`, and
`scripts/studio-release.lock.json` (v0.1.1, Studio source
`733b094741b350a31c28e24988352e5bbec30346`, SDK
`5765b427f421dca9c42832ae450fa1c5aad882c2`). No fixture upgrades are implied.

The existing [live-journey workflow](../../.github/workflows/console-live-journey.yml)
runs “Studio customer journey on real images” when PR paths match its filters,
including Web, Shell, Contracts and Dockerfile changes. Its
`studio-customer` job builds the checked-out PR Console image, records identity,
and exercises the real journey without mocks/skips. Require that check on visual
PRs and verify its consumed head/image, not just its check label. Source changes
cannot use honua-release's `e2e/`-only descendant-suite exception against an old
pinned Console image. No candidate retagging or release-pin changes are authorized.
If the pinned Studio bundle or a missing server contract blocks this evidence,
record `NEEDS-MAIN` with the exact failure and stop rather than alter Studio.
