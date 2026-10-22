---
type: reference
title: "Unified agent workspace host"
description: "Console and embedded Studio workspace composition, credential boundaries, and deployment requirements."
---
# Unified agent workspace host

The Console host shares one server-owned workspace and one native SDK agent session with the realtime Studio custom element. Enable `Studio:EmbedJsStudio=true` to render the pinned Studio element at `/studio` and `/studio/drafts`; the legacy standalone builders are retired and cannot be restored by a capability flag.

Studio assets are served from `wwwroot/_studio/console.js` and `style.css`. The production entry must export `registerAllStudioElements`, `createWorkspaceAgentSession`, and `createWorkspaceTranscriptVerifier`; it must not contain the Blazor harness fixture session or automount logic. Runtime CDN loading is unsupported.

The browser authenticates with the Console cookie. `/api/agent-workspace/session` returns an antiforgery token and the selected environment identity, never a server token. All mutation requests carry `X-CSRF-Token`; workspace and Studio transport requests also carry `X-Honua-Environment` and the captured opaque `X-Honua-Session-Partition`. The partition binds the human principal and the complete selected upstream authority, including its path. `sourcePathBase` exposes only that verified path for authenticated map-source transport; persisted canonical source URLs remain unchanged. The BFF resolves and refreshes the operator bearer server-side. It does not use an administrator key, relay redirects, or forward arbitrary browser headers. Unknown paths and query keys fail closed.

The optional native agent additionally requires `HONUA_AGENT_API_KEY`, `HONUA_AGENT_RELEASE_ID`, and `HONUA_AGENT_SERVER_BASE_URL` (defaults to the configured Honua server URL). This key must identify a separately authorized agent principal with explicit workspace membership. It is never sent to the browser or to another environment profile's server. Human membership is checked before the agent transport is accessed; the upstream AI and MCP requests use only the dedicated agent credential. External MCP clients remain independent and can attach to the same workspace.

Prompt submission captures a view revision in the durable server turn before any model request. The model receives a fresh projection of that frozen turn fetched under the agent principal; human-authorized context is not copied into the model. The SDK verifies signed model transcripts using the selected server's authenticated capability signing manifest, a replay store and the configured release/tenant/turn binding. Missing membership, signing keys, release binding or provider capability prevents native execution.

Each document gets a unique view ID, including duplicated browser tabs. Workspace identity persists in session storage. Navigation transmits typed layer references only, without URLs, query strings, page contents or credentials. Pending turns retain their captured context after navigation. Suggested presentation routes are allowlisted. The follow-navigation toggle controls whether an authorized task opens its form; unsaved shared fields pause presentation.

The publish-layer form shares only `connectionId`, `schema`, `table`, `serviceName` and `layerName`. Server snapshots update untouched fields; local unsaved fields survive refresh and conflict responses. Saves send field patches with the authorized draft revision. Publication uses the canonical server draft-submit endpoint and persists its command ID before transport, so retry after a lost response reconciles the same command. Accepted turns, conversation text and context events do not establish publication success.

The backend contract is `/api/v1/agent/workspaces`: create workspace, read a per-view snapshot, update context with an expected revision, create/read/patch typed drafts, submit a draft, append/read conversation, and create/read immutable turns. Runtime integration requires the matching server implementation; missing routes are surfaced as unavailable rather than simulated success.

Processes uses bounded human transport under `/api/processes`. Built-in graphs save immutable workflow-package versions, validate with the existing compiler and run through the server's workflow publication pipeline. Custom source/run calls name the actual workspace and use the human bearer through `/api/v1/agent/workspaces/{workspaceId}/processes/local`. The Console does not receive a daemon key. The server owns the configured private daemon origin and credential; binding and revocation are human-only. Imported outputs use ordinary multipart import and connection-layer publication. A layer is added to Studio only after a real publication receipt resolves to one unique live catalog source and the existing composition orchestrator accepts it.

