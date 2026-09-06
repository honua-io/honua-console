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

## Slices waiting on honua-server#3365

The following slices are deliberately not claimed by this lane until the server publishes and a
candidate image carries the residual `admin:approve` grant recipe:

1. Approve and reject mutations from the selected proposal panel under a separately scoped human
   approver bearer.
2. Console-versus-`honua admin` semantic parity for proposal, decision/approval, operation, audit,
   publication, and final-link identifiers.
3. Negative approval cases that require minting the residual grant recipe: insufficient scope,
   wrong tenant, wrong owner, proposer self-approval, and protected proposal detail non-disclosure.
4. The full and witness-mode approval smoke. Their read-only inspection coverage can run now, but
   neither mode can claim approval support before the candidate grant exists.

Receipt parsing, exact-ID read routes, health/release/observability/support reads, operator-bearer
wiring, fail-closed no-admin-key configuration, and independent UI evidence are not blocked by
#3365 and live in this directory.


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
