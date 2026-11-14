# Shipping Console live replay

Run **Shipping Console live replay** (`shipping-live.yml`) with a successful
`static-bundle.yml` run ID, the independently selected zip SHA-256, immutable
serving and matching executor image digests, and a full server source commit.
The workflow accepts only server commits already merged into server trunk.
That server source must contain the packaged static Console quickstart
(honua-server #7693). Images must be accessible to the runner.

The replay downloads `console-static-release` from that run and verifies the
bundle checksum while packaging the actual Server quickstart. It starts its
PostGIS, Server and HTTPS Console edge, then runs Chromium at 1280 and 390 widths.
There is no LegacyHost, request interception or contract fake server. It checks
password sign-in, the secure browser session cookie, live Home and connections
reads, CSRF rejection, successful sign-out and session revocation. The artifact
contains source-run metadata, input identities, logs and page screenshots.
Browser traces are disabled because they can retain credentials and cookies.

The disposable database also receives a one-feature service fixture compiled
with the selected server source's metadata snapshot helper. The browser opens
its Protocols tab, checks that displayed endpoints use the public HTTPS origin,
and queries the displayed FeatureServer URL for the seeded count. This setup
does not exercise service publication or approval. Its data and metadata are
removed with the temporary stack.

For local replay, install the locked dependencies in `e2e/playwright` and Chromium,
then run from the repository root on a Docker host:

```bash
SERVER_SOURCE=/path/to/pinned/honua-server \
SERVER_IMAGE=registry/server@sha256:... \
EXECUTOR_IMAGE=registry/executor@sha256:... \
CONSOLE_BUNDLE=/path/to/honua-console.zip \
CONSOLE_BUNDLE_SHA256=... bash e2e/run-shipping-live.sh
```

The host needs free quickstart ports (including 18083) and its default Docker
subnet. The stack and generated password are removed on exit. Honua lanes run
this through `remote-run` on the build host.

A green diagnostic run against source-built images is not independent verification.
For ticket verification, use a verification cohort with immutable image and bundle
identities and record the cohort and evidence; a cohort needs no release certification.
Release gate acceptance still requires its recorded live pass. This password-session journey does not cover OIDC,
publication approval or the other release journeys; approval requires an
interactive OIDC session.
