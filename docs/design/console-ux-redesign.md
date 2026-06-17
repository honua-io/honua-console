# Console UX Redesign — Unified Data → Layer Flow (AI + Manual), BlueSpatial-inspired

Status: design proposal (draft). Filed 2026-06-17.

Audience: console product/design, frontend architecture, founder review.

Decision inputs this proposal honors:

- **console#193** (founder, 2026-06-12) — the console's job is **information**
  (status, timelines, plans/diffs, audit) + **approval** (human-in-the-loop for
  agent operations). *Minimal forms — agents and APIs do the writing; the console
  witnesses and authorizes.* This proposal is therefore **flow-first and
  approval-first**, not forms-first; the manual wizard is the fallback driver of
  the same flow, not the primary surface.
- **Dual-mode style authoring stays** — MapLibre/Maputnik (canonical) **and** Esri
  `drawingInfo`, per `Pages/StudioStyleEditorPage.razor` and ADR-0002. The redesign
  reuses the existing dual-mode editor verbatim; it does not collapse to one encoding.
- **Route/IA is governed** by `docs/console-route-map.md` and the Console Patterns
  Charter. This proposal adds one composite route and refactors existing Operate
  routes into a shared flow; it does not re-invent URL shapes, RBAC gates, or
  empty/missing-binding states.

---

## 1. Current-state audit — where honua-console is clunky

### 1.1 The pain: "resources" and "layers" are separate top-level sections

The Operate secondary nav (`Layout/ConsoleLayout.razor:82–95`) lists **Connections,
Resources, Services, Layers, Versions, Catalogs, …** as *parallel, co-equal*
sections. A user landing in Operate sees four sibling nouns
(Connection / Resource / Service / Layer) with no expressed flow between them. The
mental model — "a connection is a data source; a resource is canonical metadata; a
service exposes it; a layer is the exposed thing" — lives only in prose on each
page, not in the navigation.

Concretely, the four nouns are four destinations:

| Noun | Route | Page | What it shows |
|---|---|---|---|
| Connection | `/operate/connections` | `OperateConnectionsPage.razor` | data sources (DB connections, test-then-pick) |
| Resource | `/operate/resources` | `OperateResourcesPage.razor` | canonical metadata + blast radius (services/layers/maps affected) |
| Service | `/operate/services` | `OperateServicesPage.razor` | published service slots |
| Layer | `/operate/layers` | `OperateLayersPage.razor` | flat layer list; each row links *back* to its "canonical resource" |

The layer list (`OperateLayersPage.razor:30–47`) literally has a **"Canonical
resource"** column whose only job is to re-link the layer to the resource the user
already navigated away from. That column is a tell: the data model already knows a
layer *is* a published resource, but the IA splits them across two pages and asks
the user to reconcile them by clicking back and forth.

### 1.2 The pain: there is no single "add a service and import data" flow

"Add data → make a layer" is today **scattered across five disconnected pages**,
and the user has to know which one to start from:

```
/operate/resources/new          OperateResourceNewPage.razor  — a *chooser* of three links:
   ├─ Table     → /operate/publishing/quick   (OperatePublishLayerPage.razor)
   ├─ File      → /operate/resources/import    (OperateImportFilePage.razor)
   └─ Migration → /operate/import/service      (OperateImportServicePage.razor)

/operate/import/service          OperateImportServicePage.razor — remote Esri/OGC import (its own intake)
/operate/import/esri/*           ImportEsriWizardPage / WebMap / Dashboard / StoryMap — *content* import (separate)
/operate/publishing/quick        OperatePublishLayerPage.razor — table → service layer (its own connection+table form)
/operate/layers/:id/style        OperateLayerStylePage.razor / StudioStyleEditorPage.razor — styling, *after* the fact
```

Each entry point is a self-contained form with its own connection picker, its own
table/file picker, its own "name the service" field, and its own success toast.
`OperateResourceNewPage.razor` is a card menu that *fans out* to three terminal
forms — exactly the "pick your silo first" friction the founder is reacting to.
There is **no continuous journey** from "I have a file/connection" to "I have a
styled, published layer." After import you land on a resource detail page; the
"now publish it as a service layer" step is a separate page you must navigate to;
the "now style it" step is a *third* page. The product knows the steps
(`information-model-summary.md:13–14` lists `Service → Layer/Table`), but the UI
makes the user be the orchestrator.

