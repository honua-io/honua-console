# Bundled support KB (L0 deflection)

`support-kb.jsonl` is a vendored copy of the honua-gis-llm support KB seed,
used by the `/support` L0 deflection KB search
(`FileConsoleSupportKnowledgeBase`).

## Provenance

- **Source repo:** `honua-gis-llm`
- **Source path:** `kb/v0.1/support-kb.jsonl`
- **Produced by:** `scripts/build_support_kb.py` (honua-gis-llm#26) from the
  corpus fault-resolution records (`corpus/v0.1/faultcatalog.jsonl`) and,
  optionally, the honua-devops FaultCatalog.
- **Schema:** validates against honua-gis-llm `kb/schema/kb_record.schema.json`.

## Refresh

Re-run the upstream build and copy the artifact back over this file:

```bash
# in honua-gis-llm
python scripts/build_support_kb.py --out kb/v0.1/support-kb.jsonl
# then copy kb/v0.1/support-kb.jsonl -> this directory
```

A deployment may override the bundled copy with a newer artifact by setting
`Honua:Support:KbPath` / `HONUA_SUPPORT_KB_PATH` to an absolute JSONL path.

## Embeddings (stubbed)

Every record currently ships `embedding: null`. The ingestion, schema, and
`embedding_text` are real and tested upstream; only vector population is
deferred. Console therefore retrieves **lexically** over
`embedding_text` / `title` / `symptoms` (see `FileConsoleSupportKnowledgeBase`).
When an OpenAI-compatible `/v1/embeddings` backend is available, re-run the
upstream build with `--embed <model-id>` and swap the ranker for cosine
similarity (single swap point: `FileConsoleSupportKnowledgeBase.Search`).
