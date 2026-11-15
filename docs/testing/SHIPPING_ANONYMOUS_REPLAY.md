# Signed-out shipping Console replay

The shipping Playwright suite includes `shipping/anonymous-session.spec.ts`.
Each test starts with an isolated browser context and runs at 1280 × 900 and
390 × 844 against the static bundle served by the packaged Server quickstart.

The replay checks that the session endpoint reports an anonymous session,
services, connections and API-key list reads return HTTP 401, and no browser
session cookie is issued. It also checks the mounted sign-in page preserves a
local return destination, including its query and fragment, while external,
protocol-relative and encoded traversal destinations fall back to Console Home.
Screenshots capture the signed-out page and the local return destination.

Install the locked dependencies and Chromium in `e2e/playwright`, then run from
the repository root on a Docker host with free quickstart ports, including 18083:

```bash
SERVER_SOURCE=/path/to/pinned/honua-server \
SERVER_IMAGE=registry/server@sha256:... \
EXECUTOR_IMAGE=registry/executor@sha256:... \
CONSOLE_BUNDLE=/path/to/honua-console.zip \
CONSOLE_BUNDLE_SHA256=... bash e2e/run-shipping-live.sh
```

This command runs every shipping test, including sign-in and sign-out, and
removes the stack and generated password on exit. The server source must include
the static Console quickstart. Honua lanes execute it through `remote-run`.
Use a green-trunk verification cohort with recorded source commits, image
digests and bundle checksum; cohort verification needs no release certification.
Record the cohort identity, command, exit status, logs and both viewport reports
with the verification result. A pass requires every shipping test to pass with
no skips. Test discovery alone does not establish a live pass.
