# Console phase 0 verification

Phase 0 installs the v2 light tokens, shares state presentation while preserving
page/section headings and capability boundaries, and makes existing table scroll
regions reachable by keyboard. The existing shell, navigation, 107 route
declarations and pinned Studio release retain their owners and behavior.

## Authenticated legacy routes

These are actual server-backed reads from an isolated local testbed using the
repository's OIDC Compose configuration. The Console receives no service key.
The governed operator sign-in and publish/query journey both passed; the published
PostGIS source contains three independently queried features. Counts and names
in the captures are controlled test data, not production data or a mock UI.

All twelve route/viewport captures have one H1, fit their 1440 px or 390 px
viewport, and have no serious or critical WCAG 2.1 A/AA axe findings.
[Audit summary](../images/console-phase-zero/populated/route-audits.json).

| Route / state | Desktop | Mobile |
| --- | --- | --- |
| Data & Layers, publication selected | [1440 px](../images/console-phase-zero/populated/data-1440.png) | [390 px](../images/console-phase-zero/populated/data-390.png) |
| Service settings, populated controls and layers | [1440 px](../images/console-phase-zero/populated/service-settings-1440.png) | [390 px](../images/console-phase-zero/populated/service-settings-390.png) |
| Health, populated checks and latency | [1440 px](../images/console-phase-zero/populated/health-1440.png) | [390 px](../images/console-phase-zero/populated/health-390.png) |
| Observability, populated telemetry and events | [1440 px](../images/console-phase-zero/populated/observability-1440.png) | [390 px](../images/console-phase-zero/populated/observability-390.png) |
| Access, authenticated empty roster | [1440 px](../images/console-phase-zero/populated/members-1440.png) | [390 px](../images/console-phase-zero/populated/members-390.png) |
| Settings, populated server projection | [1440 px](../images/console-phase-zero/populated/settings-1440.png) | [390 px](../images/console-phase-zero/populated/settings-390.png) |

The fresh testbed has no members. Its empty roster and disabled export/invitation
controls are shown honestly; no membership data or mutation capability was added.
Long publication routes, service headings and definition labels wrap within the
content column. Tables retain horizontal scrolling inside their labelled regions.

## Contrast, focus and motion

Actual Add data and selected-publication Open route links compute to 5.473:1 in
both normal and hover states. Keyboard focus is visible on both. The populated
health scroll region moves from 0 to 40 px with ArrowRight while retaining focus;
its 352 px scrollport contains 807 px of content and its outline offset is -2 px.
[Computed observations](../images/console-phase-zero/populated/computed-contrast.json)
and [focused scrollport](../images/console-phase-zero/populated/health-scroll-focus-390.png).

The browser suite checks resolved semantic text/status pairs, secondary-button
and tab boundaries, system-dark consistency, forced-color outlines, computed
shell/collapse-control transition durations under reduced motion, and long
identifiers at 390 px. Normal text requires 4.5:1; controls require 3:1.
Shared-state tests cover heading ownership, capability denial without mounting
children, actions and keyboard disclosure controls. Existing credentials,
approval, workspace, save/reopen and publication assertions remain intact.

## Studio image evidence

The [PR-image customer journey](https://github.com/honua-io/honua-console/actions/runs/41892880093)
passed five tests with zero skips, failures or flakes. Its runtime receipt records
Console merge source `fa62e1fe8b06bd3ea711eb6ce7cae2216ba5fef7` (including the
approved phase 0 source `dd0f51e`) and image ID
`sha256:eb8331b98c667e661a60d4f2b0c93891512ba95de0aafef869f767f50053bef9`.
It exercises real OIDC, imports, painted features, styling/labels, save/reopen,
independent rendering, expiry/recovery and separate approval/publication.
[Save/reopen milestone](../images/console-phase-zero/populated/studio-ci-saved-reopened.png).
This CI reference is separate from the local source-built legacy-route captures.
The final PR checks must pass the image journey again after the reflow changes.
No Studio bundle or release change is part of this work.

## Unbound and gated states

These earlier captures explicitly have no server binding; they supplement the
server-backed evidence and do not establish live acceptance.

| Route | Desktop | Mobile |
| --- | --- | --- |
| First run | [1440 px](../images/console-phase-zero/first-run-1440.png) | [390 px](../images/console-phase-zero/first-run-390.png) |
| Data & Layers, unbound | [1440 px](../images/console-phase-zero/data-unbound-1440.png) | [390 px](../images/console-phase-zero/data-unbound-390.png) |
| Temporal, unavailable | [1440 px](../images/console-phase-zero/capability-unavailable-1440.png) | [390 px](../images/console-phase-zero/capability-unavailable-390.png) |
