# Issue 421 audit record

Re-verification was performed against trunk. The three security findings were already fixed.
The two low-severity backlog items were still present and are fixed on this branch.

| Finding id | Outcome | Evidence |
| --- | --- | --- |
| CON-005 | fixed | `CON-005 stored embed options cannot re-enable Vega actions` in `e2e/playwright/specs/chart-runtime.spec.ts` supplies `usermeta.embedOptions.actions: true` and proves that the rendered chart has no `.vega-actions` element. `chart-preview.js` now removes the untrusted embed options before invoking Vega Embed. |
| CON-002 | fixed | `CON_002_Sanitize_RejectsDecodedTabThatBrowsersTreatAsProtocolRelative` in `tests/Honua.Console.Native.Core.Tests/ConsoleReturnUrlTests.cs` proves that a decoded tab bypass is rejected; the surrounding theory also covers newline, carriage return, and DEL. |
| CON-004 | fixed | `CON_004_EdgeBearer_IsNotReboundToAnOperatorAddedServer` in `tests/Honua.Console.IntegrationTests/ConsoleEdgeIdentityBearerPersistenceTests.cs` proves that an edge bearer becomes a non-forwardable sentinel when the active profile does not share the configured server origin. |
| Low: process-wide server-session slot exhaustion | fixed | `GetOrCreate_OneOperatorCannotFillTheProcessWideTable` proves one operator is capped and their least-recently-used partition is evicted, while another operator's partitions stay. `BeginSignIn_FailedServerConfig_DoesNotKeepTheNewPartition` proves a failed configuration probe does not retain the new slot. |
| Low: base-path retargeting in `HonuaServerBindingHandler` | fixed | `RetargetsStartupBasePathOntoTheActiveProfileBasePath` proves a request under the startup base path is rebuilt on the active profile's base path. |

## Observable behavior

- Stored chart specifications can retain other `usermeta`, but `usermeta.embedOptions` no longer
  affects rendering; chart actions remain disabled.
- Redirect targets containing ASCII control characters now fall back to `/`.
- An edge-forwarded access token is persisted only for an active profile on the configured
  deployment server's origin. Other profiles receive the existing non-forwardable session
  sentinel and must use their per-origin sign-in flow.
- One operator can hold at most 32 server-session partitions. A further sign-in evicts that
  operator's least-recently-used partition. A configuration probe that does not succeed does
  not keep a newly created partition.
- Family-A requests keep the path relative to the startup server base and resolve it against
  the active profile base, so a profile with its own base path is not called at the startup prefix.
