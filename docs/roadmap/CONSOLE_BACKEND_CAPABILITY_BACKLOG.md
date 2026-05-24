# Console Backend Capability Backlog

Status: planning draft.

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).

Design sources:

- [Console Canvas Handoff](../design-handoff/console-canvas/README.md)
- [Honua Studio Information Model And Workflows](../architecture/studio-information-model-and-workflows.md)
- [GitOps Metadata Publishing Information Model](../architecture/gitops-metadata-publishing-information-model.md)
- [Temporal Data Viewer Information Model](../architecture/temporal-data-viewer-information-model.md)
- [Operate Observability Information Model](../architecture/operate-observability-information-model.md)

## Purpose

The Console design handoff describes the product surface. This backlog names the backend, SDK, and DevOps capabilities required behind that surface so implementation does not turn into UI-only mock behavior.

The central product constraint is simple: **there is one Honua Console deployable artifact.** Catalog, open data, STAC, DCAT, Studio, Operate, Share, publishing, and administration all live behind that unified runtime.

## Backend Ownership Rules

- `honua-server` owns contracts, persistence, RBAC, validation, execution records, audit, provenance, package publication, service endpoints, and runtime capability discovery.
- `honua-devops` owns GitOps PR authoring, promotion orchestration, deployment evidence, rollback orchestration, and AI DevOps workflow APIs.
- SDK repos own typed projections and helper clients for Console, MCP, QGIS, generated apps, and embeds.
- `honua-console` owns the authoring/operate UI, editor projections, preview orchestration, route-level composition, and use of server-authored policy decisions.

## Capability Crosswalk

| Console promise | Backend capability needed | Existing coverage | Gap / action |
| --- | --- | --- | --- |
| One deploy surface for Catalog, Share, open data, STAC, DCAT, Studio, and Operate | Shared content item, publication, sharing, catalog endpoint, route, RBAC, and audit model | `honua-console#4`, `#7`, `#8`, `#9`; `honua-server#1162`; `honua-sdk-js#225`; `honua-sdk-dotnet#166` | Keep STAC/DCAT/open-data parity inside Console gates. Do not create a Portal-owned backlog lane. |
| AI natural language to spatial query | Saved query package, source binding discovery, field/CRS validation, permission check, cost estimate, preview execution, result persistence | Partial through metadata/content baseline and query APIs | File/attach server work for query package validation, preview planning, and saved query content versions. |
| AI natural language to spatial analysis | Analysis package, parameter validation, runtime estimate, queued execution, result artifact, rerun provenance | `honua-server#681`, `#721`, `#724`, `#1170` partially cover execution substrate | Add Studio-facing analysis package API and result artifact contract if not folded into GP/process work. |
| AI map/dashboard/report/app publishing | Package validators, data binding validators, publication records, route/share/embed policy, dependency graph, rollback pointer | `honua-console#16`; `honua-sdk-js#225`, `#226`; `honua-server#1162` | Server needs explicit package lifecycle/publish endpoints for map, dashboard, report, app, and generated app reopen flows. |
| Vega-Lite dashboard/report charts | Vega-Lite spec validation, data binding/aggregation contract, preview data endpoint, publication dependency checks | Standard chosen in Studio architecture docs | Add package validator rules and SDK projections for chart data bindings and generated Vega-Lite specs. |
| Survey-style form builder | Form package, field/domain rules, conditional visibility, attachments, offline policy, submission target, sync policy | `honua-server#1158` covers back-office parity; temporal/sync tickets cover later conflict review | Add form package/submission/offline policy API if `#1158` only ports legacy admin behavior. |
| Unified GP/ETL editor | Workflow DAG package, node registry, parameter schema, dry run, schedule, worker profile, job runner publish, eligible GP/process endpoint publication | `honua-server#360`, `#361`, `#682`, `#681`, `#721`, `#724`; `honua-console#17` | Consolidate old GP/ETL research/issues into one Studio workflow package contract and execution API. |
| GitOps metadata publishing across environments | Semantic resource IDs, environment bindings, release package, compatibility prevalidation, data script coverage, Git operation, release lifecycle, rollback | `honua-server#1163`, `#1164`, `#1165`; `honua-devops#57`, `#58`; `honua-console#22` | Active lane. Console must visualize the same release operation IDs produced by server/devops. |
| Temporal data viewer and "git over data" history | Capability discovery, checkpoints, as-of read, diff, attribution, rollback plan/job, feature timelines | `honua-server#1166`; `honua-sdk-js#227`; `honua-console#23` | Ensure temporal support is optional and surfaced through capability flags, not assumed by UI. |
| Disconnected sync conflict review | Named replica metadata, base/client/server diff, conflict queue, resolution job, audit evidence | `honua-server#1167`; `honua-sdk-js#228`; `honua-console#23` | Align names with Esri sync concepts without making a separate Esri-only model. |
| Operate event viewer, logs, telemetry, alerts, jobs | Normalized operational event query, server telemetry status, log/evidence links, alert/rule APIs, job run API | `honua-server#1168`, `#1169`, `#1170`; `honua-sdk-js#229`; `honua-console#24` | Active lane. AI summaries remain evidence-linked and advisory. |
| Realtime/geofence alerting | Rule model, spatial zone model, stream/event source, delivery channel status, dead-letter/retry state | `honua-server#393`, `#339`, `#1169` | Tie geofence rules into Operate event/alert model and job/stream evidence. |
| Native Console multi-environment/mTLS | Environment profile, advertised transport capabilities, native gRPC streaming, client certificate trust profile | `honua-server#1171`; `honua-console#26` | Web Console must render native-only capability states without depending on MAUI. |
| MCP/QGIS parity | Same package contracts, capability discovery, validation responses, preview/publish operations exposed through SDK/MCP | `honua-sdk-js#226`, `#227`, `#228`, `#229` | Do not fork artifact schemas for Console, MCP, QGIS, or generated apps. |

