# Studio customer journey

The `studio-customer` project in `playwright.live.config.ts` runs
`live/specs/studio-customer-journey.live.spec.ts` against the real server, PostGIS,
Keycloak and Console image. It does not reuse the admin setup project or stored cookies.
The `customer` realm user has only the `user` role. The Console has no admin key.
An independent verifier provisions the private source connection and reads server state;
all customer imports, publication and authoring use the human session.

The second test is an operator control that isolates the embedded recipe table/filter
transport from the customer permission path. It uses a separate real operator login,
a typed PostGIS fixture and a real published layer. It is not customer qualification.

The workflow builds a Console image from the checked-out PR, uses the ordinary live
Compose testbed plus `docker-compose.studio-customer.yml`, and runs nightly and on changes
to the Console host, shell and bundled Studio assets. Its manual `studio-customer` input
also accepts immutable `console_image` and `server_image` GHCR references. Both are
required when replaying a published Console. The runtime receipt checks the actual
container image IDs, observed repository digests and source labels, plus `/version.json`.
Setting `HONUA_CONSOLE_E2E_CONSOLE_IMAGE` disables the Playwright source web server.

The testbed sets a one-minute operator bearer lifetime while leaving the real IdP
session alive, so a real server refresh can occur during authoring. The test observes
the old expiry pass and requires a newer server expiry with the same principal and map.
It never edits browser clocks, injects tokens, supplies fake model responses or replaces
API calls. An unexpected 4xx/5xx, failed request, console error or axe violation fails.

Evidence includes typed OGC/FeatureServer reads, saved-version owner and body reads,
separate server export PNG pixels with wrong-colour and transparent-gap controls,
public viewer reads, axe output and credential-free progress. Auth traces, videos and
screenshots are disabled because they can retain passwords, cookies or authorization
codes. Container logs remain diagnostics; do not upload token-bearing debug logs.

The customer test is strict: forbidden imports, missing label authoring, unsupported
map-local server rendering or incomplete publication remain failures. A successful
operator transport control cannot turn an incomplete customer journey into a pass.
The release gate consumes the complete customer progress attachment as well as the
Playwright totals. Candidate pins and release notes are owned by honua-release.
