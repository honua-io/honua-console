# Studio customer journey

The `studio-customer` project in `playwright.live.config.ts` runs
`live/specs/studio-customer-journey.live.spec.ts` against the real server, PostGIS,
Keycloak and Console image. It does not reuse the admin setup project or stored cookies.
The `customer` realm user is the install's `admin` publisher. The Console has no admin key.
An independent verifier provisions the private source connection and reads server state;
all customer imports, publication and authoring use the human session.

The second test is an operator control that isolates the embedded recipe table/filter
transport from the customer import path. It uses a separate real operator login,
a typed PostGIS fixture and a real published layer. It is not customer qualification.

The workflow builds a Console image from the checked-out PR, uses the ordinary live
Compose testbed plus `docker-compose.studio-customer.yml`, and runs nightly and on changes
to the Console host, shell and bundled Studio assets. Its manual `studio-customer` input
also accepts immutable `console_image` and `server_image` GHCR references. Both are
required when replaying a published Console. The runtime receipt checks the actual
container image IDs, observed repository digests and source labels, plus `/version.json`.
Setting `HONUA_CONSOLE_E2E_CONSOLE_IMAGE` disables the Playwright source web server.
Every CI run requires a `ghcr.io/honua-io/honua-server@sha256:...` input or an immutable
digest in `HONUA_CONSOLE_PR_SERVER_IMAGE` or `HONUA_CONSOLE_NIGHTLY_SERVER_IMAGE`.
Without an override, the workflow uses the reviewed `nightly-a5e593b` server digest pinned in
the workflow. Floating tags fail before the testbed starts. Local source-image diagnostics
must set `HONUA_CONSOLE_E2E_SERVER_IMAGE` explicitly and are not release qualification.

The testbed uses the unlicensed 2026.1 server posture (`Licensing:Mode=Disabled`),
where ordinary draft composition executes directly and publication retains its separate
governed approval floor. It sets a one-minute operator bearer lifetime while leaving the real IdP
session alive, so a real server refresh can occur during authoring. The test observes
the old expiry pass and requires a newer server expiry with the same principal and map.
It never edits browser clocks, injects tokens, supplies fake model responses or replaces
API calls. Every 4xx/5xx, connectivity failure, console error or axe violation fails. Intentional
fetch cancellation is retained separately; actual loaded rows and filters still must pass.

Evidence includes typed OGC/FeatureServer reads, saved-version owner and body reads,
separate server export PNG pixels with wrong-colour and transparent-gap controls,
public viewer reads, axe output and credential-free progress. Auth traces, videos and automatic screenshots are disabled because they can retain
passwords, cookies or authorization codes. Named product milestones are captured only
after sign-in with the credential-page guard described below. Container logs remain diagnostics; do not upload token-bearing debug logs.

The customer test is strict: forbidden imports, missing label authoring, unsupported
map-local server rendering or incomplete publication remain failures. A successful
operator transport control cannot turn an incomplete customer journey into a pass.
The release gate consumes the complete customer progress attachment as well as the
Playwright totals. Candidate pins and release notes are owned by honua-release.

## Agent reflection in the customer quickstart

The `agent-reflection` Playwright project starts the server's packaged quickstart with its
separate manager and Compose executor, adds a local OIDC provider for an attributable customer
session, and runs the pinned Console image or the PR image. A deterministic MCP client reads
live release state, observes stopped Redis, creates a restart proposal, and waits for a distinct
principal to approve it. The same client imports and publishes polygon and CSV point datasets;
server metadata and audit records are checked alongside Console views. Browser errors, failed
requests and HTTP 4xx/5xx remain failures.

The live workflow requires a server image whose source includes the managed Compose observer
and executor changes. For a PR it derives an executor from that selected server image; release
replay supplies the immutable executor image from the same candidate through `executor_image`.
The quickstart and its private test credentials live only in an isolated workspace directory;
the workflow removes its containers and volumes. The uploaded agent result contains progress
and runtime image identities, not credentials or browser traces. Both the Studio and agent
journeys must pass for the release gate; missing progress cannot qualify a candidate.

## Milestone screenshot gallery

Both the Studio customer and agent-reflection journeys capture named screenshots only
on product pages after sign-in. The gallery includes map creation, applied style,
rendered labels, saved/reopened maps, health degradation and its finding, pending and
completed restart proposals, recovery, and published layer geometry and field types.
Credential pages are refused by the capture helper; sign-in traces remain disabled.

The `studio-customer-journey-evidence` artifact contains
`artifacts/studio-customer/gallery/index.html` and its PNGs. Download the artifact and
open the index locally. The same workflow builds this gallery for pull requests,
nightly runs, and immutable-image replays dispatched by the release customer gate
(#1182, extended by honua-release#1193). The index is built even after a failure so
partial screenshots remain available; missing milestones never establish qualification.
