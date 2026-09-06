# Focused Console 2026.1 evidence — #351

The release promise is the optional, supported inspect/approve/operate/recover Console surface,
with every privileged interactive request attributed to the active human and no shared-key fallback.
This work does not change the terminal journey's independent support or evidence status.

| Acceptance area | Pre-cut implementation and evidence | Remaining qualification |
| --- | --- | --- |
| Production startup and authorized focused routes | Configured server target initializes an isolated per-operator profile without granting a session; existing deny-by-default route audit retained | Start/authenticate the exact supported Console image with the cut server |
| Terminal-created resource, job, result, version/hash and lifecycle identities | Existing typed read routes and focused terminal-receipt adapter retained; contract tests run in Playwright CI | Consume the actual completed/paused terminal receipt; assert every required identity in successfully loaded views |
| Generic approve/reject parity | Existing generic proposal and deploy clients use the same operator-only transport as reads | Server #3304 publication-intent bridge is still in open PR #3980. The #3365 grant is implemented by merged PR #3576, with stronger proof in merged PR #4372. Run separate Console/CLI human decisions and compare canonical identifiers after the bridge lands |
| Bearer-only privileged reads, writes and map proxy | Final browser transport strips configured keys, checks credential expiry/exchange and server binding, disables redirects/cookies; invalid credentials return 401 before transport | Exact-candidate identity flow |
| Operational read policy and stronger mutation/approval policy | Console forwards server decisions and does not infer privileges from read access | Server `OpsReadPolicy` endpoint coverage and real two-operator/two-tenant scope, owner, self-approval and protected-read matrix |
| Operator and target isolation | Registered-client tests cover absent, sentinel, expired, unbound and mismatched credentials; independent sessions forward distinct bearers; target edits fail closed | Server-side authorization is not proven by an instrumented upstream |
| Sanitized diagnostics and recovery | Existing diagnostics/recovery views retained; proxy errors expose no upstream body; map responses prohibit cross-operator caching | Candidate health/release faults with canonical operation/proposal/audit IDs and recovery links |
| Full and witness modes | Production browser proxy suite exercises both presentation modes under identical operator rules; witness navigation is focused and broader surfaces are marked Preview | Read/approve smoke against the exact receipt and candidate |
| Independent UI evidence | Receipt writer rejects passing evidence with a blocked approval; failed terminal receipts rejected; fresh fail record precedes browser inspection | Emit the separate complete UI-parity receipt after candidate qualification |

`FocusedOperatorCredentialTests` exercises the production DI/client chain over an instrumented
transport, including a typed version read whose expected version is the independently specified
fixture `2026.1.7`. `ConsoleServerBoundClientFactoryFailClosedTests` retains concurrent circuit
isolation assertions. `MapProxyCacheHeaderTests` asserts no-store even if upstream says public.

`e2e/playwright/operator/credential-boundary.spec.ts` runs Chromium/API requests through the real
Production host in full and witness modes with a configured shared key and a controlled upstream.
It checks all three proxy routes, exact feature values `[17, 25]`, fixed tile bytes, style URL rewriting,
401/403 status preservation, no upstream diagnostic disclosure, and zero upstream calls for an
operator with no bearer. This is Console transport evidence, not a simulated server RBAC proof.

The candidate-only rows are released from pre-cut execution because the exact cut image and its
terminal receipt cannot exist until after the burn-down and candidate cut (operator ruling B).
Open server implementation and policy dependencies remain blockers; they are not released as
candidate-only work. #351 must remain open until those criteria are satisfied or explicitly dispositioned.

## Pre-cut verification

- Focused .NET transport, presentation, session-BFF and cache regressions: **54 passed, 0 skipped**.
- Focused terminal-receipt contract/adjudication tests: **4 passed**.
- Current Web host rebuild: **passed, 0 warnings, 0 errors**.
- Browser, full local suites and final-head CI results are recorded in PR #360 after execution.

The local lane required escalation for MSBuild's denied IPC socket. Shared compilation stayed
enabled; graceful build-server shutdown released inherited build-slot locks before the successful
local build. These local results do not substitute for exact-candidate qualification.
