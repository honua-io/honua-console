# Console Design-Fidelity Scorecard

Audit of each console surface against its `docs/design-handoff/console-canvas/screens-*.jsx`
mockup + `wireframe.css` + `ui-surface-briefs.md` + Console Patterns Charter §9.

**Method:** the wireframe mockups are deliberate sketch artboards (`--ink/--accent/--paper`,
generic `.scr/.bd/.card`). The implementation uses its own `--honua-*` design system + BEM
classes per Charter §9 ("reuse the portal's CSS variable pattern as a *visual reference*"). Fidelity
is scored on **structure, layout regions, named components/affordances, and state vocabulary** —
not literal colors.

_Snapshot: 2026-05-31, against trunk after the parity-remediation + high-severity remediation passes._

## Scorecard

| Surface | Mockup | Score | HIGH | MED | LOW |
|---|---|---|---:|---:|---:|
| Studio Map (AI + editor + publish) | screens-studio.jsx | 94 | 0 | 1 | 1 |
| Studio Dashboard (AI + editor + publish) | screens-studio.jsx | 93 | 0 | 1 | 0 |
| Studio Home (`/studio`) | screens-studio.jsx | 60 → **remediated (#123)** | 0 | 0 | 0 |
| Studio Query | screens-studio-rest.jsx | 88 | 0 | 1 | 0 |
| Studio Analysis | screens-studio-rest.jsx | 85 | 0 | 1 | 0 |
| Studio Report | screens-studio-rest.jsx | 82 | 0 | 1 | 1 |
| Studio App | screens-studio-rest.jsx | 88 | 0 | 1 | 0 |
| Studio Form | screens-studio-form-workflow.jsx | 86 | 0 | 1 | 0 |
| Studio Workflow | screens-studio-form-workflow.jsx | 88 | 0 | 1 | 0 |
| Studio Map Collaboration | screens-collab-rbac.jsx | 0 → **remediated (#124)** | 0 | 0 | 0 |
| Share (home + manage) | screens-share.jsx | 93 | 0 | 1 | 0 |
| Publishing wizards | screens-quick-publish.jsx + screens-publish-flow.jsx | 92 | 0 | 1 | 0 |
| GitOps Releases | screens-gitops-temporal-sync.jsx | 88 | 0 | 2 | 0 |
| Temporal + Sync conflicts | screens-gitops-temporal-sync.jsx | 91 | 0 | 1 | 0 |
| Catalog (content) | screens-catalog-console.jsx | 89 | 0 | 1 | 0 |
| Catalogs (discovery endpoints) | screens-catalogs.jsx | 10 → **remediated (#125)** | 0 | 0 | 0 |
| RBAC Access (roles + members) | screens-collab-rbac.jsx | 95 | 0 | 0 | 1 |
| Operate Overview (`/operate`) | screens-overview.jsx | n/a (IA-diagram artboard, no literal screen) | — | — | — |

## HIGH-severity gaps — all remediated + merged

1. **Studio Home** (was 60) → `Components/Studio/StudioHome.razor` — hero prompt + suggestion chips,
   4-column content-type card grid, recent-projects table. PR #129 (closes #123).
2. **Studio Map collaboration** (was 0) → `Components/StudioMapCollaboration.razor` — presence chrome,
   live-cursor/markup affordances, feature-pinned comment drawer, activity sidebar, Comments/Activity
   tabs. Structural chrome to mockup; live data renders explicit missing-binding until the server
   collaboration API (honua-server#1278) lands. PR #130 (closes #124).
3. **Catalogs discovery endpoints** (was 10) → `Pages/CatalogsListPage.razor` +
   `CatalogsEndpointDetailPage` + `CatalogItemEditorPage` — Esri/OGC/STAC discovery endpoints,
   default-on vs opt-in, feeders, per-endpoint issues, detail drill-down, item editor. Live data
   gated on honua-server#1279 (missing-binding until then). PR #131 (closes #125).

## MED-severity gaps

- **Cross-cutting — field-state vocabulary:** Studio editors render plain inputs instead of the
  `field-state.jsx` FieldRow state pills (`input/discovered/calculated/system/admin`). _In remediation._
- **GitOps Releases (×2):** "Data scripts" should be a dedicated tab with covered/no-rollback badges
  (currently a side-column list); "Git PR preview" should be an inline PR-diff (currently a link).
  _In remediation._
- **Studio Report:** Print-preview / Export-PDF toolbar actions omitted — **server-contract-gated**,
  intentionally deferred.

## Notes

- `#124`/`#125` landed as structural chrome to their mockups with explicit missing-binding states;
  their live data lights up when honua-server#1278/#1279 publish (Charter §11 — no fabricated data).
- Surfaces with hi-fi mockups not individually scored in this pass: environments, connections,
  resources, services, event-viewer, activity, settings-states, styling, native-aidevops. Their
  impl pages exist; deep fidelity scoring is a follow-up.
