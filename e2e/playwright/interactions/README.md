# Installed Console interaction checks

Release owns the FOSS4G scenario inventory, prompt variants, schedule and qualification
report. These tests consume its generated Console projection; there is no Console
scenario corpus or qualification reporter.

Generate the A/B projection from the reviewed Release checkout:

```sh
python3 "$HONUA_RELEASE_CHECKOUT/tools/agent_e2e_pilot.py" export \
  --entrypoint console --output /tmp/foss4g-console-projection.json
export HONUA_FOSS4G_PROFILE_PROJECTION=/tmp/foss4g-console-projection.json
```

Required deployment inputs:

| Variable | Requirement |
| --- | --- |
| `HONUA_WORKSPACE_E2E_ORIGIN` | Installed Console HTTP(S) origin without path or credentials |
| `HONUA_WORKSPACE_E2E_OPERATOR_STATE` | Private Playwright storage-state file for an actual signed-in/paired human |
| `HONUA_WORKSPACE_E2E_SERVICE` | Existing published service in an isolated evaluation workspace |
| `HONUA_WORKSPACE_E2E_LAYER` | Numeric layer ID within that service |
| `HONUA_WORKSPACE_E2E_SERVER_SHA` | Server commit from the reviewed deployment manifest |
| `HONUA_WORKSPACE_E2E_STUDIO_SHA` | Studio commit from that manifest; linked-filter check requires the candidate filtering implementation bundled into Console |
| `HONUA_WORKSPACE_E2E_MODEL_ID` | Verified configured model deployment identifier from that manifest |
| `HONUA_FOSS4G_PROFILE_PROJECTION` | Release-generated Console projection path |
| `HONUA_FOSS4G_VARIANT` | Development variant `A` by default; `B` only when authorized by the Release schedule; held-out `C` is rejected |
| `HONUA_WORKSPACE_E2E_CATEGORY_FIELD` | AI-03 probe only: actual string category field with at least two nonempty values |

The human must have access to real workspace context, canonical Studio drafts,
source queries, and the native agent with transcript verification configured.
Console `/version.json` must expose its actual commit. The other artifact values
are recorded as deployment-manifest expectations, not independently observed
installation provenance.

The fixture must contain actual rendered features. The visual roundtrip needs a
string or numeric field containing two distinct observed values. The dashboard
probe needs at most 50 complete rows, an object-ID field, and the configured string
category field. Its table must display all fixture rows in the requested field
order. Every case creates real unique map drafts; preserve those drafts for failure
inspection and clean them through the evaluation environment's normal lifecycle.

After prerequisites and execution budget are verified by the controller:

```sh
npm run e2e:interactions
```

Use Playwright `--grep` through the underlying package script for a scheduled subset.
The config inherits installed-workspace authentication, runs one worker with zero
retries, and disables screenshots, traces and videos. Missing prerequisites fail;
there are no transport mocks, injected drafts, artificial delays or fallback agents.

Implemented supporting checks:

- `[IX-01:selected-map]`: natural selected-map/layer reference, exact authorized
  selection, canonical mutation, rendered layer and unchanged distractor draft.
- `[IX-04:visual-agent-visual]`: visual category style, natural prompted opacity,
  another visual edit, independent canonical/live paint checks and unchanged distractor.
- `[IX-06:navigate-midturn]`: actual human New map action switches A to B while
  the same captured turn is busy before and after the switch; A changes, B stays
  unchanged, and the next-request context remains B. A turn completing too quickly
  fails this timing condition rather than claiming navigation coverage.
- `[AI-03:map-dashboard]`: supporting map-family dashboard fragment, exact chart
  aggregates and table rows, linked map/chart/table filtering and restoration, and
  saved-version reopening. This does not qualify the full publication/approval journey.

Effects are established through authorized canonical draft reads and rendered data,
not assistant text or the generic conversation status. Completion also requires an
assistant record for the exact persisted turn and an idle real session. Successful
cases attach bounded credential-free `interaction-evidence`; attachment presence
alone does not establish release qualification. These candidate tests have not yet
been executed against a connected deployment.
