# Live-LLM smoke lane (honua-console#283, retargeted in #1047)

A separate, **non-blocking** nightly lane that exercises **real AI inference** through the
Console's `/support` GIS assistant. It is the only Console surface that still calls a model:
the self-service panel sends the conversation to an OpenAI-compatible
`/v1/chat/completions` endpoint configured by `HONUA_LLM_BASE_URL`, `HONUA_LLM_MODEL` and
`HONUA_LLM_API_KEY`.

The lane used to call the honua-server studio generate-from-prompt routes (QUERY, MAP, FORM,
WORKFLOW). ADR-0076 (honua-server 834fec8f31) retired that server-side generation in favour of
client-side inference over composition operations. Three of those routes no longer exist, and
`/api/v1/studio/map-packages/generate` now creates drafts deterministically. The lane therefore
runs no honua-server. Client-side studio generation is evaluated by the sdk-js
`mcp-eval-live` lane.

Model output is non-deterministic, so the answer check is **tolerant**: a substantive reply
appeared. A missing assistant panel or an error reply fails the lane once a provider resolved.
The lane runs nightly and on demand, never on the PR gate.

## Run it

```
npm run e2e:live-llm          # from the repo root — orchestrates the whole lane
```

`e2e/run-live-llm.mjs` resolves a provider, starts the LiteLLM gateway in Bedrock mode
(`docker-compose.yml`), starts the Console with the `HONUA_LLM_*` settings, runs
`playwright.live-llm.config.ts`, tears down, and prints the surface summary (also written to
`$GITHUB_STEP_SUMMARY` in CI).

## Configuring the nightly credential

The lane activates a provider from the environment; wire ONE of these on
`honua-io/honua-console` (workflow: `.github/workflows/console-live-llm-nightly.yml`):

**OpenAI-compatible (simplest, no containers):**
- `HONUA_LIVE_LLM_OPENAI_API_KEY` (secret) — the API key.
- optional repo *variables* `HONUA_LIVE_LLM_OPENAI_MODEL` (default `gpt-4o-mini`),
  `HONUA_LIVE_LLM_OPENAI_ENDPOINT` (default `https://api.openai.com/v1`).

**AWS Bedrock (via the LiteLLM sidecar):**
- repo *variable* `HONUA_LIVE_LLM_BEDROCK_ROLE_ARN`: the GitHub OIDC role from honua-iac
  `bootstrap/aws-ci-oidc-roles` (`honua-console-gha-bedrock-live-llm`). The job runs in the
  `bedrock-live-llm` environment, which the role trusts, and gets short-lived credentials;
  no access key is stored. Locally, any AWS credential chain in the environment works.
- optional variables `HONUA_LIVE_LLM_AWS_REGION` (default `us-west-2`),
  `HONUA_LIVE_LLM_BEDROCK_MODEL` (default `bedrock/invoke/us.anthropic.claude-haiku-4-5-20251001-v1:0`).
  The role grants `bedrock:InvokeModel` on Anthropic models and US inference profiles, which is
  why the default routes through `bedrock/invoke/` (InvokeModel) rather than Converse.
- The orchestrator generates a per-run LiteLLM master key and hands the same value to the
  Console as `HONUA_LLM_API_KEY`, so only this run's Console can spend the credential.

With neither configured the nightly job still runs and stays green — the spec skips.

## Local overrides

`HONUA_LIVE_LLM_MODE=openai|bedrock|none` forces the mode. Locally, Bedrock mode auto-resolves
credentials from your AWS CLI chain (`aws configure export-credentials`) — no secrets needed.
