# Honua Console · Design Handoff

This folder is the source-of-truth handoff for the Honua Console design work.
It's optimised for LLM consumption (Claude / Codex / others) — every artboard
is a React component, all in JSX, with semantic class names and a shared
field-state vocabulary so an engineer (or an LLM) can extend any surface
without reverse-engineering visual intent.

## How this is structured

The whole console is one big design canvas (`index.html` + JSX files). Each
JSX file is a section worth of surfaces; each exported function in a JSX file
is one artboard. Open `index.html` in a browser to see the full canvas.

Visual primitives:

- `shell.jsx` · TopBar, Sidebar, PageHead, Tabs, Toolbar, Btn, Badge, Inp, Sel, Stepper, Callout
- `field-state.jsx` · FieldRow / FieldGroup / ScopeChip / FieldStateLegend — used everywhere
- `map-preview.jsx` · MapPreview (mode='layer' | 'service') — used in any surface that needs a map
- `wireframe.css` · all styling. Variables at the top.

## Reading order for an LLM

1. `decisions.md` — every model decision, in one place
2. This file (`README.md`) — the artboard index
3. `field-state.jsx` and `shell.jsx` — the shared primitives
4. Pick a section file based on what you need to extend
5. For implementation planning, use the [Design Artifact Work Breakdown Matrix](../../roadmap/DESIGN_ARTIFACT_WORK_BREAKDOWN.md) before filing child work

---

## Sections (in order they appear on the canvas)

### ⓪·a Collaboration & RBAC — `screens-collab-rbac.jsx`
Felt-style multiplayer on Studio Map + hierarchical role model.
- `StudioMapCollab` — map editor with presence avatars, named cursors, drawing markup layer, feature-pinned comment pins, follow-mode pill, live activity sidebar
- `MapCommentThread` — feature-anchored comment thread (drawer); @mentions, reactions, linked-job attachments, resolve
- `RBACOverview` — scope hierarchy diagram + 10-role × 8-permission matrix
- `TeamMembers` — workspace people list + scoped invite drawer (env-scoped, content-scoped, time-limited)

### ① Studio · AI GIS creation surface — `screens-studio.jsx`
Prompt → clarify → package → preview → edit → publish for spatial content.
- `StudioHome` — workflow picker (8 content types) + recent projects
- `StudioMapAI` — AI map flow with embedded clarification radios + package inspector
- `StudioMapEditor` — three-column working editor (layers / preview / paint inspector)
- `StudioMapPublish` — publish review with visibility radios + dependency list

### ①·b Studio · Form + Workflow — `screens-studio-form-workflow.jsx`
- `StudioFormAI` — hydrant inspection form; resource binding, offline rules, EXIF strip
- `StudioFormBuilder` — field list / tablet preview / field inspector (Identity/Validation/Privacy/Storage)
- `StudioWorkflowAI` — DAG preview with parallel publish branches + failure edge
- `StudioWorkflowEditor` — step palette / canvas DAG / step inspector

### ①·c Studio · Report, App, Query, Analysis — `screens-studio-rest.jsx`
- `StudioReportEditor` — outline / long-form page / embedded item inspector with **version pinning** (live vs pinned-to-v4)
- `StudioAppEditor` — page tree / responsive canvas / component-binding inspector
- `StudioQueryBuilder` — visual predicate builder + generated SQL + parameterized preview
- `StudioAnalysisEditor` — pipeline DAG + step inspector; publishable as GP service

### ②  Catalog · unified content surface — `screens-catalog-console.jsx`
Every content type in one browse surface.
- `CatalogList` — 13 content-type strip + unified table
- `ContentItemDetail` — Overview / Versions / Lineage / Bindings / Publication / Permissions / Activity tabs (anchored on a Map)
- `ContentItemUsage` — "where used" lineage view for a data resource (impact-preview before edits)

### ③  Operate · Event viewer + Investigations — `screens-event-viewer.jsx`
Absorbs old Activity + Alerts into one timeline.
- `EventViewerList` — 13-dimension filter builder + type strip + dense table mixing all event types
- `EventDetailDrawer` — selected event with AI DevOps advisory + related objects + raw evidence + lifecycle
- `Investigation` — pinned events + thread notes + AI suggested next steps

### ④  Operate · GitOps Release — `screens-gitops-temporal-sync.jsx`
- `GitOpsRelease` — semantic diff (with breaking change highlight) + env matrix + compatibility preflight + data-script coverage + apply strategy
- `GitOpsCITimeline` — applied release with 14-step check timeline; rollback within 14d

### ⑤  Operate · Temporal viewer — `screens-gitops-temporal-sync.jsx`
- `TemporalViewer` — as-of timeline scrubber (edits + release ticks) + side-by-side as-of vs now with change summary

