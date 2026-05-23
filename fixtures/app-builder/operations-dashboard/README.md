# Operations Dashboard App-Builder Fixtures

These descriptors are the model-free proof set for `honua-console#5` (ported
verbatim from `honua-portal#50`). They are Console-side fixtures, not server or
SDK contract copies. The smoke harness consumes SDK-JS controller and
exploration runtime surfaces (`@honua/sdk-js/operator`,
`@honua/sdk-js/exploration`), while these files provide deterministic inputs
and expected evidence IDs for CI, demos, and release validation.

- `success.json` covers prompt, clarification, spec/plan review, apply,
  generated preview, direct edit, private publish, and reopen. It also flags
  `runtime.chartSpec: "vega-lite"` so the smoke harness can assert the
  Vega-Lite adapter branch introduced in the Console port.
- `unsupported-capability.json`, `auth-denial.json`,
  `oversized-estimate.json`, `missing-binding.json`, and
  `apply-failure.json` cover the named failure states required by the proof.

All fixtures must keep `"mode": "model-free"` and must not name a live model
provider. If the production route later consumes server-seeded fixtures, these
IDs should remain stable so evidence manifests can be diffed across runs.

## Descriptor Contract

The success descriptor must include the complete deterministic path: `prompt`,
`source`, `intent`, `clarificationAnswers`, `clarifiedIntent`, `spec`, `plan`,
`execution`, `mapPackage`, `appPackage`, `edit`, and `publish`. The `spec.widgets`
set is intentionally narrow for this proof and must contain the five runtime
widget kinds exercised by the harness: `map`, `list`, `indicator`, `chart`, and
`filter`.

Each failure descriptor must include `failure.code`, `failure.stage`,
`failure.expectedSurface`, owner attribution, `canRetryWithoutModel`, and a
stable `evidence.selector` that starts with `failure-`.

See `docs/studio/PORT.md` for the Studio port's source mapping, reframing
rules, and acceptance gates. A shared `app-builder-proof/v1` contract doc will
land alongside the `@honua/sdk-js` shared contract work (honua-sdk-js#225); the
Portal-side `docs/contracts/app-builder-proof-v1.md` is the historical
reference until then.
