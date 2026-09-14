# Candidate-bound Console receipt producer

The canonical producer runs only at the SDK's `console-approval` pause. It opens
the exact published Console in Chromium, validates the checkpoint digest and the
sealed paused Studio handoff, inspects connection/service/layer and three GP job
identities, selects the unique candidate-bound map/app/dashboard proposals, and
uses the real Console approval controls. It then observes publication, structured
audit operation identity, deliberate-failure diagnostics, and stable-job recovery
in the Console UI. The direct server client is a read-only witness and cannot
produce a release receipt.

Use an operator bearer for the interactive browser through
`HONUA_AI_ARC_CONSOLE_TOKEN`; `HONUA_ADMIN_KEY` and `HONUA_API_KEY` are refused.
The credential is attached in-process to requests for the configured Console
origin as a trusted-edge operator session. It is never attached to server/public
origins and is never written to stdout, receipts, or temporary files.

To exercise the focused server API-key recipe, also supply
`HONUA_AI_ARC_CONSOLE_READ_APPROVE_KEY` and its
`HONUA_AI_ARC_CONSOLE_READ_APPROVE_KEY_ID`, and run in
`HONUA_CONSOLE_MODE=witness`. The preflight verifies the key's effective active
grants are exactly `admin:read` and `admin:approve`, then reads and approves the
exact candidate proposals with `X-API-Key`;
the browser then independently witnesses those same proposal, operation, audit,
and publication identities. The key is never serialized into either receipt.

```sh
HONUA_AI_ARC_ENDPOINT=http://127.0.0.1:8080 \
HONUA_AI_ARC_CONSOLE_ORIGIN=http://127.0.0.1:8081 \
HONUA_AI_ARC_CHECKPOINT=out/checkpoint.json \
HONUA_AI_ARC_CONSOLE_RECEIPT_SCHEMA=honua-sdk-js/mcp/release/zero-to-map/contracts/console-receipt.schema.json \
HONUA_AI_ARC_REAL_MODEL_HANDOFF=out/studio-real-model-handoff.json \
HONUA_AI_ARC_CONSOLE_RECEIPT=out/console-release.json \
HONUA_AI_ARC_SDK_CONSOLE_RECEIPT=out/console-sdk.json \
HONUA_AI_ARC_CONSOLE_EVIDENCE=out/console-evidence.json \
HONUA_AI_ARC_CONSOLE_TOKEN='<scoped bearer>' \
HONUA_AI_ARC_CONSOLE_READ_APPROVE_KEY='<admin:read + admin:approve key>' \
HONUA_AI_ARC_CONSOLE_READ_APPROVE_KEY_ID='<key UUID>' \
HONUA_CONSOLE_EDGE_AUTH='<trusted edge shared secret, when configured>' \
HONUA_CONSOLE_MODE=witness \
npm run receipt:console
```

For AWS, `HONUA_AI_ARC_ENDPOINT` must be HTTPS. Local Docker accepts HTTP or
HTTPS for its private endpoint and Console origin; the SDK-owned receipt contract
still requires each externally shared publication URL to use HTTPS.
`HONUA_CONSOLE_MODE=full` approves pending proposals; `witness` never mutates and
passes only when the exact proposals were already resolved. The command exits 0
only after all three files are written, and exits 1 without a passed receipt on
missing input, mismatch, browser failure, or write failure.

The two receipt paths are intentional distinct files containing byte-identical
copies of the one manifest-pinned SDK schema `honua.zero-to-map.console-receipt/v1`.
The producer loads and validates that exact pinned schema before either write.
`HONUA_AI_ARC_CONSOLE_EVIDENCE` is a separate Console-owned
`honua.console.ai-arc-evidence/v1` sidecar binding aggregate/handoff/checkpoint
digests, component and observed runtime SHAs, browser observations, and canonical
integrity. It is evidence, not an authentication signature.

The release ordering is strict:

1. Studio `release:real-model-ai-arc -- prepare --execute --yes` writes the
   immutable paused handoff to `HONUA_AI_ARC_REAL_MODEL_HANDOFF` and exits 2.
2. This producer reads that handoff, checkpoint, and pinned SDK schema, then
   writes the byte-identical aggregate aliases and the Console evidence sidecar.
3. Studio `release:real-model-ai-arc -- resume --execute --yes` reads the immutable
   handoff plus the aggregate `HONUA_AI_ARC_CONSOLE_RECEIPT` and writes final
   real-model evidence to `HONUA_AI_ARC_REAL_MODEL_EVIDENCE` without replacing
   the handoff.
4. The manifest-pinned SDK resumes with `HONUA_AI_ARC_SDK_CONSOLE_RECEIPT`; the
   release/DevOps aggregate validator consumes `HONUA_AI_ARC_CONSOLE_RECEIPT`.

`--real-model-handoff` is the CLI equivalent of
`HONUA_AI_ARC_REAL_MODEL_HANDOFF`. The canonical CLI accepts only the sealed
Studio handoff at this boundary. `HONUA_AI_ARC_REAL_MODEL_EVIDENCE` is reserved
for Studio's post-Console output; Console never reads or writes it. The retired
`--pre-console-evidence` input is refused.