### ⑥  Operate · Sync conflicts — `screens-gitops-temporal-sync.jsx`
- `SyncConflictsList` — replicas with conflicted rows + auto-resolution rules
- `SyncConflictReview` — 3-way merge (base / client / server) per row + AI advisory

### ⑦  Share · outside-world surface — `screens-share.jsx`
- `ShareHome` — KPI strip + unified shared-items table
- `ShareLinkConfig` — per-item: public link + embed snippet + token links + traffic
- `ShareOpenDataPage` — opt-in landing page editor + live page preview (DCAT + JSON-LD)
- `ShareExports` — scheduled exports (S3 / SFTP / webhook / audit snapshots)

### ⑧  Native Console host — `screens-native-aidevops.jsx`
- `NativeHostFirstRun` — full native chrome; mTLS, cert pinning radio, connection probe
- `NativeHostProfiles` — 5-profile manager; cert-changed warning blocks connect

### ⑨  AI DevOps · advisory home — `screens-native-aidevops.jsx`
- `AIDevopsConsole` — KPI strip + 4 open briefs with touches chip strips
- `AIDevopsBrief` — single brief with evidence table + 3 numbered suggested actions + counterfactual + reasoning trace link

### ⓪  Environments & Fleet — `screens-environments.jsx`
- `EnvironmentsList` — overview of dev/staging/prod + drift table
- `EnvironmentDetail` — prod with Fleet sub-tab (8 tasks, 1 OOMKilled)
- `DeployPromote` — older promote-wizard (now superseded by GitOps Release)
- `AlertsList` — three-scope unified feed (env / runtime / definition)

### Original admin surface
(These are the earlier screens, before the Console reframe — still valid, mostly for the Operate area:)

- `screens-overview.jsx` — IA map + Dashboard A/B
- `screens-connections.jsx` — Connections list + Postgres detail + add-connection wizard. **Important**: remote services (Esri/OGC/WMS) are NOT connections — they're imports (see `screens-wizards.jsx · WizImportEsri` which is the **migration** flow).
- `screens-resources.jsx` — Data Resources list + 9 resource detail tabs (Overview / Source / Fields / Metadata / Publish / Access / Validation / Presentation / Advanced)
- `screens-wizards.jsx` — Create resource from table / file / remote-service-migration
- `screens-services.jsx` — Services & Layers list + Explorer (folders → services → layers tree with right-click context menus) + Service detail
- `screens-publish-flow.jsx` — Author-resource-first publish wizard (advanced power-case)
- `screens-quick-publish.jsx` — Quick publish 3-step wizard (common case: Service → Layer → Review)
- `screens-activity.jsx` — Older Activity + Validation centre (superseded by Event Viewer)
- `screens-settings-states.jsx` — Settings (Auth/CORS/License/Catalog endpoints) + Resource sub-tabs + states gallery
- `screens-fields-hifi.jsx` — Field-state vocabulary anchor screens
- `screens-styling.jsx` + `screens-styling-more.jsx` — MapLibre canonical, OGC API Styles endpoint, per-slot Esri Renderer override, WMS SLD override, version history, resync confirm
- `screens-catalogs.jsx` — Catalogs section (Esri / OGC Records / OData / STAC / DCAT)

---

## Conventions any extender should follow

1. **Every shared component goes through `Object.assign(window, {...})`.** Each `<script type="text/babel">` gets its own scope; components used across files must be window-exported.
2. **Field state vocabulary is the visual language**: ✏️ input / 🔍 discovered / 🧮 calculated / ⚙️ system / 🔒 admin. Use `FieldRow state="..."` for any operator-facing field.
3. **Scope chips communicate blast radius**: `<ScopeChip scope="resource|publication|service|server" count="..."/>` on every group of editable fields.
4. **`MapPreview`** in `mode="layer"` for one selected layer, `mode="service"` for the whole service composite with layer-toggle overlay.
5. **Names matter**: services have "Slot" semantics (Layer slot = publication record); data lives on Data Resources; catalog entries auto-mirror Esri + OGC API publications.
6. **Tone**: every governed operation says so loudly. Audit + version on publish. Advisory-only AI. Operator approval required for any AI action.

## How to add a new artboard

1. Add the component to an existing or new `screens-*.jsx`
2. `Object.assign(window, { ComponentName });` at the bottom
3. `<script type="text/babel" src="screens-*.jsx">` in `index.html` head (if new file)
4. Add `<DCArtboard id="..." label="..." width={W|WIDE} height={H}><ComponentName /></DCArtboard>` inside a `<DCSection>` in `index.html`

`W` is 1280 (a standard browser-shape artboard), `WIDE` is 1480 (used for wide tables / dashboards / split editors), `H` is 800 / `TALL` is 900.

## How to open this

`index.html` in any modern browser. Babel transpiles JSX in-browser; expect a ~10s warmup for the full deck.

For a print/PDF dump, see `index-print.html` (auto-fires the browser print dialog after artboards render).