## Filed Backend And SDK Issues

These were filed as GitHub issues and projected from Specifica items under `agent-delivery-spec/.specifica/`.

| Priority | Issue | Specifica slug | Why it matters |
| --- | --- | --- | --- |
| P0 | [honua-server#1180](https://github.com/honua-io/honua-server/issues/1180) | `studio-package-lifecycle-api-for-console-authoring` | Console needs one server-owned way to save drafts, validate packages, create content versions, publish, reopen, and roll back generated artifacts. |
| P0 | [honua-server#1181](https://github.com/honua-io/honua-server/issues/1181) | `package-validation-and-preview-planning-api` | Studio, MCP, QGIS, and SDK clients need the same validation output for data bindings, permissions, schema, CRS, capability support, estimated cost, and publish blockers. |
| P1 | [honua-server#1182](https://github.com/honua-io/honua-server/issues/1182) | `saved-query-and-analysis-content-versions-with-job-artifacts` | Natural-language query/analysis should produce durable content and reusable outputs, not one-off preview state. |
| P1 | [honua-server#1183](https://github.com/honua-io/honua-server/issues/1183) | `map-dashboard-report-and-app-publication-registry` | Publishing needs durable route, visibility, embed policy, dependency, provenance, and rollback records across generated artifacts. |
| P1 | [honua-server#1184](https://github.com/honua-io/honua-server/issues/1184) | `form-package-submission-attachments-and-offline-policy-api` | Survey-style form workflows need field rules, conditional visibility, domains, attachments, target writes, and offline behavior governed by the server. |
| P1 | [honua-server#1185](https://github.com/honua-io/honua-server/issues/1185) | `unified-gp-etl-workflow-package-and-node-registry` | The GP/ETL editor needs a shared node/parameter/runtime contract before UI graph editing can be meaningful. |
| P1 | [honua-server#1186](https://github.com/honua-io/honua-server/issues/1186) | `console-capability-manifest-endpoint` | Console, MCP, QGIS, and native hosts need one way to discover supported package families, temporal support, sync support, realtime support, transports, and policy limits per environment. |
| P1 | [honua-sdk-js#230](https://github.com/honua-io/honua-sdk-js/issues/230) | `sdk-js-studio-package-family-projections-and-validation-responses` | Browser interop, MCP, QGIS, generated apps, and embeds need typed contracts from the same source. |
| P1 | [honua-sdk-dotnet#169](https://github.com/honua-io/honua-sdk-dotnet/issues/169) | `sdk-dotnet-console-studio-package-clients-and-validation-responses` | Blazor Console and optional MAUI host need typed .NET clients without duplicating server DTOs. |
| P1 | [honua-devops#59](https://github.com/honua-io/honua-devops/issues/59) | `console-facing-ai-devops-operation-bridge` | Console needs a stable API for AI DevOps to create GitOps proposals, show evidence, and monitor PR/promotion/rollback state without scraping CI or Git. |

## Execution Order

1. Land the metadata/content/RBAC baseline and SDK projections.
2. Define the package lifecycle and validation/preview APIs.
3. Wire Studio query, analysis, map, dashboard, report, app, and form packages to content versions.
4. Wire GP/ETL workflow packages to job-runner execution and eligible process endpoints.
5. Complete GitOps release lifecycle, compatibility prevalidation, operation monitoring, and rollback.
6. Complete Operate observability, realtime/geofence rules, and jobs.
7. Add temporal/as-of/diff/rollback and disconnected sync conflict review where sources declare support.
8. Add native Console capability discovery, gRPC streaming, and mTLS profiles.

## Parity Gate Additions

The existing Console parity gate should be expanded to prove:

- Open data, STAC, and DCAT publication are served by the unified Console runtime.
- A generated Studio map, dashboard, report, form, app, query, analysis, and workflow can each be saved as a content item/version and reopened.
- A package validation response is identical in shape across Console, MCP, QGIS, and SDK clients.
- A Studio publication creates job, event, audit, provenance, publication, and content-version records linked by stable IDs.
- A rollback or republish uses server-owned records rather than UI-local state.
