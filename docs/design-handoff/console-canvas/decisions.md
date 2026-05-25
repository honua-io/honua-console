# Honua Console · Model Decisions

Every consequential design / model decision, in one place. Read this before
extending any surface — most surface choices are downstream of one of these.

---

## Identity hierarchy

```
Workspace   = a cohort of envs that share canonical content & definitions
            (e.g. "Public Works" with dev/staging/prod)
  Environment = dev / staging / prod (single workspace is the default; multi only when needed)
    Fleet     = the N replicas backing one env (ECS tasks, K8s pods, batch workers).
                Mostly system-managed. Surfaced only for SRE / health drill-down.
      Content / Resource = the canonical objects (data resources, maps, dashboards,
                            forms, apps, reports, queries, analyses, workflows…)
        Publication / Layer Slot = where a Data Resource is exposed (one slot per service)
          Resource field-level rules = PII redaction, hide-from-public, etc.
```

**No tenant / org switching in v1.** A single org sees its workspace(s) and envs.

---

## Data model — the four primitives that matter

| Primitive | What it is |
|---|---|
| **Connection** | persistent credential-bound store (Postgres, S3, SQL Server, FGDB, Snowflake). NOT remote services — those are migration sources, not connections. |
| **Data Resource** | the canonical thing operators describe once: source binding, fields, metadata, access defaults, validation, presentation. Lives above environments. |
| **Service** | runtime endpoint (FeatureServer, MapServer, ImageServer, OGC API Features, OGC API Records, WMS, WMTS, OData). |
| **Layer slot** | the publication record inside a service; binds one Data Resource. **Many layers can reuse the same resource.** A layer can override presentation but never semantics. |

**Migration vs sync**: importing from a remote Esri/OGC/WMS service is a one-time
**migration** that copies data into Honua-managed storage. We don't proxy. We
don't poll. We don't store the remote credentials. To pick up later changes,
operators run **Migrate again**.

---

## Folder model

Folders are a Honua primitive (not from any doc — added in design because
operators expect Esri-style folder grouping in REST URLs). Folders are
organisational only, no security implications. Routes are derived:
`/folder/service/kind`. Routes are never editable; renaming a folder/service
shifts the route.

---

## Catalog model

Catalogs are discovery endpoints. Each catalog has a **server-wide on/off** in
Settings → Catalog endpoints; turning it off disables all per-service registration
checkboxes.

| Catalog | Auto-default? | Source |
|---|---|---|
| Esri catalog | **default-on** | every Esri service publication |
| OGC API Records | **default-on** | every OGC API Features publication |
| OData catalog | **opt-in per entity set** | OData services |
| STAC | opt-in per resource | resource-led publish |
| DCAT | opt-in per resource | resource-led publish |

Esri + OGC catalogs auto-mirror service publications (checkbox pre-checked,
operator can uncheck). OData / STAC / DCAT require explicit opt-in.

**Catalogs are consolidated.** STAC + DCAT are first-class catalogs in Honua
Console with the same shape as the others.

---

## Styling model

**Canonical = MapLibre GL Style JSON** on the Data Resource. Edited in Maputnik.

| Encoding | Canonical? | How produced |
|---|---|---|
| MapLibre GL | ✅ canonical | authored in Maputnik |
| SLD / SE | generated build artefact | auto-translated from canonical |
| Esri Renderer JSON | generated build artefact | auto-translated from canonical |
| QGIS QML | generated build artefact (sidecar) | auto-translated from canonical |
| 3D Tiles | n/a (vector resources) | n/a |

**OGC API Styles is the endpoint** — one style URL per resource serves all
encodings via content negotiation. Services link to the resource style URL;
they don't fork it.

**Slot overrides** when a target needs something the canonical can't express
(Esri arcade `valueExpression`, dotDensity, SLD per-class ColorMap). Override
state on each binding slot: `tracking canonical` / `override active` / `canonical
changed · needs review` / `resync available` / `needs rebuild`. Overrides are
named drafts; resync archives them (never destroys).

---

## Publish flow

Two entry points, same Publication record:

1. **Quick publish** (common case): `Service → Layer → Review` (3 steps).
   Operator never thinks about the canonical resource — Honua creates it
   implicitly.
2. **Author-resource-first** (power case): author the resource, then publish
   it to N services from the Publish tab.

**Folder → Service → Layer** is the canonical hierarchy. At the Layer step,
the operator either binds an **existing** Data Resource (which can already
back layers in other services) or **creates a new** Data Resource inline.

---

## Field state vocabulary

Every form field on every screen falls into one of five states:

| Marker | State | Visual | Editable? |
|---|---|---|---|
| ✏️ | Input | normal field | yes |
| 🔍 | Discovered (auto-filled, overridable) | dashed border, "auto" pill, Override link | yes (override sticks across re-introspection) |
| 🧮 | Calculated from other fields | grey background, "→ derived from X" hint | no |
| ⚙️ | System-assigned | greyed mono | no |
| 🔒 | Admin / server config | grey italic, links to Settings | no |

**Scope chips** on every field group communicate blast radius:
- 🌐 Resource-wide (affects N layers across M services)
- 🔧 Layer-only (publication-local)
- ⚙️ Service-wide (all layers in this service)
- 🖥 Server-wide (Settings)

---

## RBAC model

Hierarchy: **Workspace → Environment → Content → Publication → Resource field**.
Defaults flow top → bottom; overrides allowed at any level.

**8 built-in roles** (matrix in `screens-collab-rbac.jsx · RBACOverview`):