### 1.3 The pain: AI and manual paths are different surfaces, not one flow

Studio already has a strong **AI conversation + live-preview + approve** surface
(`Components/StudioAiConversation.razor`: left conversation column with structured
clarification cards, right live package preview; the design-fidelity scorecard
rates the Studio Map/Dashboard AI screens 93–94). But that AI posture lives **only
in Studio** (map/dashboard/report/app authoring). The Operate "add data → layer"
journey has **no AI driver at all** — it is pure forms. So the two product
directions the founder wants unified (agent-first per console#193, and a usable
manual fallback) are today split across two areas (Studio = AI authoring; Operate =
manual plumbing) with no shared flow.

### 1.4 What is actually good and reusable (don't rebuild)

- **`Components/EsriImportIntake.razor`** — a generalized "intake bar" with four
  modes (*Paste JSON / Upload file / From URL or item ID / From connected ArcGIS*),
  inline validation, dirty-state + unsaved-changes guard. This is 80% of a unified
  "Add data" intake control already.
- **`Components/StudioAiConversation.razor`** — the AI conversation + clarification
  + live-preview + evidence surface. This *is* the approval surface from console#193.
- **`Pages/StudioStyleEditorPage.razor`** — dual-mode (MapLibre/Esri) styling, server
  converts between encodings. Keep as-is; the new flow just *embeds/links* it.
- **`OperateResourcesPage.razor`'s blast-radius model** (services/layers/maps/share
  links affected) — excellent "information + approval" content; it becomes the
  approval payload, not a separate page.
- Missing-binding / capability-state vocabulary (`OperateCapabilityStateList`) — the
  flow must keep rendering these, never fabricate data.

---

## 2. BlueSpatial-inspired patterns (with specifics)

The old BlueSpatial admin UI (`/Views/Admin/*`, an AngularJS app) is **not** a
perfect template — its rigidity (you *must* pre-create Folder → Service →
Connection before any layer can exist) is itself friction we want to soften. But
several of its interaction patterns are exactly what honua-console is missing.

| # | BlueSpatial pattern | Where (BlueSpatial) | What we borrow |
|---|---|---|---|
| **B1** | **"Add data" and "add layer" are literally one dialog.** `+ Add Layer` opens a switchboard whose first control is just *how* you're sourcing data: `layerTypes = [{Name:"Add layer from file"},{Name:"Add layer from database"}]`. There is no orphan "dataset" you import and then later have to find and turn into a layer. | `create-layer-modal-component.js:13`; `add-layer-from-file-component.*`; `add-layer-from-database-component.*` | Collapse our five entry points into **one "Add data" switchboard** (file / table / remote service / from existing resource / AI). The act of adding data *is* the act of creating the layer. |
| **B2** | **One unified catalog tree is the spine** — Folder ▸ Service ▸ Layer in a single tree with type-specific icons and live **Running/Stopped** status badges; selection drives a context detail panel. | `manage-metadata-component.html`; status at `:26` | Replace the parallel Resources/Services/Layers lists with **one "Data & Layers" tree/list** keyed on the resource, with the service exposure and status shown inline. Kills the "Canonical resource" back-link column. |
| **B3** | **Context-sensitive next action.** The action buttons change with the selected node: a Service offers *Add Layer / Run-Stop / Build Tiles*; a Layer offers *View / View 3D / Style / Delete*. The legal next step is always where you are. | `manage-metadata-component.html:60–119` | In the unified view, the selected resource/layer surfaces its own next steps (Preview / Style / Publish / Share) inline — no hunting across Operate sub-pages. |
| **B4** | **Auto-select the thing you just created and drop into its editor.** After upload/generate, the new node is pushed into the tree and auto-selected, landing you on its tabs. Zero "now go find it." | `add-layer-from-file-component.js:142–162`; `manage-metadata-component.js:364–370` | The unified flow ends on the **new layer's detail with Style/Publish right there** — never a dead-end success toast that drops you back to a list. |
| **B5** | **Built-in onboarding that teaches the model.** An intro tour narrates Folder→Service→Layer, auto-triggered on an empty catalog. | `manage-metadata-component.js:117–133, 313–316` | The **empty state** of the unified view explains "Add data → it becomes a layer → publish/share" in one breath and is the launch point of the flow. |
| **B6** | **Test-then-pick for data sources.** The connection editor authenticates first (`Connect`), then populates the database dropdown from what the server *actually* exposes. | `manage-connection-component.html:40–76` | Keep the connect-then-pick pattern for the "connect a source" intake mode; surface the live table list inline (we already do this in `OperatePublishLayerPage`). |
| **B7** | **Progress over a hub for every long ingest**, with a shared progress bar + a "`"<name>" was generated successfully.`" toast — never an opaque spinner. | `add-layer-from-file-component.js:94–162`; `import-service-modal-component.js:72–118` | Every ingest/publish step in the flow shows determinate progress + a named completion, then advances the wizard rather than dead-ending. |
| **B8** | **The published REST URL is the contract between authoring and preview.** Preview is one click to `/MapPreview/Map.html?layer=<rest-url>` — the *same* URL an Esri/OGC client consumes ("what I preview is what I publish"). | `manage-metadata-component.html:111`; `PreviewPlugin/MapPreview/map.js` | Preview a layer via its **actual served endpoint** (FeatureServer slot), reusing the existing MapPreview target the Esri-import pages already use. |

