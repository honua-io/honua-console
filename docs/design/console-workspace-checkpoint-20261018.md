# Console workspace checkpoint — 2026-10-18

The shared Console form captures submitted fields and authority before its first await. Publication recovery retains the original draft, revision, command and operation identity independently of later edits. An unresolved submission blocks new mutation until a read-only original-command check establishes its outcome. A reviewed retry after a terminal failure advances the canonical draft revision and preserves later typing. Recovery compares the latest pending identity after awaited reads so an old response cannot clear a newer request.

Workspace connection details are collapsed behind an accessible disclosure. Desktop and 390px disconnected onboarding expose local Docker guidance and existing-deployment connection actions in the first screen. Agent-follow navigation keeps dirty-edit guards. The host pins the embedded Maputnik URL to `/_studio/maputnik/index.html` and preserves asset-manifest coverage. Workflow package IDs use the server's bounded Unicode/control-character contract. The bounded process BFF accepts the canonical optional UploadId correlation and known-job cancel route; this does not make upload retries idempotent.

## Verification

Persistent receipts are under `artifacts/console-next-review-20261018` in the qualification workspace.

| Check | Result | Receipt |
| --- | --- | --- |
| Node | 221 passed, zero failed/skipped | `node-publication-final.log` |
| Native core | 1,496 passed, three live tests skipped | `core-final.log` |
| Integration | 1,143 passed, 60 live tests skipped | `integration.log` |
| Web build | Passed, zero warnings/errors (Debug) | `web.log` |
| Full solution format | Passed | `gate-publication-reload-final.log`, `format.log` |
| Full Chromium suite | 81 passed, exit 0, 6.6 minutes | `playwright-full-corrected.log` |
| Focused Chromium repetition | 36 passed across three repeats, exit 0 | `playwright-repeated.log` |
| Onboarding screenshots | Desktop 1440px and mobile 390px inspected; no horizontal overflow | `home-*-viewport.png`, `browser-layout.json` |

The browser suite uses the backend-free Console host and controlled fixtures where specified. Skipped live tests are not passes. This checkpoint does not establish a real connected Console/Studio form journey, actual publication against the combined server, or a large-file import. The replacement Studio bundle is still awaiting its own complete browser/build qualification. Resumable transfer, contributor handoff, admission and accepted-job execution remain separate work; all four epic milestones remain open.
