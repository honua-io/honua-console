# Live-LLM smoke lane (honua-console#283)

A separate, **non-blocking** nightly lane that exercises **real AI generation** through
the console studio generate-from-prompt surfaces — QUERY, MAP, FORM, WORKFLOW — against
a LOCAL honua-server wired to a real provider. It is the only lane that covers
`prompt -> LLM -> result`; the deterministic live lane can only prove the console's
honest baseline binding (the generation services return `unsupported` with no provider).

LLM output is non-deterministic, so every assertion is **tolerant**: "a coherent generated
result appeared", never exact structure. It runs nightly + on demand, never on the PR gate,
and never against `demo.honua.io` (these specs mutate state).

## Run it

```
npm run e2e:live-llm          # from the repo root — orchestrates the whole lane
```

`e2e/run-live-llm.mjs` resolves a provider, brings up `docker-compose.yml`, starts the
Console bound to the server, runs `playwright.live-llm.config.ts`, tears down, and prints a
per-surface summary (also written to `$GITHUB_STEP_SUMMARY` in CI).

## honua-server provider-support findings (verified against the honua-server checkout)

- **Confirmed providers:** `local` and `openai` (OpenAI-compatible `/chat/completions`),
  `anthropic`, `bedrock`, `azureopenai`, plus `deterministic` (fixture replay).
- **All studio generation families share one config section: `WorkflowGeneration`.** There is
  no separate `FormGeneration`/`QueryGeneration`/`MapGeneration` section. `WorkflowGeneration:Enabled=true`
  turns on **query + map + form + workflow** together.
- **Important asymmetry:** QUERY / MAP / FORM generation call an OpenAI-compatible
  `/chat/completions` endpoint **directly** (they hard-require `Endpoint` + `Model` and send
  `response_format: json_schema, strict:true`). Only **WORKFLOW** generation goes through the
  provider abstraction that natively supports Bedrock/Anthropic/Azure. So to exercise **all four**
  surfaces with one credential you need an OpenAI-compatible endpoint — which is why the Bedrock
  path here runs a **LiteLLM** sidecar that presents `/chat/completions` and translates to Bedrock
  Converse (verified to satisfy the strict `json_schema` requests).
- **Exact config keys** (env form, `:` -> `__`, no prefix):
  - `WorkflowGeneration__Enabled=true`
  - `WorkflowGeneration__DefaultProvider=<local|openai|bedrock|...>`
  - `WorkflowGeneration__Providers__<id>__Endpoint` (base incl. `/v1`; code appends `/chat/completions`)
  - `WorkflowGeneration__Providers__<id>__Model`
  - `WorkflowGeneration__Providers__openai__ApiKey` (Bearer; `local` needs none and allows non-HTTPS)
  - Bedrock: `WorkflowGeneration__Providers__bedrock__Model` + `__Region`; uses the AWS default
    credential chain.
- **"unsupported" state:** with the section disabled, or the selected provider missing `Endpoint`/`Model`,
  every generation endpoint returns `{ "status": "unsupported" }`. The specs detect this and skip
  cleanly instead of failing.

## Configuring the nightly credential (repo secret)

The lane activates a provider from the environment; wire ONE of these as repo secrets on
`honua-io/honua-console` (workflow: `.github/workflows/console-live-llm-nightly.yml`):

**OpenAI-compatible (simplest — all four surfaces, no sidecar):**
- `HONUA_LIVE_LLM_OPENAI_API_KEY` (secret) — the API key.
- optional repo *variables* `HONUA_LIVE_LLM_OPENAI_MODEL` (default `gpt-4o-mini`),
  `HONUA_LIVE_LLM_OPENAI_ENDPOINT` (default `https://api.openai.com/v1`).

**AWS Bedrock (via the LiteLLM sidecar — all four surfaces):**
- `HONUA_LIVE_LLM_AWS_ACCESS_KEY_ID`, `HONUA_LIVE_LLM_AWS_SECRET_ACCESS_KEY` (secrets),
  optional `HONUA_LIVE_LLM_AWS_SESSION_TOKEN`.
- optional variables `HONUA_LIVE_LLM_AWS_REGION` (default `us-west-2`),
  `HONUA_LIVE_LLM_BEDROCK_MODEL` (default `bedrock/invoke/us.anthropic.claude-haiku-4-5-20251001-v1:0`).
  The credential needs `bedrock:InvokeModel` on an inference-profile-backed model. Note the
  `bedrock/invoke/` prefix: it routes via Bedrock InvokeModel (Anthropic tool-use for
  json_schema) rather than Converse native structured output, whose grammar compiler rejects
  the honua QUERY/MAP/FORM strict schemas (`minItems > 1`, "compiled grammar too large").

With neither configured the nightly job still runs and stays green — every spec skips.

## Local overrides

`HONUA_LIVE_LLM_MODE=openai|bedrock|none` forces the mode. Locally, Bedrock mode auto-resolves
credentials from your AWS CLI chain (`aws configure export-credentials`) — no secrets needed.
