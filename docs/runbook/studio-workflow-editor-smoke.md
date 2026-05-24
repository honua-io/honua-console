# Studio Workflow Editor Smoke

Use this targeted smoke for honua-console#17 until server-backed Console
previews are wired.

1. Start Console with `npm run dev -- --host 127.0.0.1`.
2. Open the Studio workflow editor.
3. Select `Generate Draft`.
4. Confirm the generated definition JSON contains `source`, `process`,
   `transform`, `sink`, and `publication` nodes.
5. Select `Validate` and confirm the validation panel reports `valid`.
6. Select `Dry Run` or `Sample Run`.
7. Confirm run history shows `accepted -> running -> successful`, logs,
   artifacts, rejected rows, and provenance links.
8. Enable scheduled execution, keep `0 2 * * *`, and select
   `Publish Batch Definition`.
9. Confirm the published content item shows manual and scheduled modes,
   versions, rollback controls, provenance hash, and run-history link.
10. Select `Publish Process Service`.
11. Confirm the process route uses `/ogc/processes/processes/.../execution`
    and includes parameter, result package, and permission metadata.
