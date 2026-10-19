# Installed Console workspace journey

This lane exercises the current embedded Studio at `/studio` with one native
conversation and real server data. It never starts a source build, mocks a
transport, injects a draft, or enables the retired Studio builders. It requires
an already installed evaluation deployment, a real model configured through the
separate agent identity, and browser storage state from actual human pairing or
sign-in. Keep that state outside version control and CI artifacts.

From `e2e/playwright`, set these inputs and run
`npx playwright test --config playwright.workspace.config.ts`:

- `HONUA_WORKSPACE_E2E_ORIGIN`: the installed Console origin.
- `HONUA_WORKSPACE_E2E_OPERATOR_STATE`: a private Playwright storage-state file
  captured after completing the deployment's normal human authentication flow.
- `HONUA_WORKSPACE_E2E_SERVICE`: an existing published service on the dedicated
  evaluation deployment.
- `HONUA_WORKSPACE_E2E_LAYER`: its numeric layer id, containing renderable features.
  At least one authorized string or numeric field must have two distinct values
  among the first twenty queried features for the thematic styling checks.

The test asks the real native agent to add that source to a uniquely named map,
checks canonical draft readback and features painted on the map, then opens the
visual map style editor through Console. It verifies that the sandboxed Maputnik
UI boots, uses the host's thematic controls with real authorized field values,
asks the native agent to change opacity while preserving those colors, and makes
another visual thematic edit while preserving the agent's opacity change. Saves
flush the actual Maputnik frame. The test checks the canonical style and live map
paint after each change, saves a version, navigates through Console, and reopens
the version with the same styling in the same conversation. It fails on missing
authentication, provider capability or data;
it does not convert those failures into skips. Mutation actions are not retried.

The credential-free receipt identifies the content and saved body. Network
traces and videos are disabled. Run only against a disposable evaluation
deployment: drafts and the saved content item remain available for inspection.

This is one part of the full qualification journey. It does not certify fresh
installation/pairing, imports, shared-style governance, arbitrary edits inside
Maputnik's own controls, publication/audience, crash recovery,
external MCP clients, or artifact provenance. Record exact released artifacts
in the encompassing installation receipt. The older builder tests remain
unqualified and must be replaced by coverage of their current product outcomes;
passing this test alone does not replace those outcomes or complete a milestone.
