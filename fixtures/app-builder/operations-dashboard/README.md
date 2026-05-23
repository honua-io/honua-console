# Operations Dashboard App-Builder Fixtures

These descriptors are the model-free proof set for `honua-portal#50`.
They are intentionally portal-owned descriptors, not server or SDK contract
copies. The smoke harness consumes SDK-JS controller and exploration runtime
surfaces, while these files provide deterministic inputs and expected evidence
IDs for CI, demos, and release validation.

- `success.json` covers prompt, clarification, spec/plan review, apply,
  generated preview, direct edit, private publish, and reopen.
- `unsupported-capability.json`, `auth-denial.json`,
  `oversized-estimate.json`, `missing-binding.json`, and
  `apply-failure.json` cover the named failure states required by the proof.

All fixtures must keep `"mode": "model-free"` and must not name a live model
provider. If the production route later consumes server-seeded fixtures, these
IDs should remain stable so evidence manifests can be diffed across runs.

## Descriptor Contract

The repository contract for these descriptors and their smoke evidence manifests
lives in
[`docs/contracts/app-builder-proof-v1.md`](../../../docs/contracts/app-builder-proof-v1.md).

The success descriptor must include the complete deterministic path: `prompt`,
`source`, `intent`, `clarificationAnswers`, `clarifiedIntent`, `spec`, `plan`,
`execution`, `mapPackage`, `appPackage`, `edit`, and `publish`. The `spec.widgets`
set is intentionally narrow for this proof and must contain the five runtime
widget kinds exercised by the harness: `map`, `list`, `indicator`, `chart`, and
`filter`.

Each failure descriptor must include `failure.code`, `failure.stage`,
`failure.expectedSurface`, owner attribution, `canRetryWithoutModel`, and a
stable `evidence.selector` that starts with `failure-`.
