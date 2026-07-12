# Pinned diagnostic-bundle conformance kit (vendored, read-only)

This directory is a **pinned, read-only mirror** of the support-owned diagnostic-bundle
conformance kit. The source of truth is `honua-io/honua-support` — Console never forks or
edits this contract. See honua-console#307 and honua-support#54 / honua-support#57.

## Contents

| File | Purpose |
| --- | --- |
| `diagnostic-bundle.v1.json` | Canonical sanitized diagnostic-bundle v1 JSON Schema (byte-for-byte identical to `https://honua.io/schemas/diagnostic-bundle.v1.json`). |
| `diagnostic-bundle.v1.provenance.json` | Published provenance: source pin, byte length, and SHA-256 of the schema. |
| `diagnostic-bundle.v1.conformance/manifest.json` | Language-neutral conformance manifest: `schemaSha256` pin + valid/invalid cases. |
| `diagnostic-bundle.v1.conformance/valid/*.json` | Instances that MUST validate. |
| `diagnostic-bundle.v1.conformance/invalid/*.json` | Instances that MUST be rejected (each with an `expectedErrorContains`). |

## Provenance / pinning

- Schema source commit (from `diagnostic-bundle.v1.provenance.json`):
  `honua-io/honua-support@0c990fbe8f519a00a57e26dab21cbb8f80d559ea`,
  path `schemas/diagnostic-bundle.v1.json`.
- Schema SHA-256: `4dd7282d17bb417d56f1c3cfa243e03b612a401e5d22be766658849287e431a9` (6494 bytes).
- Kit mirrored from `honua-io/honua-support@3704ab95c880fcc709417d4dc31c25a36796a6b1`.

`DiagnosticBundleProvenanceTests` enforces the pin: the schema bytes here, the copy embedded
into `Honua.Console.Shell`, the provenance `sha256`/`bytes`, and the manifest `schemaSha256`
must all agree. Any drift (tampering with the schema, the pin, or the embedded copy) fails CI
with a clear provenance/hash error. To adopt a new schema version, re-mirror this whole kit from
honua-support and update the pin together — never edit these files in place.
