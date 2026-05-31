# Issue #122 · Esri content-import surfaces — build handoff

Mockups for the four Esri content-import tickets (#100 / #101 / #102 / #104).
These are **content imports** — distinct from the existing data-layer → PostGIS
importer (`screens-wizards.jsx#WizImportEsri`), which the issue explicitly calls
out as a different flow.

## Files

| File | Covers | Components |
|---|---|---|
| `screens-import-esri.jsx` | #100, #101, #104 | `ImportEsriWebMap`, `ImportEsriDashboard`, `ImportStoryMap`, `Fid` (fidelity badge helper) |
| `screens-import-wizard.jsx` | #102 | `ImportEsriWizard`, `ImportRunProgress`, `ParityScorecard`, `ImportMissingBinding` |

Both load after `shell.jsx`, `field-state.jsx`, `map-preview.jsx`. Each component
is `Object.assign(window, …)`-exported. Built on `wireframe.css` tokens.

## Artboard → ticket map

| Artboard | Ticket | Purpose |
|---|---|---|
| Import Esri Web Map | #100 | Web Map JSON → `honua.map-package.v1`. Intake (paste/upload/URL/connected ArcGIS), layer mapping table, per-layer fidelity, MapPreview target, missing-binding banner. |
| Import Esri Dashboard | #101 | Dashboard JSON → dashboard package. Element inventory, element→widget grid, target layout preview. |
| Import StoryMap / Hub | #104 | StoryMap/Hub → report. Section→block mapping, target content preview. (P4) |
| Import wizard · Map | #102 | Multi-step wizard, step 3. Mixed content types, per-item fidelity + blockers. |
| Import wizard · Run | #102 | Progress, resumable, per-item state, 1 failed/retry. |
| Parity scorecard | #102 | pass/degraded/binding/failed counts, overall parity bar, per-item findings, export report (PDF) + findings (CSV). |
| Missing-binding | #102 | "No honua-server configured" — Charter §11 binding-required state, never mocked data. |

## Fidelity convention (shared across all surfaces)

Every per-item row carries a fidelity badge via the `Fid` helper:

- `clean` → **converts clean** (green) — full fidelity
- `degrade` → **degrades** (yellow) — usable, but something changed (named in the note)
- `drop` → **dropped** (red) — can't import (named + reason)
- `manual` → **needs review** (blue) — usually a data-resource binding required

Source→target mapping is always explicit (Esri thing → Honua thing) with the
reason for any degrade/drop in the adjacent note column.

## State coverage (per shared requirements + Charter §11)

Each surface represents: empty · loading · success · **partial import** ·
error/unsupported element · **missing-binding (no honua-server)**. The
missing-binding state is its own artboard for #102 and an inline banner on #100.

## Open items that block full close (need a human, not design)

1. **Migration-run API owner** — the issue flags "⚠️ Confirm the migration-run
   API owner/contract before design." The mockups assume **honua-devops** drives
   the run and surface that in the UI (wizard context bar, run detail). If the
   real owner differs, change the label in `ImportEsriWizard` + `ImportRunProgress`.
2. **`ui-surface-briefs.md` brief + Charter §9 entry** — required by acceptance,
   but they're repo doc updates, not canvas mockups. Draft separately.

## Build notes

- `honua.map-package.v1` is the target schema for #100; dashboard package for
  #101; report content for #104.
- "Create" CTAs wire to `/console/publications` per #100.
- Unbound layers must not silently use mock data — show the binding-required
  state and offer: pick resource / import data first / create draft with layer
  disabled.
- Parity scorecard is the migration record — exported report attaches to the
  migration event in Event Viewer; the run is re-runnable for just the
  failed/unbound items.