| Role | View public | View internal | Comment | Draft / collab | Publish | Edit access | Manage roles | Server admin |
|---|---|---|---|---|---|---|---|---|
| Admin | ● | ● | ● | ● | ● | ● | ● | ● |
| Workspace owner | ● | ● | ● | ● | ● | ● | ● | — |
| Publisher | ● | ● | ● | ● | ● | env-scoped | — | — |
| Editor | ● | ● | ● | ● | — | — | — | — |
| **Draft collaborator** *(new)* | ● | ● | ● | scoped | — | — | — | — |
| Reviewer | ● | ● | ● | — | — | — | — | — |
| Org viewer | ● | ● | — | — | — | — | — | — |
| Anonymous | ● | — | — | — | — | — | — | — |

Custom roles supported (e.g. `Auditor` with conditional "no PII" cell).
Built-ins are frozen — clone to create a custom variant.

**Invite defaults**: MFA required, time-limited (30d), env-scoped, optional content-scoped, optional IP allowlist.

**Identity sources**: OIDC (Entra / Okta), SAML, API keys, Entra group sync (groups are first-class members).

---

## Collaboration (Felt-style)

| Surface | Multiplayer features |
|---|---|
| Studio Map | full multiplayer: presence + cursors + drawing markup layer + feature-pinned comments + follow mode + live activity |
| Studio Dashboard / Form / Report / App / Workflow | presence + comments (no cursors, no drawing) |
| Resources / Services | presence + comments |
| Public links | view-only, no collab |

**Comments** can be pinned to: feature, layer, or AI-message. Threads survive
edits via auto re-anchoring. Each comment can attach linked actions (e.g.
"linked job: refresh succeeded 2m ago").

**Markup layer** is per-draft — drawing arrows / sticky notes / freehand on a
map doesn't touch the canonical style.

---

## Environments / GitOps

- **Canonical** (resources, metadata, styles, audiences) lives **above** envs.
- **Per-env state**: connection credentials, runtime overrides, fleet of compute, jobs, activity.
- **Promotion** = config-repo commit. Each release shows: semantic diff with
  breaking-change highlights, env matrix (item × dev/staging/prod with version
  transitions), compatibility preflight (schema / access / runtime / data
  scripts), data-script coverage, Git PR preview, CI timeline (14+ checks
  serialised), apply strategy picker (rolling / blue-green / immediate), auto-rollback
  window (10m default).
- **Rollback** = re-apply previous bundle. Available 14d.
- **Auto-rollback** on critical alert fired during the rollback window.

---

## Temporal / Sync

- Temporal-enabled resources have an **as-of slider** with edit ticks + release ticks; selected timestamp shows side-by-side as-of vs now.
- **Selective rollback** quarantines current values into a recovery snapshot before applying.
- **Sync conflicts** (disconnected replicas reconnecting) auto-resolve when fields match latest-wins / spatial-snap ≤1m / temporal-server-wins-past-publish.
- Remaining conflicts get a **3-way merge** UI: base / client / server columns with field-level highlights + AI advisory recommending winner.

---

## AI

- **AI is never agentic.** All recommendations require operator approval.
- **AI is always evidence-linked.** Every brief lists the events / objects /
  patterns it drew from.
- **Reasoning trace** one click away on every recommendation.
- AI surfaces appear: inside Event detail drawer, inside Investigations, inside
  Sync conflict review, as a standalone **AI DevOps** advisory home with briefs
  drilled-into single-brief views.

---

## Naming / vocabulary discipline

These terms are **forbidden as primary UI labels** (per Honua product spec):
`storageBinding`, `projectionProfile`, `ABAC`, `canonical graph`, `distribution
object`, `policy condition`, `runtime snapshot`. They appear only in Advanced
diagnostics / raw object inspectors.

Use instead: Source, Style, Access, Slot label, Catalog entry.

**No separate product surface.** STAC + DCAT publishing lives in Honua Console.

**Service catalog terminology**: it's **"Esri catalog"** (not Esri item, not
GSR, not GeoServices REST catalog).

**Migration** (one-time copy), not **sync** / **proxy** / **mirror**, when
talking about importing from remote services.

---

## State coverage

Every list / detail / wizard must handle:

- Empty (first run)
- Loading (skeleton)
- Warning (non-blocking, e.g. schema drift)
- Blocked (must-fix, e.g. publish blocked by CRS missing)
- Partial success (e.g. 2 of 3 layers imported — don't hide the wins)
- Success (with actionable next step)
- Error · system (with trace ID)
- Permission denied (with "request role" affordance)
- Filtered-empty (with clear-all)

See `screens-settings-states.jsx · StatesGallery` for the canonical treatments.

---

## Native host (regulated deployments)

Desktop shell wrapping the web app. Features specifically for gov / regulated:
- mTLS with client cert in `~/.honua/`
- Server cert pinning (SHA-256 pin recommended)
- Encrypted profile storage (OS keychain)
- Offline cache for content
- Audit log forwarded to workspace audit endpoint

Cert change detection blocks the connection until operator re-verifies.

---

## Things we explicitly did NOT build

- **Tenant / org switching** — out of v1 by spec.
- **AI agentic actions** — advisory only.
- **Embedded Felt-style voice / video huddle** — out of scope; OS shells handle.
- **3D Tiles styling editor** — n/a for vector-first v1.
- **Real-time data write-back through Honua to source databases** — Honua is read-publish, not a transactional middleware.
