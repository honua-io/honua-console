# Console phase 0 review captures

These captures show the local published Console with **no server binding**.
They cover first-run setup, the Data & Layers missing-binding state with Add data
focused, and the whole-page unsupported-capability state. Desktop is 1440 × 900;
mobile is 390 × 900. Each route has one H1 and the document fits its viewport.
The shell and navigation retain their existing structure.

| Route | Desktop | Mobile |
| --- | --- | --- |
| First run | [1440 px](../images/console-phase-zero/first-run-1440.png) | [390 px](../images/console-phase-zero/first-run-390.png) |
| Data & Layers, unbound | [1440 px](../images/console-phase-zero/data-unbound-1440.png) | [390 px](../images/console-phase-zero/data-unbound-390.png) |
| Temporal, unavailable | [1440 px](../images/console-phase-zero/capability-unavailable-1440.png) | [390 px](../images/console-phase-zero/capability-unavailable-390.png) |

The browser accessibility suite checks actual rendered Add data text/background
contrast in normal and hover states, resolved semantic token pairs, system-dark
consistency, forced-color outlines and reduced motion. Shared state tests cover
page/embedded headings, denial without mounting children, labelled focusable table
regions and native keyboard controls in the existing publication disclosure list.

**Acceptance remains incomplete:** these are not populated legacy-route captures.
The available local servers have data but no configured human identity provider;
interactive reads correctly require an operator bearer. Populated Data & Layers,
service settings, health/observability, access/settings and the candidate-image
Studio customer journey still need a supported authenticated deployment. These
images and local tests do not establish release qualification.
