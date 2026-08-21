# Candidate-bound Console receipt producer

The producer runs only at the SDK's `console-approval` pause. It validates the
checkpoint digest and the sealed paused Studio real-model handoff (a paused live
SDK receipt remains accepted for the direct SDK path), joins exact connection,
service, layer, GP, and Studio identities, then selects the unique map, app, and
dashboard proposals whose server-owned review evidence contains those identities.
It never accepts proposal IDs, publication IDs, or pass/fail claims from command
line flags.

Use a bearer limited to read access plus `admin:approve`. Supply it only through
`HONUA_AI_ARC_CONSOLE_TOKEN`; `HONUA_ADMIN_KEY` and `HONUA_API_KEY` are refused.
The credential is sent only to the configured same-origin candidate endpoint and
is never written to stdout, receipts, temporary files, or public-route requests.

```sh
HONUA_AI_ARC_ENDPOINT=http://127.0.0.1:8080 \
HONUA_AI_ARC_CHECKPOINT=out/checkpoint.json \
HONUA_AI_ARC_REAL_MODEL_EVIDENCE=out/studio-real-model-evidence.json \
HONUA_AI_ARC_CONSOLE_RECEIPT=out/console-release.json \
HONUA_AI_ARC_SDK_CONSOLE_RECEIPT=out/console-sdk.json \
HONUA_AI_ARC_CONSOLE_TOKEN='<scoped bearer>' \
npm run receipt:console
```

For AWS, `HONUA_AI_ARC_ENDPOINT` must be HTTPS. Local Docker accepts HTTP or
HTTPS. `HONUA_CONSOLE_MODE=full` approves pending proposals; `witness` never
mutates and passes only when the exact proposals were already resolved.

Two outputs are intentional. `HONUA_AI_ARC_CONSOLE_RECEIPT` is the release and
DevOps three-family evidence document. `HONUA_AI_ARC_SDK_CONSOLE_RECEIPT` is the
manifest-pinned SDK's app-gate projection. Both are derived from the same live
observations and exact candidate boundary; neither is a self-declared receipt.

The release ordering is strict:

1. Studio `release:real-model-ai-arc -- prepare --execute --yes` writes the
   paused handoff to `HONUA_AI_ARC_REAL_MODEL_EVIDENCE` and exits 2.
2. This producer reads that handoff plus `HONUA_AI_ARC_CHECKPOINT`, then writes
   the aggregate and SDK Console projections.
3. Studio `release:real-model-ai-arc -- resume --execute --yes` reads the paused
   handoff plus the aggregate `HONUA_AI_ARC_CONSOLE_RECEIPT` and replaces the handoff with
   final real-model evidence.
4. The manifest-pinned SDK resumes with `HONUA_AI_ARC_SDK_CONSOLE_RECEIPT`; the
   release/DevOps aggregate validator consumes `HONUA_AI_ARC_CONSOLE_RECEIPT`.

`--pre-console-evidence` is the CLI equivalent of
`HONUA_AI_ARC_REAL_MODEL_EVIDENCE`. The legacy
`HONUA_AI_ARC_PRE_CONSOLE_RECEIPT`/`--pre-console-receipt` aliases are limited to
the direct paused-SDK-receipt path.
