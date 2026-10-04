---
type: guide
title: Live AWS browser smoke
---

# Live AWS browser smoke

The scheduled and manually dispatched `.github/workflows/console-aws-browser.yml`
workflow runs the `live-aws-browser` check against `https://demo.honua.io`.

Before checkout, tool installation, or publishing, `operator-preflight` checks
whether both `HONUA_CONSOLE_AWS_OPERATOR_USERNAME` and
`HONUA_CONSOLE_AWS_OPERATOR_PASSWORD` repository secrets are present. If either
is absent, `live-aws-browser` concludes **skipped**. The preflight notice and job
summary name both secrets and direct the operator to configure both for a
provisioned AWS operator, then rerun the workflow. This is an explicit missing
precondition, not successful browser certification.

With both secrets present, the existing publish, AWS-bound journeys, and evidence
upload run unchanged. Invalid credentials, backend errors, browser assertions,
and missing evidence still fail; only absent operator secrets permit a skip.
The check name remains `live-aws-browser` for the release environment-gated
classification in `honua-release/certification/env-gated-checks.yaml`.

Run the workflow contract tests with
`node --test tests/workflows/live-aws-browser-smoke.test.mjs`; they also run in
`npm test`.

Refs honua-io/honua-release#376
