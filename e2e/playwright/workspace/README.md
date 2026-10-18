# Unified workspace browser qualification

Run against a dedicated deployed candidate with the matching Console, server, Studio assets and real model provider. This lane starts no server, enables no shelved builders, mocks no route and retries no model turn. Unconfigured native execution fails the run.

```sh
HONUA_WORKSPACE_E2E_ORIGIN=http://localhost:3000 \
HONUA_WORKSPACE_E2E_AUTH=local \
npm --prefix e2e/playwright exec -- playwright test --config e2e/playwright/playwright.workspace.config.ts
```

`local` uses the candidate's explicitly enabled local human bridge. `oidc` reuses `live/operator-session.ts` and its `HONUA_CONSOLE_E2E_OPERATOR_*`/IdP settings. `storage` requires `HONUA_WORKSPACE_E2E_STORAGE_STATE` from a prior real human sign-in. No service/agent/bridge secret is supplied to the browser harness. Authentication and model requests are not traced. The native server/gateway/provider remains configured only in the deployed host. Use the canonical shared build lock for dependency install and browser execution.

The implemented journey proves real human session availability, workspace creation/membership, human-created canonical Studio draft binding, primary Console conversation across Console and Studio views through one SDK session, absence of the embedded duplicate composer, durable transcript after navigation, human dirty form preservation during authorized observation, and an available approval inbox. It records only binding references, not credentials, prompt output or model claims of execution.

Epic 6297 acceptance still requires these additional real journeys:

- External-agent human pairing/revocation and agent context filtering, with a second restricted principal. Existing `live/operator-session.ts` proves OIDC exchange, not external pairing. The new workspace UI currently has no pairing controls.
- Agent draft mutation followed by actual lifecycle GET/generation advance and canvas projection; human canvas edit followed by an agent-authorized next-turn reference. The benign chat test deliberately avoids claiming this proof.
- Data connection/import → canonical service publication → map source → Studio publication → audience authorization. Reuse `live/specs/services-layers.live.spec.ts` for independent catalog, metadata and feature query assertions, and the audience/receipt helpers in `live/console-receipt-browser.mjs`. Those existing specs cover a separate lane and do not yet establish the unified-workspace chain.
- A genuine native mutation must produce a server approval proposal, appear in the inbox, require an explicit human action, and reconcile the canonical operation receipt after approval (including lost response). `ProposalApprovalPanel` exposes stable `data-proposal-id`, `data-proposal-approve`, `data-proposal-reason` and action-message hooks; `live/console-receipt-browser.mjs` already drives actual proposals. No synthetic proposal or assistant-text operation ID should be used.
- Simultaneous tabs, navigation during a pending real native turn, hostile agent resource selection, principal/environment changes, and interrupted-stream/restart reconciliation against actual HTTP services. Current focused unit/integration tests cover these boundaries but do not replace deployed browser evidence.
- Built-in and custom Processes repair/run/artifact → layer → same Studio draft, including retained immutable snapshots, dirty-editor conflict, command reconciliation and real failed-run diagnostics.

The legacy `playwright.live-llm.config.ts` exercises shelved generators and skips unsupported/error results. It is not an acceptance gate for this workspace. No live acceptance result is claimed until this new lane runs against the actual provisioned candidate.
