# Console UX Redesign — Resource-First Publication, GeoServer-style Treeview, Unified Data → Publish Flow (AI + Manual)

Status: design proposal (draft). Filed 2026-06-17. Revised 2026-06-17 to add the
three founder-specified pillars (see box below).

Audience: console product/design, frontend architecture, founder review.

> **Implementation status (Phases 1+2 landed).** The resource-first publish flow
> (`Components/Operate/DataToPublishFlow.razor` at `/operate/data/new`) and the
> GeoServer-style resource→publications treeview + Layer Preview
> (`Components/Operate/ResourcePublicationsTree.razor` + `LayerPreviewPane.razor` at
> `/operate/data`) are implemented and wired to the real honua-server admin clients
> (`IConsoleFileImportOperation`, `IServiceLayerPublishOperation`,
> `IOperateTransitionDataSource`, `IStudioMapStyleCatalogDataSource`). The
> protocol→publication mapping is the single authority `Models/PublishProtocolCatalog.cs`.
> The old entry points (resources/layers/services/import/publish) redirect into the
> unified flow (`Pages/OperateDataRedirects.razor`); the Operate nav adopts the
> Data & Layers spine (§5.1). **Phase 3 (the AI outcome-approval driver) is a clean
> seam, not built** — `FlowDriver.Ai` surfaces the deferred outcome+approval intent
> over the same flow object; manual mode drives the flow today.

