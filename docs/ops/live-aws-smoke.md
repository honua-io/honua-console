---
type: guide
title: Live AWS browser smoke
---

# Live AWS browser smoke

The scheduled and manually dispatched `.github/workflows/console-aws-browser.yml`
workflow currently records a **SKIPPED** notice. The first Launch Stack gate
install is not available. No browser journey runs, and a successful preflight is
not a live qualification pass. The workflow does not emit a `live-aws-browser`
check or browser evidence.

The retired smoke ran a non-shipping legacy host against a deployment that did
not serve the static Console. Repository operator username/password secrets are
no longer used by this workflow.

Before enabling browser qualification, connect it to a gate install with:

- The candidate server image and published static Console, with their identities
  recorded in the evidence.
- The stack's Cognito pool and a test operator created by the stack, without
  long-lived repository operator credentials.
- Shipping Console journeys at 1280 and 390 widths, against that installed
  Console rather than the legacy fixture.
- Failing assertions and required evidence uploads on a provisioned gate.

The local shipping replay remains documented in
[Shipping live replay](../testing/SHIPPING_LIVE_REPLAY.md). It does not replace
AWS/Cognito qualification.

Run the workflow contract tests with
`node --test tests/workflows/live-aws-browser-smoke.test.mjs`; they also run in
`npm test`.