**Deliberate divergence from BlueSpatial:** B2's tree must *not* force the user to
pre-build Folder→Service→Connection as prerequisites. We keep B1's single-act
ingestion and B4's auto-select-and-edit, but **auto-provision or inline-pick** the
service/folder/connection. And per console#193, the *preferred* driver of the flow
is the agent (BlueSpatial had no AI); the BlueSpatial wizard maps to our **manual
mode**, not the default.

---

## 3. The unified flow (the core fix)

One coherent journey replaces the five-page scatter. Same flow, whether an agent or
a human drives it:

```
            ┌─────────────────────────────────────────────────────────────────────┐
            │                         ADD DATA → LAYER                              │
            │                    (one route, one flow object)                       │
            └─────────────────────────────────────────────────────────────────────┘

  ① ADD DATA                ② RESOURCE              ③ LAYER / SERVICE        ④ STYLE          ⑤ PUBLISH
  intake bar (B1)           canonical metadata      expose as a service       dual-mode (kept) review + approve
  ┌──────────────┐          ┌──────────────┐        layer (new or reuse)      ┌──────────┐    ┌──────────────┐
  │ Upload file  │          │ inferred     │        ┌──────────────┐          │ MapLibre │    │ blast radius │
  │ Connect/pick │──ingest─▶│ fields,      │──bind─▶│ service slot │──draws──▶│   OR     │──▶ │ + visibility │
  │   table      │          │ geometry,    │        │ + layer name │          │ Esri     │    │ + route/embed│
  │ Remote svc   │          │ SRS, issues  │        │ (auto or pick)│         │ drawing  │    │ → APPLY      │
  │ Existing     │          │ (validation) │        └──────────────┘          │  Info    │    └──────────────┘
  │   resource   │          └──────────────┘                                  └──────────┘
  │ Describe(AI) │                                                                              ends on the
  └──────────────┘                                                                              new layer's detail (B4)
```

Key principles:

1. **No orphan "resource" vs "layer" split.** A resource is the canonical metadata
   produced in step ②; a layer is that same resource *exposed* in step ③. They are
   two states of one object in one flow, not two nav sections (kills the
   "Canonical resource" back-link, §1.1; pattern B2).