Refresh the managed Studio assets with `node scripts/vendor-studio-assets.mjs BUILD_DIRECTORY STUDIO_COMMIT_SHA SDK_COMMIT_SHA`. The vendor manifest records every asset's SHA-256 and both exact source revisions. The build must include the SDK's schema-aware canonical tool dispatch; the published SDK 0.2.2 predates that fix and is insufficient for the native broad-tool session.

The opt-in Docker local human bridge requires Console `Honua__Console__Auth__Mode=Dev`, `Honua__Console__Auth__LocalHumanBridge__Enabled=true`, `ASPNETCORE_ENVIRONMENT=Development` and host-only `HONUA_LOCAL_HUMAN_BRIDGE_KEY`. Server configuration requires `AgentWorkspace__LocalHumanBridge__Enabled=true`, the independent bridge `Key`, a fixed `TenantId`, and the existing `Authentication__OperatorBearer` enabled with its own server-only signing key. The exchange is restricted to the configured server origin and cookie principal. It produces a ten-minute human bearer stored only in the operator/profile session. The agent key and transport never participate in this exchange; production environments do not expose the exchange endpoint.

Studio task bindings use `{id,revision,kind:'studio-composition',packageKey}` from the authorized package-draft lifecycle response. The host projects that exact draft into the canvas and forwards successful human generation changes into the shared context. The next native turn attaches its frozen agent-authorized draft reference; later canvas edits cannot retarget an active model turn. Lifecycle generation conflicts remain visible and do not trigger overwrite retries. The human transcript projection is display-only and never establishes operation receipts or executes tools.

The optional native infrastructure extension uses `HONUA_AGENT_GATEWAY_URL` (a host-pinned HTTP(S) origin ending in `/`) and independent `HONUA_AGENT_GATEWAY_KEY`. Both must be configured together; partial or invalid configuration fails closed. MCP then uses that same persistent gateway composition as external clients. AI and workspace APIs still use the actual server with the separate agent key. Each gateway request receives a host-derived opaque operator partition and the freshly captured workspace/turn headers. The BFF verifies the gateway's capability manifest against a separately agent-authorized turn before advertising `honua.workspace.devops`; neither browser headers nor tool-name prefixes grant this family. Without the optional bridge, native chat continues to use the server's canonical MCP surface.

Before capturing a Studio prompt, the host waits for accepted human draft writes and binds their observed generation without refreshing away unsaved controls. Idle canvas refreshes observe external changes only when Studio's dirty/write guards allow them. An active turn always retains its submitted draft and resource selection.

Studio exposes an explicit New map action that creates a canonical server draft before canvas editing. The accepted draft generation binds the next prompt. Processes captures only server-issued immutable package identity, version and content hash; custom code binds only an actual authorized project identity, revision and source digest. The server captures an immutable source snapshot when the prompt is submitted; later source edits cannot change that turn. Dirty graph text prevents package projection or prompt capture. Human OGC reads allow only one collection items endpoint with JSON format and limit at most 2000. FeatureServer rendering permits only JSON or GeoJSON.

The embedded Studio also exposes **Save version** and **Saved versions** above the canvas. Saving waits for accepted visual edits and creates an immutable version through the current human session. Selecting **Reopen as draft** opens a saved version of that map as a new shared draft and updates the conversation context. These actions do not publish. Pending forms or conflicts must be resolved first; navigation, a changed map or changed user authority prevents a delayed response from replacing the active view. Failed saves are not retried automatically. If the connection drops, inspect saved versions before explicitly saving again.

Native browser requests carry an opaque operator/profile partition from the cookie session. The BFF checks the current host identity before sending any agent credential. A principal or environment change closes the SDK session; ordinary draft detachment retains its conversation. Delayed polling cannot replace a newer view revision, and completed turns do not clear a newly typed prompt.

Native AI and MCP event streams pass through incrementally after membership and captured-turn authorization, with request cancellation propagated upstream and caching disabled. Capabilities and gateway binding metadata are bounded before verification. Run discovery uses the canonical workspace run list and command receipts; assistant text is never parsed into an operation identity.