> **What this revision adds (founder direction, post-#198).** The first cut of this
> doc had the unified flow, the existing-resource intake, and dual-mode styling
> right, but it modeled the middle of the flow as a vague "Layer / Service" step and
> still drew a flat catalog list. The founder asked for three things that are the
> *heart* of the design, now folded in:
>
> 1. **The resource-first publication model** (§3.0) — the real metadata-v2 model:
>    you publish a **Resource**, then toggle which **Services/protocols** expose it,
>    which creates **Publications** (the "layers" clients consume). This replaces the
>    old "Layer/Service" step ③ with a **"Publish the resource → pick protocols"** step.
> 2. **A GeoServer-style treeview as the primary organizing UI** (§5.3) — a
>    **Resource → its protocol publications** tree (GeoServer "Store" == Honua
>    "Resource"), with a prominent **Layer Preview**. This *is* the cure for the
>    "resources separate from layers" split, and it's a migration on-ramp for
>    Esri/GeoServer users.
> 3. **Hide the plumbing in AI mode** (§5.5) — the AI surface reads as an
>    **outcome + one approval** ("I'll publish Maui Parcels as FeatureServer + STAC
>    and style it green — [Approve] [Edit] [Reject]"), not a plan/spec/tool-call/
>    dry-run dump. The plan and internals move behind an on-demand **Details**
>    disclosure; the same flow object drives it underneath.

Decision inputs this proposal honors:

- **The resource-first publication model is the spine** — confirmed in metadata-v2
  (`Honua.Core.Abstractions/Features/Metadata/Domain/V2/MetadataReleaseEnums.cs`,
  `MetadataV2Graph.cs`): three semantic kinds — **Resource** (canonical data +
  `schemaFields`), **Service** (publishes resources; carries `protocols`), and
  **Publication** (binds `resourceId` + `serviceId`). You publish a *resource* and
  select which services/protocols expose it; each binding is a Publication, and the
  Publications are the "layers" Esri/OGC clients consume. This is the inverse of
  BlueSpatial's service-first model — and the correct one. See §3.0.
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

Studio already has an **AI conversation + live-preview + approve** surface
(`Components/StudioAiConversation.razor`: left conversation column with structured
clarification cards, right live package preview; the design-fidelity scorecard
rates the Studio Map/Dashboard AI screens 93–94). But that AI posture lives **only
in Studio** (map/dashboard/report/app authoring). The Operate "add data → layer"
journey has **no AI driver at all** — it is pure forms. So the two product
directions the founder wants unified (agent-first per console#193, and a usable
manual fallback) are today split across two areas (Studio = AI authoring; Operate =
manual plumbing) with no shared flow. **And the existing AI surface itself draws a
founder critique — it "exposes too much plumbing"** (plan/spec/tool-call/dry-run
columns up front). So we reuse its *mechanism* but not its default presentation: the
data→publish AI mode reads as an outcome + one approval, plan-on-demand (§4.1/§5.5).

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

## 3.0 The resource-first publication model (the spine of the whole design)

Everything below — the flow, the treeview, the AI surface — hangs off one model,
confirmed in metadata-v2. There are **three semantic artifact kinds**
(`MetadataReleaseEnums.cs:MetadataSemanticArtifactKind`):

| Kind | What it is | metadata-v2 source |
|---|---|---|
| **Resource** | The canonical data + schema. Owns `schemaFields`, geometry, SRS, storage bindings. *The thing you actually have.* | `MetadataV2Resource` (`MetadataV2Graph.cs:135`); `schemaFields` at `:189` |
| **Service** | A publisher of resources. Carries **`protocols`** — the single source of truth for which protocol surfaces it exposes. | `MetadataV2Service` (`:560`); `Protocols` at `:619`; `MetadataV2ServiceType` (`MetadataV2Enums.cs:255`) |
| **Publication** | A **binding of `resourceId` + `serviceId`** — at most one per `(resourceId, serviceId)` pair. *This binding is the "layer" an Esri/OGC client consumes.* | `MetadataV2Publication` (`:743`); `resourceId`/`serviceId` at `:758`/`:765` |

The graph spells the direction out: *"Resource-first publication links from
`resourceId` to `serviceId`"* (`MetadataV2Graph.cs:89`) and *"Canonical resources.
Publications expose these resources through services"* (`:65`). So the real model is
**resource-first**:

```
  RESOURCE  ──(select services/protocols to expose it)──►  PUBLICATIONS  ──►  live REST surfaces
  (data +                                                   (one per                (FeatureServer/MapServer/
   schemaFields)        each toggle creates a Publication    resource×service)       STAC/WMS/WFS/WMTS/OGC API…)
```

You **publish the Resource**, then **toggle which Services/protocols hang off it**;
each toggle is a Publication; the Publications are the "layers." This is the **inverse
of BlueSpatial's service-first** flow (where you pre-create a Service and *then* hang
layers under it) — and it's the correct one, because the data (the resource) is the
durable thing and the protocol exposure is a cheap, additive, toggleable projection.

**Protocol vocabulary** (what the toggles are). Services declare `protocols` from
`ServiceProtocols` (e.g. `FeatureServer`, `MapServer`, `ImageServer`, `Stac`,
`OgcFeatures`, `OGC-API-Maps`, `OGC-API-Tiles`, `Wfs20`, `Wms`, `Wmts`, `OData`,
`Grpc`), with `MetadataV2ServiceType` naming the service category
(`ogc-api-features`, `wfs`, `wms`, `wmts`, `esri-feature-service`, `esri-map-service`,
`esri-image-service`, `stac-api`, `odata`, …). The publish step renders these as a
**checklist of protocols/services to expose the resource through** — not a "name a
service" form.

**What this changes in the redesign:** the old "③ Layer / Service" step becomes
**③ Publish — pick the services/protocols that expose this resource** (each pick is a
Publication). "Layer" stops being a thing you create; it is the *result* of a
Publication. This is the precise model the flat catalog only half-expressed.

---

## 3. The unified flow (the core fix)

One coherent journey replaces the five-page scatter. Same flow, whether an agent or
a human drives it. The middle step is now the **resource-first publish** (§3.0):

```
            ┌─────────────────────────────────────────────────────────────────────┐
            │                  CONNECTION → RESOURCE → PUBLISH → STYLE              │
            │                    (one route, one flow object)                       │
            └─────────────────────────────────────────────────────────────────────┘

  ① ADD DATA            ② RESOURCE            ③ PUBLISH (resource-first)      ④ STYLE          ⑤ GO LIVE
  intake bar (B1)       canonical data        pick services/protocols to     dual-mode (kept)  review + approve
  ┌──────────────┐      + schemaFields        expose this resource → each    ┌──────────┐      ┌──────────────┐
  │ Upload file  │      ┌──────────────┐      toggle = a Publication         │ MapLibre │      │ blast radius │
  │ Connect/pick │      │ inferred     │      ┌─────────────────────────┐    │   OR     │      │ + visibility │
  │   table      │─ingest▶│ fields,    │─publish▶│ FeatureServer  ✓     │─draws▶│ Esri  │──▶  │ + routes per │
  │ Remote svc   │      │ geometry,    │      │ MapServer      ✓        │    │ drawing  │      │   protocol   │
  │ Existing     │      │ SRS, issues  │      │ STAC           ✓        │    │  Info    │      │ → APPLY      │
  │   resource   │      │ (validation) │      │ WMS · WFS · OGC API …  │    └──────────┘      └──────────────┘
  │ Describe(AI) │      └──────────────┘      └─────────────────────────┘                        ends on the
  └──────────────┘            ▲                  (Publications created)                          resource node (B4)
                              └── "Existing resource" skips ingest, lands here ──┘                in the treeview (§5.3)
```

Key principles:

1. **No orphan "resource" vs "layer" split.** A resource is the canonical data
   produced in step ②; the "layers" are the **Publications** created in step ③ when
   you toggle protocols on. They are facets of one object in one flow, not two nav
   sections (kills the "Canonical resource" back-link, §1.1; pattern B2; the
   resource-first model, §3.0).
2. **One intake, five sources** (step ①, generalizing `EsriImportIntake`):
   *Upload file*, *Connect/choose a table on an existing connection*, *Remote
   Esri/OGC service*, ***Use an existing resource*** (the "I already have data,
   publish it" case), and *Describe it (AI)*. Picking "existing resource"
   skips ② and lands directly on ③ — the explicit answer to the founder's
   "use an existing resource to create a layer" gap.
3. **③ is resource-first publish, not a layer form** (§3.0): you toggle the
   **services/protocols** that should expose the resource (FeatureServer / MapServer /
   STAC / WMS / WFS / WMTS / OGC API / OData …). Each enabled toggle creates a
   **Publication**. The service slot/name is auto-provisioned with a "change"
   affordance (B-divergence) — never a mandatory pre-step.
4. **End on the artifact, not a toast** (B4): the flow terminates on the
   **resource's node in the treeview** (§5.3), its protocol publications and Preview
   right there.
5. **Style is the existing dual-mode editor** (step ④), embedded — MapLibre or Esri
   drawingInfo, server-converted. Not re-specified here.
6. **Go-live is an approval, not a form** (step ⑤, console#193): the review card
   shows the blast radius (`OperateResourcesPage` model) + the visibility and the
   **per-protocol routes** the publications will serve; Apply is the single
   authorizing action.

### 3.1 Flow object (shared by both drivers)

```
DataToPublishFlow                                  # (was DataToLayerFlow — renamed for the resource-first model)
  Source        : { kind: file|table|remoteService|existingResource|aiPrompt, ref }
  Resource      : { resourceId?, inferredFields[], geometry, srs, validationFindings[] }
  Publications[]: { serviceId?, serviceType, protocols[], identifier?, isPrimary,    # §3.0: one per service/protocol set
                    autoProvisioned: bool }        #   each entry binds Resource→Service (a metadata-v2 Publication)
  Style         : { styleId, encoding: MapLibre|Esri }     # reuses StudioStyleEditor
  GoLive        : { visibility, embedPolicy, routesByProtocol{}, blastRadius }
  Driver        : agent | manual
  Step          : addData | resource | publish | style | golive | done
  Approvals[]   : { step, plan, dryRun?, decidedBy, decision }
```

The shape mirrors metadata-v2 (§3.0): one `Resource`, a list of `Publications` (one
per service/protocol exposure), and the styled, gated go-live. Both drivers mutate the
*same* flow object and hit the *same* server contracts (file import, publish-resource,
toggle-protocol, style save). The only difference is **who fills the fields and who
clicks Apply** — and, in AI mode, **how much of this object the human sees** (§5.5).

---

## 4. AI mode AND non-AI mode — one flow, two drivers

> Per console#193: agents do the writing; the console witnesses and authorizes. So
> **AI is the default driver**; manual is the always-available fallback. A single
> **driver toggle** in the flow header switches between them *without losing the
> flow object* — you can start with AI, drop to manual to hand-tune step ③, and
> resume.

### 4.1 AI mode (default) — outcome + one approval, plumbing hidden

> **Founder critique this answers:** the current AI studio flows *"expose too much
> plumbing."* The first cut of this doc reused `StudioAiConversation.razor` as-is —
> i.e. the very surface being criticized, with its plan/spec/tool-call/dry-run
> columns visible by default. **This revision keeps the same underlying flow object
> but changes what the human sees:** AI mode reads as an **outcome statement + a
> single approval**, with the plan and internals tucked behind an on-demand
> *Details* disclosure.

The default AI surface is **one card, not a transcript**:

- The human states intent ("publish Maui Parcels as a public layer, styled green").
- The agent works the whole flow object silently and surfaces **one outcome card**:
  > *"I'll publish **Maui Parcels** as **FeatureServer + STAC** and style it
  > **green**."*  **[ Approve ]   [ Edit ]   [ Reject ]**
- **Approve** is the single authorizing action — it runs the whole flow (it does not
  walk the human through five sub-approvals unless they ask).
- **Edit** opens the relevant step's manual control inline (drops to the wizard
  inputs for just that facet, §4.3), then re-summarizes.
- **Reject** discards and re-prompts.
- **The plan/internals are available on demand, never the default surface.** A
  *Details ▸* disclosure on the card reveals what §4.1-old put up front: inferred
  fields/geometry/SRS, the exact Publications (service/protocol bindings) it will
  create, the chosen style encoding, the dry-run result, and the blast radius. Power
  users and audits get the full plan; the default reader gets the outcome.
- Ambiguity that genuinely blocks the outcome → **one inline clarification** with
  effect-labeled choices (reusing `StudioAiConversation`'s clarification cards), not a
  multi-turn interrogation.
- Maps cleanly onto the devops console-bridge posture (console#193:
  `create_gitops_proposal` / `get_devops_operation_status`): the single Approve emits
  the proposal/operation the timeline tracks; the *Details* disclosure is where that
  plan/diff lives.

This is the "minimal forms, information + approval" posture of console#193 taken to
its conclusion: the human authorizes an **outcome**, and the plumbing
(services, protocols, publications, dry-runs) is *information available on request*,
not the headline. See the revised wireframe in §5.5.

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

GeoServer's webadmin sidebar IA is the model the founder pointed at: a familiar
left-rail of *Data → Stores → Layers* with a Layer Preview, narrow and stable. We
adopt that shape (a single **Data & Layers** spine fronting a tree) — a deliberate
familiarity win for migrating Esri/GeoServer users.

```
BEFORE (Layout/ConsoleLayout.razor:82–95)        AFTER
  Connections                                       Data & Layers      ← resource-first TREE (resources→publications)
  Resources        ─┐                                 ├─ + Add data    ← launches the flow (route below)
  Services          ├─ four parallel nouns            ├─ Layer Preview ← GeoServer-style "see the output fast" (B8)
  Layers           ─┘                                 └─ Connections   ← sources stay a sub-section (B6)
  Versions                                          Publishing
  Catalogs                                          Versions
  Settings / Access / Temporal / Observability / Metrics  (unchanged)   Catalogs
```

`Connections` stays (a data *source* is legitimately its own concept, B6 ≙
GeoServer's "Stores → connection params") but moves *under* Data & Layers as a
sub-section. `Resources`, `Services`, and `Layers` collapse into the single
**Data & Layers** tree (§5.3). **Layer Preview** is promoted to a first-class
sidebar entry — GeoServer's "immediate visibility" onboarding lever, so a user sees
a rendered result fast rather than only catalog rows.

### 5.2 Route map delta (additive; governed by `docs/console-route-map.md`)

```
NEW   /operate/data                         Resource→publications treeview (the GeoServer-style spine, §5.3)
NEW   /operate/data/new                     The data → publish flow  (?driver=ai|manual, ?step=add|resource|publish|style|golive)
KEEP  /operate/data/:id                     Resource node detail = the resource + its publications (merges /operate/resources/:id + /operate/layers/:id)
REDIRECT /operate/resources  → /operate/data
REDIRECT /operate/layers     → /operate/data           (a "layer" is now a Publication node under its resource)
REDIRECT /operate/services   → /operate/data?view=services
REDIRECT /operate/resources/new, /operate/resources/import, /operate/publishing/quick, /operate/import/service
         → /operate/data/new?source=<file|table|remoteService>   (the five entry points fold into one)
KEEP  /operate/connections*                 (data-source management; now reached under Data & Layers)
KEEP  /studio/styles/:id                    (dual-mode style editor — embedded by step ④, unchanged)
```

`/operate/import/esri/*` (content import: web maps/dashboards/storymaps) stays
distinct — it is *content* migration, not the data→layer flow (route map §106 calls
out this distinction explicitly). The flow may *link* to it as an alternate source.

### 5.3 Wireframe — GeoServer-style **resource → publications treeview** (the primary spine)

This is the structural heart of the IA. GeoServer shows **Layers under a Store**; the
exact analogy is **Honua Publications under a Resource** (GeoServer "Store" == Honua
"Resource", §3.0). Making that hierarchy *visible* is what finally cures the
"resources separate from layers" split that the old flat catalog only half-addressed.
Each resource node expands to its **protocol publications, rendered as toggles** —
the same toggles the publish step (§3.0/§5.4b) writes:

```
┌ Operate / Data & Layers ─────────────────────────────────────┬─ Layer Preview ──────────────┐
│  Data & Layers                          [ + Add data ▾ ]      │  parcels · FeatureServer      │  ← GeoServer "see
│  Resources, and the services/protocols each is published on.  │ ┌──────────────────────────┐ │     output fast"
│  ⌕ filter…     [ All ][ Published ][ Draft ][ Needs review ]  │ │   ▢▢▢ rendered map tile   │ │     (B5/B8)
│ ┌───────────────────────────────────────────────────────────┐│ │   (the served endpoint)   │ │
│ │ ▼ ● parcels        file · 12,403 feat · polygon  ▶ Running ││ └──────────────────────────┘ │
│ │     resource rsc_8f…   (canonical data + 14 fields)        ││  open in: FeatureServer ▾     │  ← preview == the
│ │     ├─ FeatureServer  ✓  /city/parcels/FeatureServer/1     ││  [ Style ] [ Share ] [ ⋯ ]    │     published URL
│ │     ├─ MapServer      ✓  /city/parcels/MapServer            ││                               │
│ │     ├─ STAC           ✓  /stac/collections/parcels         │└───────────────────────────────┘
│ │     └─ WMS · WFS · WMTS · OGC API · OData    [ + expose ▾ ] │   ← dormant protocols = one click
│ │        [ Preview ] [ Style ] [ Share ] [ ⋯ ]               │      to add a Publication (§3.0)
│ ├───────────────────────────────────────────────────────────┤
│ │ ▶ ○ zoning         table · postgis: city_db.zoning ⏹ Draft │   ○ = resource exists, no Publications yet
│ │     resource rsc_2a…   (not published)  [ Publish → ]       │      → the resource-first publish flow (§5.4)
│ ├───────────────────────────────────────────────────────────┤
│ │ ▼ ● traffic-sensors  remote · arcgis…/Traffic   ▶ Running   │
│ │     ├─ FeatureServer  ✓  /live/traffic/FeatureServer/0      │   org-only
│ │     └─ STAC           ✓  /stac/collections/traffic         │
│ └───────────────────────────────────────────────────────────┘
└───────────────────────────────────────────────────────────────┘
```

Why this is the cure, not cosmetic:

- **The hierarchy is now visible.** The "Canonical resource" back-link column (§1.1)
  is gone because the resource *is* the parent node and its publications hang under
  it — exactly GeoServer's Store→Layers tree, exactly metadata-v2's
  Resource→Publications (§3.0). No more bouncing between a Resources page and a
  Layers page to reconcile what is one object.
- **Publications render as protocol toggles** (`FeatureServer ✓ · MapServer ✓ ·
  STAC ✓ · WMS …`). A checked protocol is a live Publication; an unchecked one under
  `[ + expose ▾ ]` is one click to create another (additive, cheap — the
  resource-first promise). Toggling here is the *same* act as step ③ of the flow.
- **Layer Preview is first-class** (right rail + the sidebar entry, §5.1):
  GeoServer's "immediate visibility" onboarding. The preview targets the **served
  endpoint** for the selected protocol (B8: "what I preview is what I publish"), so
  users see a rendered result immediately and pick which protocol to open it in.
- **Context-sensitive actions** (B3) live on the selected node: a resource offers
  *Publish / Preview / Style / Share*; a single publication offers *Preview / Open
  route / Unpublish*.
- **Migration advantage:** this is the IA Esri (Portal/Server "service → layers") and
  GeoServer ("store → layers") users already have in muscle memory. The tree lowers
  the switching cost — an explicit moat note for the redesign.

### 5.4 Wireframe — the data → publish flow (manual driver)

```
┌ Operate / Add data                                      ◀ AI ─[ Manual ]▶ ┐  ← driver toggle (§4.3)
│  ●─────────●─────────○─────────○─────────○                                │
│  Add data  Resource  Publish   Style     Go live                          │  ← step rail (1 flow, §3)
│ ┌─────────────────────────────────────────────────────────────────────────┐│
│ │  ① Add data                                                             ││
│ │  [ Upload file ][ Connect a table ][ Remote service ][ Existing resource ]│ ← intake bar (B1, EsriImportIntake)
│ │  ┌───────────────────────────────────────────────────────────────────┐ ││
│ │  │ Drop a .geojson/.shp.zip/.gpkg…  or  ⌕ choose existing resource… │ ││  "existing resource" → skips to ③
│ │  └───────────────────────────────────────────────────────────────────┘ ││
│ │                                          [ Cancel ]   [ Continue → ]    ││
│ └─────────────────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────────────────┘
        … step ② shows the RESOURCE: inferred fields/geometry/SRS + validation findings …
        … step ③ is the RESOURCE-FIRST PUBLISH step — pick protocols/services (below, §5.4b) …
        … step ④ embeds StudioStyleEditor (MapLibre | Esri) …
        … step ⑤ is Go live: visibility + per-protocol routes + blast radius (§5.6) …
        → ends on the resource node in the treeview (B4, §5.3)
```

### 5.4b Wireframe — the **primary** step ③: *publish a resource → pick services/protocols*

This is the redesign's headline interaction and the direct expression of the
resource-first model (§3.0). The user is past "I have data" (the resource exists);
now they **toggle which protocol surfaces expose it**. Each toggle they enable becomes
a metadata-v2 **Publication** (`resourceId` + `serviceId`). No "name a service" form
fronts this — the service slot is auto-provisioned, editable under *change*.

```
┌ ③ Publish "parcels" — choose how to expose this resource ───────────────────┐
│  Resource: parcels   (12,403 polygon features · 14 fields · EPSG:2926)      │  ← the thing you have (§3.0)
│  Pick the services/protocols that should serve it. Each is a Publication.    │
│ ┌─ GeoServices (Esri) ───────────────┬─ OGC ──────────────────────────────┐ │
│ │ [✓] FeatureServer   editable feat.  │ [ ] OGC API Features               │ │  ← protocol toggles
│ │ [✓] MapServer       rendered tiles  │ [ ] WMS    rendered                │ │     = Publications
│ │ [ ] ImageServer     (raster only)   │ [ ] WFS    feature download        │ │     (ServiceProtocols /
│ ├─ STAC / Catalog ───────────────────┤ [ ] WMTS   tiled                   │ │      MetadataV2ServiceType,
│ │ [✓] STAC API        collection      │ [ ] OData                          │ │      §3.0)
│ └─────────────────────────────────────┴────────────────────────────────────┘ │
│  Service slot:  city/parcels   (auto)            [ change… ]                  │  ← auto-provision (B-divergence)
│  Will create 3 publications →  FeatureServer/1 · MapServer · stac/parcels     │  ← preview of what ③ writes
│                                              [ Back ]   [ Continue to style → ]│
└──────────────────────────────────────────────────────────────────────────────┘
```

The same toggle control appears on the treeview's `[ + expose ▾ ]` (§5.3): publishing
a resource and exposing an *already-published* resource through one more protocol are
the **same act** on the same model — add a Publication.

### 5.5 Wireframe — the AI driver: **outcome + one approval, plumbing hidden** (default)

Per the founder critique (§4.1), the AI surface is **not** a plan/spec/tool-call/
dry-run dump. It is a single outcome card; the plan lives behind *Details ▸*.

```
┌ Operate / Add data                                      ◀[ AI ]─ Manual ▶ ──┐
│ ┌─ Conversation (StudioAiConversation) ──────────────────────────────────┐  │
│ │ You:  publish Maui Parcels as a public layer, styled green             │  │
│ │                                                                        │  │
│ │ Honua:                                                                 │  │
│ │  ┌──────────────────────────────────────────────────────────────────┐ │  │
│ │  │  I'll publish **Maui Parcels** as **FeatureServer + STAC**         │ │  │  ← the OUTCOME,
│ │  │  and style it **green**.                                          │ │  │     not the plan
│ │  │                                                                    │ │  │
│ │  │      [ Approve ]      [ Edit ]      [ Reject ]                     │ │  │  ← ONE approval
│ │  │                                                                    │ │  │
│ │  │  Details ▸  (inferred fields · publications · dry-run · blast)     │ │  │  ← plumbing on demand
│ │  └──────────────────────────────────────────────────────────────────┘ │  │
│ │                                                                        │  │
│ │  (if genuinely ambiguous, ONE inline choice — e.g.                     │  │
│ │   "Geometry precision?  [ keep ]  [ snap 1m ]" — else nothing)         │  │
│ │ ┌ refine… ─────────────────────────────────────────────┐  [ Send ]    │  │
│ └─┴───────────────────────────────────────────────────────┴─────────────┘  │
│  Approve emits the devops proposal/operation; the plan/diff lives in Details.│  ← console#193
└──────────────────────────────────────────────────────────────────────────────┘
```

Expanding **Details ▸** reveals exactly what the old surface put up front — the
inferred schema, the precise Publications (service/protocol bindings) to be created,
the dry-run, and the blast radius — but only when asked. The default reader sees an
outcome and one button.

### 5.6 Wireframe — Go live (step ⑤, both drivers; the approval payload)

```
┌ ⑤ Go live — review & approve ───────────────────────────────────────────────┐
│  Resource "parcels"  →  3 publications on service city/parcels (new)        │
│  Visibility:  ( ) Private   ( ) Org   (•) Public        Embed: [ allow ▾ ]  │
│  Routes:   FeatureServer  /city/parcels/FeatureServer/1                     │  ← per-protocol routes
│            MapServer      /city/parcels/MapServer                           │     (one per Publication, §3.0)
│            STAC           /stac/collections/parcels                         │
│ ┌ Blast radius (OperateResourcesPage model) ─────────────────────────────┐ │
│ │  Services 1   Publications 3   Saved maps 0   Share links 0   Apps 0    │ │  ← info, console#193
│ └─────────────────────────────────────────────────────────────────────────┘│
│                                            [ Back ]      [ Apply & publish ]│  ← single authorizing action
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 6. Mapping to honua-console + phased plan

### 6.1 Reuse / refactor / add

| Action | Component / page | Notes |
|---|---|---|
| **Reuse** | `Components/StudioAiConversation.razor` | The AI driver's rail. **But default to the outcome card (§5.5), not its plan/spec columns** — the founder's "too much plumbing" critique is about exactly this surface; the plan moves behind *Details* (§4.1). |
| **Reuse** | `Pages/StudioStyleEditorPage.razor` (+ `StyleReferencePicker`) | Step ④, embedded. Dual-mode preserved. |
| **Reuse** | `Components/OperateCapabilityStateList`, missing-binding surfaces | Flow keeps neutral/missing-binding states; never fabricates. |
| **Generalize** | `Components/EsriImportIntake.razor` → `AddDataIntake.razor` | Add modes: *table*, *existing resource*, *AI prompt* (it already has paste/upload/URL/connected). |
| **Merge** | `OperateResourcesPage` + `OperateLayersPage` + `OperateServicesPage` → `OperateDataPage` (`/operate/data`) | The **resource→publications treeview** (§5.3), not a flat list: each resource node expands to its protocol publications. Old routes redirect. |
| **Merge** | `OperateResourceDetailPage` + `OperateLayerDetailPage` → `/operate/data/:id` | One node = resource + its publications; inline Preview/Style/Share/Publish (B3/B4). |
| **Re-sequence** | `OperateImportFilePage`, `OperatePublishLayerPage`, `OperateImportServicePage` | Their bodies become **steps** of the flow (`/operate/data/new`), not standalone pages. The publish-layer page body becomes the **resource-first protocol-toggle step ③** (§5.4b). Old routes redirect into `?source=`. |
| **Add** | `Components/Operate/DataToPublishFlow.razor` (host) + `DataToPublishFlowState` (model) | The flow object (§3.1) + step rail + driver toggle. Wraps the reused step bodies. |
| **Add** | `Components/Operate/AddDataIntake.razor` | Step ① (sketched in `src/Honua.Console.Shell/Components/AddDataIntake.razor`). |
| **Add** | `Components/Operate/PublishProtocolPicker.razor` | Step ③ — the protocol/service toggle grid (§5.4b); also reused by the treeview's `[ + expose ▾ ]`. Each toggle ↔ a metadata-v2 Publication. |
| **Add** | `Components/Operate/ResourcePublicationsTree.razor` (+ `LayerPreviewPane`) | The §5.3 spine: resource nodes, publication toggles, the served-endpoint preview (B8). |
| **Add** | `Components/Operate/AiOutcomeApprovalCard.razor` | The §5.5 outcome+approval surface with the *Details* disclosure; wraps `StudioAiConversation`'s plan as the disclosure body. |
| **Update** | `Layout/ConsoleLayout.razor:82–95` + `ConsoleRouteMap` | IA change (§5.1) + the route delta (§5.2). |

No server contract changes: the flow calls the **same** import/publish/style
endpoints these pages already bind (`IConsoleFileImportOperation`,
`IServiceLayerPublishOperation`, `IConsoleServiceImportOperation`,
`IStudioMapStyleCatalogDataSource`). The protocol-toggle step writes Publications via
the existing publish operation per enabled protocol — resource-first, no new contract.
This keeps the change Console-side and governed by the Patterns Charter.

### 6.2 Phased implementation

**Phase 0 — this doc.** Design approval (founder + console route-map reconcile).

**Phase 1 — highest-impact slice: the manual flow with the resource-first publish step
(`/operate/data/new`).** Build `DataToPublishFlow.razor` + `AddDataIntake.razor`
(generalize `EsriImportIntake`) + **`PublishProtocolPicker.razor`** (the step-③
protocol/service toggle grid, §5.4b — the heart of the resource-first model). Wire the
existing import/publish/style step bodies into the rail, end on the resource node (B4).
Redirect the five old entry points into `?source=`. *This one slice delivers the
founder's "add a service and import data" and "use an existing resource to create a
layer" journeys **and** makes the resource-first publish concrete.* Manual driver only.

**Phase 2 — the GeoServer-style treeview (`/operate/data`, §5.3).** Build
`ResourcePublicationsTree.razor` + `LayerPreviewPane`: resource nodes expanding to
protocol-publication toggles, the served-endpoint Layer Preview, context-sensitive
node actions. Redirect `/operate/resources`, `/operate/layers`, `/operate/services`;
apply the IA nav change (§5.1). **This is what actually cures the
"resources separate from layers" split** — promote it ahead of the AI driver, since
it is the structural fix. Reuse `PublishProtocolPicker` for the tree's
`[ + expose ▾ ]`.

**Phase 3 — AI driver: outcome + one approval (§5.5).** Add the
`AiOutcomeApprovalCard` over `StudioAiConversation`, defaulting to the outcome
statement + single Approve/Edit/Reject with the plan behind *Details ▸* (the
plumbing-hidden surface, §4.1). Driver toggle (§4.3); Edit drops to the relevant
manual step. Wire Approve to the devops console-bridge proposal/operation surface
(console#193); the plan/diff is the *Details* body.

**Phase 4 — preview + polish.** Per-protocol preview via each served endpoint (B8,
reuse the MapPreview target the Esri-import pages use); empty-state onboarding that
teaches resource→publish→expose (B5); determinate ingest progress everywhere (B7).

### 6.3 Risks / constraints

- **Route governance:** `/operate/data*` and the redirects must be reconciled into
  `docs/console-route-map.md` (its §1 taxonomy + §5 disposition tables) before
  Phase 2 lands — this proposal is additive but the route map is the source of truth.
- **Stay true to the resource-first model** — the publish step toggles
  protocols/services on a *resource* and writes Publications (§3.0). Do **not** revert
  to a service-first "name a service, then add layers" form; that is BlueSpatial's
  inverted model and the founder's explicit non-goal.
- **Dual-mode style must not regress** — step ④ embeds the existing editor; do not
  fork it.
- **No fabricated state** — every step and every tree node renders
  missing-binding/capability states when the server base URL is unset, exactly as the
  current pages do; the treeview must not invent resources or publications.
- **Keep the plumbing hidden in AI mode** — the default AI surface is the outcome
  card; the plan/spec/dry-run stays behind *Details* (§4.1/§5.5). Don't re-expose
  `StudioAiConversation`'s full plan columns as the default, which is the exact
  founder critique.
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
- **Resource-first model (metadata-v2, honua-server):**
  `Honua.Core.Abstractions/Features/Metadata/Domain/V2/MetadataReleaseEnums.cs`
  (`MetadataSemanticArtifactKind`: Resource / Service / Publication),
  `MetadataV2Graph.cs` (`MetadataV2Resource:135` + `schemaFields:189`;
  `MetadataV2Service:560` + `Protocols:619`; `MetadataV2Publication:743` +
  `resourceId:758`/`serviceId:765`; graph comments at `:65`, `:89`),
  `MetadataV2Enums.cs:255` (`MetadataV2ServiceType`), `ServiceProtocols.cs`
  (protocol string constants).
- GeoServer webadmin (IA reference): the Stores → Layers tree, the Layer Preview
  page, and the quickstart "publish a store, see it rendered fast" onboarding — the
  model for §5.1/§5.3.
- BlueSpatial (old UI, *service-first* — the inverted model we reject for §3.0):
  `BlueSpatial.Web/Views/Admin/manage-metadata-component.*`,
  `create-layer-modal-component.*`, `add-layer-from-{file,database}-component.*`,
  `import-service-modal-component.*`, `manage-connection-component.*`,
  `Renderer/create-renderer-component.*`; preview `PreviewPlugin/MapPreview/map.js`.
- Founder direction: console#193 (information + approval, forms-light, agent-first);
  post-#198 direction (resource-first publication, GeoServer-style treeview, hide the
  plumbing in AI mode).