2. **One intake, five sources** (step ①, generalizing `EsriImportIntake`):
   *Upload file*, *Connect/choose a table on an existing connection*, *Remote
   Esri/OGC service*, ***Use an existing resource*** (the "I already have data,
   make a layer from it" case), and *Describe it (AI)*. Picking "existing resource"
   skips ② and lands directly on ③ — this is the explicit answer to the founder's
   "use an existing resource to create a layer" gap.
3. **Auto-provision the boring plumbing** (B-divergence): the service slot, folder,
   and (where possible) the connection are auto-named/auto-created, with an
   "advanced / change" affordance — never a mandatory pre-step.
4. **End on the artifact, not a toast** (B4): the flow terminates on the new
   layer's detail with Preview / Style / Share inline.
5. **Style is the existing dual-mode editor** (step ④), embedded — MapLibre or Esri
   drawingInfo, server-converted. Not re-specified here.
6. **Publish is an approval, not a form** (step ⑤, console#193): the review card
   shows the blast radius (`OperateResourcesPage` model) + visibility/route/embed
   policy; Apply is the single authorizing action.

### 3.1 Flow object (shared by both drivers)

```
DataToLayerFlow
  Source        : { kind: file|table|remoteService|existingResource|aiPrompt, ref }
  Resource      : { resourceId?, inferredFields[], geometry, srs, validationFindings[] }
  Exposure      : { serviceSlot, layerName, reuseExistingService?: serviceId }
  Style         : { styleId, encoding: MapLibre|Esri }     # reuses StudioStyleEditor
  Publication   : { visibility, route, embedPolicy, blastRadius }
  Driver        : agent | manual
  Step          : addData | resource | layer | style | publish | done
  Approvals[]   : { step, plan, dryRun?, decidedBy, decision }
```

Both drivers mutate the *same* flow object and hit the *same* server contracts
(file import, table publish, service-layer publish, style save). The only
difference is **who fills the fields and who clicks Apply**.

---

## 4. AI mode AND non-AI mode — one flow, two drivers

> Per console#193: agents do the writing; the console witnesses and authorizes. So
> **AI is the default driver**; manual is the always-available fallback. A single
> **driver toggle** in the flow header switches between them *without losing the
> flow object* — you can start with AI, drop to manual to hand-tune step ③, and
> resume.

### 4.1 AI mode (default) — agent proposes, human approves

Reuses `StudioAiConversation.razor` as the left rail; the right rail is the **same
step preview** the manual wizard renders.

- The human states intent ("publish the parcels shapefile I just uploaded as a
  styled layer, public").
- The agent fills the flow object step by step and **pauses at each step boundary**
  with a **plan card** (and, where the server supports it, a **dry-run** result):
  inferred fields/geometry/SRS for ②; proposed service slot + layer name for ③;
  proposed style for ④; visibility + **blast radius** for ⑤.
- The human **Approves / Rejects / Edits** each plan card. Reject or Edit drops that
  one step into manual controls (the wizard's own inputs), then resumes AI.
- Ambiguity → structured **clarification cards** (already supported by
  `StudioAiConversation`: choices with effect labels), not a free-text dead-end.
- Maps cleanly onto the devops console-bridge posture (console#193:
  `create_gitops_proposal` / `get_devops_operation_status`): each approved step can
  emit a proposal/operation the timeline tracks.

### 4.2 Non-AI mode (manual) — guided wizard, human drives

The **same five steps**, rendered as a linear wizard a human fills directly:

- Step ① is the generalized intake bar (`EsriImportIntake` → `AddDataIntake`).
- Steps ②–⑤ are the existing forms, *re-sequenced into one flow* instead of five
  pages: field/geometry review (from `OperateImportFilePage`), service+layer naming
  (from `OperatePublishLayerPage`), the dual-mode style editor, and the publish
  review.
- Progress + determinate ingest feedback per step (B7); auto-advance on success
  (B4); end on the layer detail.
- This is the founder's "forms-light" fallback: minimal, linear, and only as much
  form as the agent would otherwise fill.

### 4.3 Switching drivers

A header control — **`◀ AI ─ Manual ▶`** — present on every step. Switching does
**not** reset the flow object: AI-filled fields become editable manual inputs and
vice-versa. This is the literal "one underlying flow, two drivers" requirement, and
it's also the natural Edit affordance for an AI plan card (§4.1).

---

## 5. Information architecture + wireframes

### 5.1 Proposed IA change (Operate secondary nav)

```
BEFORE (Layout/ConsoleLayout.razor:82–95)        AFTER
  Connections                                       Data & Layers      ← unified (resources+layers+services)
  Resources        ─┐                                 ├─ + Add data    ← launches the flow (route below)
  Services          ├─ four parallel nouns            └─ Connections   ← sources stay a sub-section (B6)
  Layers           ─┘                               Publishing
  Versions                                          Versions
  Catalogs                                          Catalogs
  Settings / Access / Temporal / Observability / Metrics  (unchanged)
```

`Connections` stays (a data *source* is legitimately its own concept, B6) but moves
*under* Data & Layers as a sub-section rather than a co-equal top noun. `Resources`,
`Services`, and `Layers` collapse into the single **Data & Layers** surface.

### 5.2 Route map delta (additive; governed by `docs/console-route-map.md`)

```
NEW   /operate/data                         Unified Data & Layers view (resource+layer+service, one list)
NEW   /operate/data/new                     The Add data → layer flow  (?driver=ai|manual, ?step=add|resource|layer|style|publish)
KEEP  /operate/data/:id                     Resource/layer detail (merges /operate/resources/:id + /operate/layers/:id)
REDIRECT /operate/resources  → /operate/data
REDIRECT /operate/layers     → /operate/data
REDIRECT /operate/services   → /operate/data?view=services
REDIRECT /operate/resources/new, /operate/resources/import, /operate/publishing/quick, /operate/import/service
         → /operate/data/new?source=<file|table|remoteService>   (the five entry points fold into one)
KEEP  /operate/connections*                 (data-source management; now reached under Data & Layers)
KEEP  /studio/styles/:id                    (dual-mode style editor — embedded by step ④, unchanged)
```

`/operate/import/esri/*` (content import: web maps/dashboards/storymaps) stays
distinct — it is *content* migration, not the data→layer flow (route map §106 calls
out this distinction explicitly). The flow may *link* to it as an alternate source.

### 5.3 Wireframe — Unified "Data & Layers" view (replaces 3 list pages)

```
┌ Operate / Data & Layers ───────────────────────────────────────────────────┐
│  Data & Layers                                            [ + Add data ▾ ]  │  ← ▾ = file/table/remote/AI
│  Every dataset and the layer/service it's published as. One object, one row.│
│                                                                             │
│  ⌕ filter…            [ All ][ Published ][ Draft ][ Needs review ]         │
│ ┌─────────────────────────────────────────────────────────────────────────┐│
│ │ ● parcels            file · 12,403 feat · polygon   ▶ Running            ││ ● = status badge (B2)
│ │   resource: rsc_8f… → service: city/parcels-fs → layer #1   public      ││   resource→service→layer
│ │   [ Preview ] [ Style ] [ Share ] [ ⋯ ]                                  ││   inline next actions (B3)
│ ├─────────────────────────────────────────────────────────────────────────┤│
│ │ ○ zoning             table · postgis: city_db.zoning   ⏹ Draft          ││ ○ = not yet exposed
│ │   resource: rsc_2a…    (not published)        [ Publish as layer → ]    ││   the missing-link CTA
│ ├─────────────────────────────────────────────────────────────────────────┤│
│ │ ● traffic-sensors    remote · arcgis…/Traffic   ▶ Running    org-only   ││
│ │   resource: rsc_d3… → service: live/traffic-fs → layer #0               ││
│ └─────────────────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────────────────┘
```

The "Canonical resource" back-link column (§1.1) is gone — the resource, its
service, and its layer are **one row**. The "not published" row carries the exact
CTA that was previously a separate page (`/operate/publishing/quick`).

### 5.4 Wireframe — the Add-data → layer flow (manual driver)

```
┌ Operate / Add data                                      ◀ AI ─[ Manual ]▶ ┐  ← driver toggle (§4.3)
│  ●─────────●─────────○─────────○─────────○                                │
│  Add data  Resource  Layer     Style     Publish                          │  ← step rail (1 flow, §3)
│ ┌─────────────────────────────────────────────────────────────────────────┐│
│ │  ① Add data                                                             ││
│ │  [ Upload file ][ Connect a table ][ Remote service ][ Existing resource ]│ ← intake bar (B1, EsriImportIntake)
│ │  ┌───────────────────────────────────────────────────────────────────┐ ││
│ │  │ Drop a .geojson/.shp.zip/.gpkg…  or  ⌕ choose existing resource… │ ││  "existing resource" → skips to ③
│ │  └───────────────────────────────────────────────────────────────────┘ ││
│ │                                          [ Cancel ]   [ Continue → ]    ││
│ └─────────────────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────────────────┘
        … step ② shows inferred fields/geometry/SRS + validation findings …
        … step ③ shows service slot + layer name (auto-filled, "change" to edit) …
        … step ④ embeds StudioStyleEditor (MapLibre | Esri) …
        … step ⑤ is the publish review (below) …  → ends on /operate/data/:id (B4)
```

### 5.5 Wireframe — the AI driver + approval surface (default)

```
┌ Operate / Add data                                      ◀[ AI ]─ Manual ▶ ┐
│  ●─────────●─────────●─────────○─────────○                                │
│  Add data  Resource  Layer     Style     Publish                          │
│ ┌───────────── Conversation (StudioAiConversation) ──┬─ Plan / Preview ──┐│
│ │ You:  publish the parcels file as a public layer   │ ③ LAYER — proposed ││
│ │ Honua: inferred 14 fields, polygon, EPSG:2926.     │  service: city/    ││  agent's plan card
│ │        Proposing service `city/parcels-fs`,        │    parcels-fs (new)││  for the current step
│ │        layer "Parcels".                            │  layer:   Parcels  ││
│ │  ┌ Clarification ─────────────────────────────┐    │  dry-run: ✓ slot   ││
│ │  │ Geometry precision?  [ keep ] [ snap 1m ]  │    │    free, schema ok ││
│ │  └────────────────────────────────────────────┘    │                    ││
│ │  …                                                 │  [ Reject ] [ Edit ││  Edit → drops to manual
│ │ ┌ refine… ───────────────────────────┐ [ Send ]   │  → ][ Approve step ]││  controls for THIS step
│ └─┴────────────────────────────────────┴────────────┴────────────────────┘│
│  Each approved step may emit a devops proposal/operation (console#193).     │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 5.6 Wireframe — Publish review (step ⑤, both drivers; the approval payload)

```
┌ ⑤ Publish — review & approve ───────────────────────────────────────────────┐
│  Layer "Parcels"  →  service city/parcels-fs (new)                          │
│  Visibility:  ( ) Private   ( ) Org   (•) Public        Embed: [ allow ▾ ]  │
│  Route:       /city/parcels-fs/FeatureServer/1                              │
│ ┌ Blast radius (OperateResourcesPage model) ─────────────────────────────┐ │
│ │  Services 1   Layers 1   Saved maps 0   Share links 0   Generated apps 0│ │  ← info, console#193
│ └─────────────────────────────────────────────────────────────────────────┘│
│                                            [ Back ]      [ Apply & publish ]│  ← single authorizing action
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 6. Mapping to honua-console + phased plan

### 6.1 Reuse / refactor / add

| Action | Component / page | Notes |
|---|---|---|
| **Reuse** | `Components/StudioAiConversation.razor` | The AI driver's left rail + approval cards. Already generalized. |
| **Reuse** | `Pages/StudioStyleEditorPage.razor` (+ `StyleReferencePicker`) | Step ④, embedded. Dual-mode preserved. |
| **Reuse** | `Components/OperateCapabilityStateList`, missing-binding surfaces | Flow keeps neutral/missing-binding states; never fabricates. |
| **Generalize** | `Components/EsriImportIntake.razor` → `AddDataIntake.razor` | Add modes: *table*, *existing resource*, *AI prompt* (it already has paste/upload/URL/connected). |
| **Merge** | `OperateResourcesPage` + `OperateLayersPage` + `OperateServicesPage` → `OperateDataPage` (`/operate/data`) | One list, resource→service→layer per row (§5.3). Old routes redirect. |
| **Merge** | `OperateResourceDetailPage` + `OperateLayerDetailPage` → `/operate/data/:id` | One detail object with inline Preview/Style/Share/Publish (B3/B4). |
| **Re-sequence** | `OperateImportFilePage`, `OperatePublishLayerPage`, `OperateImportServicePage` | Their bodies become **steps** of `OperateDataFlowPage` (`/operate/data/new`), not standalone pages. Old routes redirect into `?source=`. |
| **Add** | `Components/Operate/DataToLayerFlow.razor` (host) + `DataToLayerFlowState` (model) | The flow object (§3.1) + step rail + driver toggle. Wraps the reused step bodies. |
| **Add** | `Components/Operate/AddDataIntake.razor` | Step ①. |
| **Update** | `Layout/ConsoleLayout.razor:82–95` + `ConsoleRouteMap` | IA change (§5.1) + the route delta (§5.2). |

No server contract changes: the flow calls the **same** import/publish/style
endpoints these pages already bind (`IConsoleFileImportOperation`,
`IServiceLayerPublishOperation`, `IConsoleServiceImportOperation`,
`IStudioMapStyleCatalogDataSource`). This keeps the change Console-side and
governed by the Patterns Charter.

### 6.2 Phased implementation

**Phase 0 — this doc.** Design approval (founder + console route-map reconcile).

**Phase 1 — highest-impact slice: the unified manual wizard (`/operate/data/new`).**
Build `DataToLayerFlow.razor` + `AddDataIntake.razor` (generalize `EsriImportIntake`),
wire the existing import/publish/style step bodies into the five-step rail, end on
the layer detail (B4). Redirect the five old entry points into `?source=`. *This one
slice delivers the founder's "add a service and import data" and "use an existing
resource to create a layer" journeys.* Manual driver only.

**Phase 2 — unify the list + detail.** `OperateDataPage` (`/operate/data`, §5.3) and
`/operate/data/:id`; redirect `/operate/resources`, `/operate/layers`,
`/operate/services`; apply the IA nav change (§5.1). Kills the resources/layers split.

**Phase 3 — AI driver + approval.** Add `StudioAiConversation` as the flow's AI rail,
the per-step plan/dry-run cards, the driver toggle (§4.3), and the
clarification→manual-edit handoff. Wire approved steps to the devops console-bridge
proposal/operation surface (console#193).

**Phase 4 — preview + polish.** One-click layer preview via the served FeatureServer
endpoint (B8, reuse the MapPreview target the Esri-import pages use); empty-state
onboarding that teaches the flow (B5); determinate ingest progress everywhere (B7).

### 6.3 Risks / constraints

- **Route governance:** `/operate/data*` and the redirects must be reconciled into
  `docs/console-route-map.md` (its §1 taxonomy + §5 disposition tables) before
  Phase 2 lands — this proposal is additive but the route map is the source of truth.
- **Dual-mode style must not regress** — step ④ embeds the existing editor; do not
  fork it.
- **No fabricated state** — every step renders missing-binding/capability states
  when the server base URL is unset, exactly as the current pages do.
- **console#193 posture** — keep forms minimal; the manual wizard is the fallback,
  the AI+approval driver is the headline. Don't let Phase 1 ossify into a
  forms-first product.

---

## Appendix — primary sources

- Current console: `Layout/ConsoleLayout.razor`, `Pages/Operate{Resources,Layers,
  Services,ResourceNew,ImportFile,ImportService,PublishLayer}Page.razor`,
  `Components/{EsriImportIntake,StudioAiConversation}.razor`,
  `Pages/StudioStyleEditorPage.razor`, `docs/console-route-map.md`,
  `docs/design-handoff/`.
- BlueSpatial (old UI): `BlueSpatial.Web/Views/Admin/manage-metadata-component.*`,
  `create-layer-modal-component.*`, `add-layer-from-{file,database}-component.*`,
  `import-service-modal-component.*`, `manage-connection-component.*`,
  `Renderer/create-renderer-component.*`; preview `PreviewPlugin/MapPreview/map.js`.
- Founder direction: console#193 (information + approval, forms-light, agent-first).
