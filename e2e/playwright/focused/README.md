# Focused 2026.1 Console candidate smoke

This TypeScript/Playwright lane consumes the terminal journey receipt and inspects the exact
identities already created by that journey. It does not create resources or publication requests.
It starts the stock Console without `HONUA_ADMIN_API_KEY`; successful interactive reads therefore
require the active operator bearer.

Run the contract tests without a Console host:

```sh
HONUA_CONSOLE_FOCUSED_ORIGIN=http://127.0.0.1:9 \
  npx playwright test --config playwright.focused.config.ts focused/specs/receipt-contract.spec.ts
```

Run the candidate smoke against the repository live-auth stack or an external Console:

```sh
HONUA_CONSOLE_FOCUSED_TERMINAL_RECEIPT=/absolute/path/to/terminal-receipt.json \
HONUA_CONSOLE_FOCUSED_IDP_USER='<candidate approver user>' \
HONUA_CONSOLE_FOCUSED_IDP_PASSWORD='<private secret>' \
npm run e2e:focused
```

Set `HONUA_CONSOLE_FOCUSED_ORIGIN` to use an already-running candidate Console, and
`HONUA_CONSOLE_FOCUSED_EVIDENCE_PATH` to choose the output receipt path. The evidence contains
resource identifiers, route outcomes, and server image/source pins. It never contains credentials.

## Remaining publication and candidate evidence

The narrow approval grant from honua-server#3365 is implemented in merged server PR #3576;
merged PR #4372 adds focused approval-effect and read-only-denial tests. The issue remaining open
is not evidence that its grant implementation is absent.

The publication-intent bridge for honua-server#3304 remains in open server PR #3980. Until it lands,
this lane cannot qualify the full proposal/operation/audit/publication/final-link chain using the
server-owned publication lifecycle. The focused receipt records that remaining dependency.

After the bridge lands and the exact candidate exists, run approve and reject through the selected
generic proposal panel under a separate human principal, compare the separate `honua admin` profile's
canonical decision and lifecycle identities, and exercise the real server's insufficient-scope,
wrong-tenant, wrong-owner, self-approval and protected-read cases. Full and witness presentation modes
must both pass; local proxy transport coverage alone is not approval qualification.

## Evidence disposition

The current harness reports `blocked` after successful inspection while approval parity remains
unqualified. It writes a fresh failed receipt before browser actions and rejects passing evidence
with a blocked approval. Failed terminal receipts cannot be inputs. Receipt-contract tests run in
the normal Playwright CI job; skipped candidate smoke is not support evidence.

Console transport regressions run in `FocusedOperatorCredentialTests`: both privileged and map
clients deny missing/sentinel/expired/unbound/wrong-target sessions before transport even with a
configured shared key. The tests also prove per-operator bearer forwarding, unchanged server 403s,
production target initialization and an actual typed version read of the independent `2026.1.7`
fixture. These tests prove Console wiring, not Honua's server-side RBAC policies.

Exact-candidate qualification still requires the terminal receipt, separate real Console and CLI
principals, scoped approval grant, server policy tests, and full/witness browser runs on the cut image.
A passing pre-cut transport or receipt-contract test does not replace that qualification.