A pending custom-source save may replace its own cleared task only after the same editor observes the accepted project revision and source digest. Capture rejects changed view, project, mode, explicit context selection, workspace, principal or environment while that save is pending. Unsaved source remains a blocking condition; capture never reads the latest source as a fallback for an already submitted snapshot.

The native bundle must advertise `workspaceAgentFeatures.toolApproval === true`. Each serialized turn clears the previous capability verdict, revalidates same-origin session metadata, reconnects discovery and refreshes the actual canonical tool catalog on the persistent SDK session. Known family selection includes `honua.workspace.local-process`; optional private DevOps tools additionally require that turn’s verified gateway capability pins. Discovery failure exposes no stale tools and never replays mutations.

### Experimental GA spec review

`Honua:Agent:GaSpecWorkflowEnabled` defaults to false. It exposes a review in the
existing host conversation for canonical saved layer draft planning and revision-pinned
layer publication plans from
`/v1/spec/ga`. Opening a plan never applies it. Human Apply uses the existing
operator/environment/origin-partitioned honua-server cookie session, with browser
antiforgery validation; it does not forward the model's or operator bearer token.
A missing or expired server human session fails closed and requires server sign-in.
The server must also enable `GaSpec:Enabled`. The bounded bundle reader consumes
the shared server bundle version and grammar; it maintains no second operation catalog.

The review displays server inputs, attribution, verdict, execution and evidence.
An accepted Apply response is followed by an authorized durable run read. The
Read durable status button recovers the stored run after reconnect; event frames
and assistant text cannot establish success. A lost Apply response preserves the
command identity and blocks blind replay. The server uses plan ID as the durable run identity, so status reads can reconcile
a lost response without repeating Apply. A missing record remains unknown and
blocks replay.
Plan IDs and revisions entered from external agents use the same review surface.

This slice does not qualify a connected production journey. Exact feature selection,
canonical workspace context availability, shared draft undo, bundle-derived pathways and acceptance
read-back remain qualification work; existing explicit Save and draft lifecycle stay
in the shared Studio editor.

The current server prerequisite adapter fails closed with `workspace-context-unavailable`
until the canonical workspace backend is integrated. This is an explicit GA blocker.

### Checked Studio filtering bundle

The managed assets include Studio `ac42da061a6ff44bc78b4c9043978711adf9ee81`:
shared source filtering for maps, charts and tables, comparable ISO date ranges,
and preserved half-open date inputs. They were imported with the normal vendor
script from the successful [Production Console assets + audience browser job](https://github.com/honua-io/honua-studio/actions/runs/39857730332/job/120100212717).
The producer passed three browser checks; the same source passed hosted typecheck,
style, build and all 1,561 unit tests.

GitHub artifact `12461370183`, named
`console-studio-assets-157d95c382932ef05ce774313fdaf4f2a81e3cee`, was downloaded and
its archive SHA-256 verified against GitHub's digest:
`febf36086ab8bedc893061d052032f31aefc637c99b5d813f4139f3fa0d017dc`.
The PR job checked out synthetic merge
`157d95c382932ef05ce774313fdaf4f2a81e3cee`; its Git tree
`f38d55e170a81d10a5f4ccda76470afac234d136` exactly matches the Studio source commit
recorded in the vendor manifest.

The bundle's SDK provenance matches the source's pinned public `@honua/sdk-js`
`0.2.7` receipt and source commit `5765b427f421dca9c42832ae450fa1c5aad882c2`.
Registry `gitHead`, archive SHA-256
`33b2c0f7ca70692fd621d8bbdcc886dc83f464d53513092d40e6e43c24396f06`, and lockfile
integrity were independently verified. No SDK revision was inferred from a local
checkout. The vendor manifest checks the copied file set, sizes and hashes.

These checks do not establish a connected installed-workspace journey or full
AI-03 qualification. Generated provenance retains `releaseQualified: false` and
`pending-installed-workspace-journey`.
