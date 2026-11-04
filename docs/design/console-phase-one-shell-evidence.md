# Console phase 1: navigation shell

The first phase 1 increment replaces the rail with light top navigation and
route breadcrumbs. Home, Data, Protocols, Studio, Operate, Access and Settings
link to existing routes. Protocols exposes the catalog page that exists today.
The current breadcrumb opens related actions; empty action containers are absent.
Witness mode and server capability gates constrain both navigation and context
links. The environment target remains available in both product modes.

The approved v2 design supersedes the earlier shell arrangement in the phase 0
planning artifacts. This increment retains the existing Data tree, conversation
lifecycle and Home content; the next phase 1 increments replace those separately.
No route, Studio bundle, credential or approval contract changes here.

## Published artifact and authenticated reads

Local captures use the Release-published Console at source
`3abcd436cd99b8076169dacea23ef9ca847eb953`, bound to the repository's isolated
OIDC Compose testbed. The Console receives no service key. Governed operator
sign-in and publication of an unused dedicated source both passed; independent
catalog, metadata and query reads verified its three polygon features.
An earlier attempt to republish the already registered default table returned
its explicit conflict state. Captures use the existing populated service.

All sixteen route captures have one Console light-DOM H1, fit their 1440 px or
390 px viewport, and have no serious or critical WCAG 2.1 A/AA axe findings.
[Route observations](../images/console-phase-one-shell/route-audits.json).

| Route / state | Desktop | Mobile |
| --- | --- | --- |
| Home, retained setup | [1440 px](../images/console-phase-one-shell/home-1440.png) | [390 px](../images/console-phase-one-shell/home-390.png) |
| Data, selected publication | [1440 px](../images/console-phase-one-shell/data-1440.png) | [390 px](../images/console-phase-one-shell/data-390.png) |
| Service settings | [1440 px](../images/console-phase-one-shell/service-settings-1440.png) | [390 px](../images/console-phase-one-shell/service-settings-390.png) |
| Health | [1440 px](../images/console-phase-one-shell/health-1440.png) | [390 px](../images/console-phase-one-shell/health-390.png) |
| Observability | [1440 px](../images/console-phase-one-shell/observability-1440.png) | [390 px](../images/console-phase-one-shell/observability-390.png) |
| Access, empty roster | [1440 px](../images/console-phase-one-shell/members-1440.png) | [390 px](../images/console-phase-one-shell/members-390.png) |
| Settings | [1440 px](../images/console-phase-one-shell/settings-1440.png) | [390 px](../images/console-phase-one-shell/settings-390.png) |
| Studio, editor mounted | [1440 px](../images/console-phase-one-shell/studio-1440.png) | [390 px](../images/console-phase-one-shell/studio-390.png) |

The testbed has an empty member roster. Home retains its setup state, and the
Studio capture shows a mounted editor rather than a saved-map acceptance claim.
Candidate-image Studio customer-journey verification remains a separate CI gate
on this PR, including its identity, approval and save/reopen assertions.

## Navigation and focus

[Desktop menu](../images/console-phase-one-shell/navigation-menu-1440.png),
[narrow menu](../images/console-phase-one-shell/navigation-menu-390.png),
[desktop context](../images/console-phase-one-shell/context-menu-1440.png) and
[narrow context](../images/console-phase-one-shell/context-menu-390.png) show
native disclosures over the populated Data page. Links use normal browser
navigation semantics; these are disclosure groups, not application menu widgets.

The [interaction observations](../images/console-phase-one-shell/navigation-interactions.json)
verify first-link focus, Escape return, narrow hamburger return and the skip link.
Both normal and synthetic long environment labels fit their viewport.
Six browser event regressions cover startup pointer/focus intent, disappearing
focus targets, handled Escape, dialog Escape and outside activation. All six
fail on the first reviewed controller and pass on the corrected controller.
The integration suite also tests witness boundaries, capability refresh and
revocation, encoded service identities, route labels and denied approval counts.
Unknown or denied approval reads omit the badge; zero is neutral and bounded
results retain their partial-count marker.
