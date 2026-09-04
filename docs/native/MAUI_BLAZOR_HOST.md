# Optional MAUI Blazor Hybrid Host

Honua Console remains a browser-deployable Blazor web app. The native host is an optional operator and power-user shell that renders the same shared Razor routes from `Honua.Console.Shell` and adds native profile, certificate, and gRPC wiring.

## Project Layout

- `src/Honua.Console.Shell`: shared Razor component library for the Console shell, route map, environment profile model, account/RBAC session model, and native streaming proof contract.
- `src/Honua.Console.Web`: independently deployable browser Console host. It references `Honua.Console.Shell` only.
- `src/Honua.Console.Native.Core`: testable native services for JSON-backed environment profiles, account-token sessions, mTLS certificate references, native HTTP/gRPC connection setup, and the deterministic telemetry streaming proof.
- `src/Honua.Console.Native`: optional .NET MAUI Blazor Hybrid host. It renders `Honua.Console.Shell.ConsoleRoutes` in a `BlazorWebView` and binds the native-core profile/session storage abstractions to MAUI secure storage.

## Host And Route Contract

The shared shell owns the route map and workflow boundaries:

| Area | Route | Boundary |
| --- | --- | --- |
| Studio | `/studio` | Builder |
| Catalog | `/catalog` | Builder |
| Operate | `/operate` | Operator |
| Share | `/share/public` (entry alias `/share`) | Builder |

Host-support routes are also shared:

| Route | Web host behavior | Native host behavior |
| --- | --- | --- |
| `/environments` | Seeded profile list and active-profile selection. Native gRPC, native mTLS, connect/disconnect, and trust validation render as "Native host only" unsupported states. | Multi-environment list with connect/disconnect, last-seen, transport indicators, trust pill, and acknowledge/revalidate. |
| `/environments/new` | Renders an unsupported state (profile creation is native-only). | First-run / add-environment form persisting through `IConsoleEnvironmentProfileStore`. |
| `/environments/{id}` | Profile diagnostics without native connection actions. | Per-profile transport, trust state, server fingerprint, issuer, last-seen, resume token, and connect/acknowledge/revalidate actions. |
| `/operate/native-stream` | Renders a native-proof unavailable state because the web host does not register native gRPC services. | Resolves the active profile, opens it through the trust gate, emits no events for blocked/unreachable outcomes, and saves resume diagnostics after a successful proof stream. |

The browser host registers `AddHonuaConsoleShell()` with configured Honua server binding values for server-backed Operate routes. The MAUI host registers `AddHonuaConsoleShell()` and `AddHonuaConsoleNativeCore()`, which replaces the shell's in-memory profile/session stores with JSON native-core stores and adds certificate, token, connection, trust, and streaming services. At this checkpoint, native environment profiles are not yet wired into `IOperateTransitionDataSource`, so MAUI Operate transition routes use the same missing-binding state until a profile-backed binding is added. The test host keeps in-memory profile/secret adapters; the MAUI host binds those adapters to `NativeSecureStorage`. This keeps browser startup and deployment independent from MAUI workloads and native gRPC dependencies.

### Host-capability seam (web renders native-only as unsupported)

Shared routes gate native capabilities on the shell-owned `IConsoleHostCapabilities` seam and resolve `IConsoleConnectionManager` as an optional service:

- The web host keeps the default `BrowserConsoleHostCapabilities` (`SupportsNativeTransports = false`) and never registers `IConsoleConnectionManager`. Native gRPC, native mTLS, certificate selection, connect/disconnect, and trust validation render through the shell's consistent unsupported/empty surfaces. `Honua.Console.Web` references `Honua.Console.Shell` only - no native or `Grpc.Net.Client` dependency.
- `AddHonuaConsoleNativeCore()` replaces the seam with `NativeConsoleHostCapabilities` (`SupportsNativeTransports = true`) and registers the connection manager, trust gate, server-certificate probe, and validation client.

## Environment Profiles

The native profile store seeds two environments, `dev` and `staging`, and supports adding more with distinct profile state stored as JSON under `honua.console.native.environment-profiles.v1`:

- server base URL
- environment kind and optional tenant ID (multi-tenancy is Preview/trial only in 2026.1)
- browser HTTP/realtime capabilities
- native gRPC capability
- optional native mTLS capability
- account/RBAC binding metadata
- environment-specific certificate reference
- last route and streaming resume token state

The implemented profile shape is Console-owned UI state, not a duplicate of server API DTOs:

| Field | Notes |
| --- | --- |
| `Id`, `DisplayName` | Stable profile identity and operator-facing label. |
| `ServerBaseUri`, `EnvironmentKind`, `TenantId` | Active self-hosted Honua Server target. GA is single-tenant; a tenant ID selects a non-production Preview/trial context only. Never connect it to customer production data. No GA, availability, performance, durability, SLA, or SLO commitment applies to multi-tenancy. Honua offers no SaaS or managed hosting. |
| `TransportCapabilities` | `BrowserHttp`, `BrowserRealtime`, `NativeGrpc`, and `NativeMtls` capability flags. |
| `Account` | Account/RBAC binding with auth mode, account id, tenant id, display name, and permission hints. |
| `ClientCertificate` | Optional certificate reference for native mTLS, plus an optional server trust profile id passed to the validation endpoint. Supported reference kinds are `None`, `FilePath`, `StoreThumbprint`, and `StoreSubject`. |
| `ConsoleEnvironmentState` | Profile-scoped `LastRoute`, `LastStreamingResumeToken`, `LastConnectedAt`, diagnostics, client-side trust pins (`PinnedServerFingerprint`, `PinnedClientCertificateThumbprint`), `TrustBlocked`, and the last server-validated `Trust` state. |

Account authorization remains bearer-token account/RBAC based; mTLS is an optional per-environment transport trust layer. Anonymous auth mode omits bearer-token attachment; client certificates are controlled only by the profile's certificate binding. Native account sessions are stored per profile as secure-storage secrets named with the `honua.console.native.account-session.{profileId}.v1` pattern.

The seeded profiles are:

| Profile | Server | Native gRPC | Native mTLS | Initial route |
| --- | --- | --- | --- | --- |
| `dev` | `https://dev.honua.local` | Enabled | Disabled | `/studio` |
| `staging` | `https://staging.honua.example` | Enabled | Enabled through a current-user certificate-store subject reference | `/operate/native-stream` |

## Connection And Certificate Contract

`NativeHonuaConnectionFactory` creates one profile-scoped `HttpClient` and one `GrpcChannel` for the selected `ServerBaseUri`. The connection manager resolves the bound client certificate once and passes both that exact instance and the accepted server fingerprint into the factory, so native HTTP/gRPC uses the same server identity **and** the same client certificate the trust gate just validated — there is no resolve-time-of-check/use gap between validation and the transport. When a fingerprint is supplied, the transport callback requires an exact certificate fingerprint match, even if the presented certificate would otherwise pass OS trust; without a supplied fingerprint, the callback falls back to the OS chain decision. This preserves acknowledged private or self-signed server identities without allowing a different OS-trusted certificate to bypass the pin. If a profile has a saved account session, the factory attaches the session access token as a bearer token. When an enabled certificate reference resolves successfully, that already-validated certificate instance is attached to the native HTTP handler — the established connection owns and disposes it — and the stream transport is reported as `grpc/native+mtls`. Only a certificate with a usable private key is attached; a public-only certificate is dropped, so the transport never reports `grpc/native+mtls` for a credential that cannot complete client authentication.

Certificate references are environment-local. File path references load a PKCS#12/PFX file, with an optional password looked up by `SecretName`. Missing, unreadable, or invalid file references resolve to no certificate rather than throwing through the route; for an mTLS profile, the connection manager persists `Missing` trust state and blocks connection creation. A certificate that resolves but has no usable private key is treated the same way — it cannot complete client authentication, so it is never validated or attached and blocks as `Missing` (`client_certificate_private_key_unavailable`). Store references search the configured `StoreName` and `StoreLocation`, defaulting to `My` and `CurrentUser` when parsing fails.

## Trust Gate, Connection Lifecycle, And Cert-Changed Blocking

`IConsoleConnectionManager` (native-only) wraps the connection factory with a server-bound trust gate:

1. Resolve the bound client certificate (if any) **once** and observe the server's TLS certificate fingerprint via `IConsoleServerCertificateProbe` (a TLS handshake; the server exposes no fingerprint endpoint, so pinning is client-side). That single resolved certificate instance is reused for the probe, the server validation, and the established transport, so the connection can never present a different certificate than the one validated. The probe bounds the handshake with a 10-second timeout; for HTTPS profiles, failure to observe a fingerprint (handshake timeout or a network failure) is `Unreachable`, not trusted-by-default. Caller cancellation propagates (`OperationCanceledException`) rather than being reported as `Unreachable`, so a cancelled probe is never mistaken for an unreachable server.
2. When a client certificate is bound, validate it against the real server through `IConsoleClientCertificateValidationClient` - a thin `HttpClient` that posts the certificate's public PEM to honua-server#1171's `POST /api/v1/admin/security/client-certificates/validate` and maps the stable `client_certificate_*` codes onto `HonuaCertificateValidationStatus`. A `401`/`403` from the validate endpoint is a permission failure, not a transport failure: it is surfaced as a blocking `client_certificate_insufficient_rbac` (`Rejected`) trust result rather than `Unreachable`, so an authorization gap shows up as a trust diagnostic.
3. `ConsoleTrustEvaluator` (pure, host-agnostic) decides the resulting `HonuaEnvironmentTrustState` and whether to **block**:
   - the observed server fingerprint differs from the pinned (acknowledged) value (`server_certificate_changed`),
   - the bound client-certificate identity changed (`client_certificate_changed`),
   - the bound client certificate resolved without a usable private key, so it cannot complete client authentication (`client_certificate_private_key_unavailable`, surfaced as `Missing`), or
   - validation returned a blocking status (`Untrusted`, `Rejected`, `WrongEnvironment`, `Expired`, `Missing`).
4. A blocked result returns **no usable connection**; the profile state records `TrustBlocked = true`. An unreachable result also returns no usable connection, but does not rewrite persisted trust state, mark the profile trust-blocked, or update `LastConnectedAt`/pinned fingerprints. A blocked or unreachable outcome disposes any previously live connection for the profile — uniformly across connect, **Acknowledge**, and **Revalidate** — so `IsConnected` is never left `true` once trust is refused or the server cannot be reached. **Acknowledge** re-pins the newly observed server identity and clears a server-certificate-change block; **Revalidate** re-runs validation against the server. First use pins on a trust-on-first-use basis only after a fingerprint is observed. `ExpiringSoon` is a non-blocking warning.

Private keys are never persisted; only certificate references and the sanitized `HonuaEnvironmentTrustState` (status, server fingerprint, issuer summary, reason code, sanitized message) are stored. Server-side mTLS policy, trust registration, revocation, and capability advertisement are honua-server#1171 (closed) and remain Scope Out of the Console UI work.

`IConsoleConnectionManager` returns `ConsoleConnectionOutcome` values:

| Outcome | Contract |
| --- | --- |
| `Connected` | A profile-scoped native connection is held. `UsesMutualTls=true` only when an enabled client-certificate reference resolved **with a usable private key** and was attached. |
| `Blocked` | No usable connection. The profile stores `TrustBlocked=true` and the blocking `HonuaEnvironmentTrustState` until acknowledge or revalidate clears it. |
| `Disconnected` | No live connection is held, including a missing profile result. |
| `Unreachable` | No usable connection: any previously live connection for the profile is disposed. The server could not be reached or an HTTPS server fingerprint could not be observed; this preserves the last persisted trust state and does not set `TrustBlocked`, update `LastConnectedAt`, or pin new fingerprints. |

### Server validation wire contract

Until `honua-sdk-dotnet#166` ships the trust client, Console keeps the honua-server#1171 request/response shapes behind `src/Honua.Console.Contracts/EnvironmentTrustShims.cs`. No feature page or native service should redeclare these DTOs.

The native validation client sends:

| Field | Contract |
| --- | --- |
| `certificate` | Public client certificate only, exported as PEM. Private keys are never transmitted. |
| `encoding` | `pem` today; the shim also allows the server-supported `urlEncodedPem` and `base64Der` values. |
| `profileId` | Optional expected server trust profile id from `ConsoleClientCertificateBinding.TrustProfileId`. |

The server returns the standard Honua envelope with `success`, `data`, and optional `message`. The `data` payload is projected as:

| Field | Contract |
| --- | --- |
| `valid` | `true` when the certificate matches a configured trust profile and mapping. |
| `code` | Stable validation code such as `success`, `client_certificate_untrusted_issuer`, `client_certificate_wrong_environment`, or `client_certificate_expired`. |
| `detail` | Sanitized operator-facing detail. |
| `principalId`, `trustProfileId`, `mappingId`, `environmentId` | Optional matched server identities. |
| `fingerprintSha256` | Public certificate fingerprint reported by the server. |
| `daysUntilExpiry` | Optional expiry horizon used to surface `ExpiringSoon`. |

Console maps `success` to `Ready` or `ExpiringSoon`; missing or unresolved certificates to `Missing`; `client_certificate_expired` to `Expired`; `client_certificate_wrong_environment` to `WrongEnvironment`; untrusted issuer/chain/not-yet-valid responses to `Untrusted`; and revoked, unmapped, invalid EKU, forwarding, or insufficient-RBAC responses to `Rejected`. `Untrusted`, `Rejected`, `WrongEnvironment`, `Expired`, and `Missing` block native connection creation. `ExpiringSoon` is a warning and does not block.

## Streaming Proof Contract

The native streaming seam is the shared `IConsoleNativeStreamingProof` interface:

| Member | Contract |
| --- | --- |
| `ProofName` | Human-readable proof name displayed by `/operate/native-stream`. |
| `StreamAsync(ConsoleEnvironmentProfile profile)` | Returns an async stream of `ConsoleStreamingEvent` values for the active profile. |

`ConsoleStreamingEvent` contains:

| Field | Meaning |
| --- | --- |
| `EnvironmentProfileId` | Profile id that produced the event. |
| `Transport` | `grpc/native` or `grpc/native+mtls` after connection setup. |
| `EventKind` | Deterministic event kind such as `telemetry.subscribed`, `jobs.progress`, or `telemetry.sample`. |
| `Message` | Operator-facing event summary. |
| `Value` | Optional numeric value for progress or telemetry samples. |
| `ResumeToken` | Profile-scoped resume token saved after the stream completes. |
| `Timestamp` | Event timestamp. |

`NativeGrpcTelemetryStreamingProof` returns no events when `TransportCapabilities.NativeGrpc` is disabled or the connection manager reports a blocked/unreachable trust state. For enabled profiles, the deterministic fixture connects through `IConsoleConnectionManager`, emits three events only after the trust gate succeeds, and labels transport from the established connection outcome (`grpc/native+mtls` only when a client certificate was actually attached). The route saves the final resume token, last route `/operate/native-stream`, connection timestamp, proof name, and final transport in profile state while preserving existing trust pins, trust-blocked state, and unrelated diagnostics. This is CI/local smoke evidence for native host wiring until shared SDK/server job or telemetry streams are available.

## Capability Matrix

| Capability | Browser Console | Native MAUI Console |
| --- | --- | --- |
| Shared Studio/Catalog/Operate/Share shell routes | Yes | Yes |
| Independent deployment without native workload | Yes | No |
| Profile list and active-profile UI | Seeded in-memory state | Persisted native profile state |
| Account/RBAC bearer session model | Shared interface | Profile-scoped secure-storage implementation |
| Native gRPC channel | Unsupported state | Profile-scoped channel |
| Client certificate / mTLS attachment | Not supported | Optional per profile |
| Connect / disconnect / last-seen | "Native host only" | Through the trust gate |
| Server-bound trust validation (mTLS) | Unsupported state | Live validation against honua-server#1171 |
| Cert-changed blocking + acknowledge/revalidate | Unsupported state | Enforced before connecting |
| Native telemetry streaming proof | Unavailable state | Deterministic three-event stream |

## Desktop Build And Package

Install the .NET MAUI workload for the target platform before building:

```bash
dotnet workload install maui
```

Windows package/build from Windows:

```powershell
dotnet publish src/Honua.Console.Native/Honua.Console.Native.csproj `
  -f net10.0-windows10.0.19041.0 `
  -c Release
```

macOS package/build from macOS:

```bash
dotnet publish src/Honua.Console.Native/Honua.Console.Native.csproj \
  -f net10.0-maccatalyst \
  -c Release
```

The generated MAUI project keeps `WindowsPackageType=None` for an unpackaged Windows build until signing and installer/update policy are defined. App store distribution automation is out of scope for this ticket.

## Local Validation

On Linux, validate the host-independent implementation with:

```bash
./scripts/fast-local-check.sh
```

The fast check runs:

```bash
dotnet test tests/Honua.Console.Native.Core.Tests/Honua.Console.Native.Core.Tests.csproj
dotnet build src/Honua.Console.Web/Honua.Console.Web.csproj
```

The native MAUI desktop app must be compiled on Windows or macOS. On Linux, the native project builds a no-op library target by default so solution and fast validation do not require Android tooling. Android source validation remains explicit with `-p:EnableHonuaConsoleAndroidTarget=true` and requires an Android SDK; it is not part of the desktop host acceptance path.

Native-core tests cover:

- shared route boundaries for Builder and Operator areas
- seeding and persistence of at least two distinct environment profiles
- account/RBAC bearer-token attachment and anonymous-profile omission
- optional client-certificate attachment
- deterministic native gRPC telemetry proof events and mTLS transport labeling
- trust-gate cert-changed blocking, acknowledge/revalidate, unreachable HTTPS probes, probe caller-cancellation propagation, missing/bad certificate references, the private-key-required mTLS gate (a public-only certificate blocks instead of connecting), server-fingerprint pin enforcement on the transport, stream-state preservation, and stable-code → trust-status mapping (`ConsoleTrustEvaluatorTests`, `ConsoleConnectionManagerTests`, `ConsoleStreamingStatePersistenceTests`, `StoreClientCertificateResolverTests`, `TlsServerCertificateProbeTests`, `NativeConnectionFactoryTests`, `NativeServerTrustTests`)

### Real-server trust integration lane (opt-in)

`tests/Honua.Console.IntegrationTests` boots a real `honua-server` (with PostgreSQL) via Testcontainers, configures mTLS, and asserts client-certificate validation and cert-changed blocking against live data (Console Patterns Charter section 11). It also drives a profile through the real trust gate against the live server and renders the shared `EnvironmentProfileDetailPage` (via bUnit), asserting the diagnostics surface shows the live blocking trust state. It is **off by default** and skips every fact with a clear reason unless opted in. It is intentionally **not** part of `fast-local-check.sh`. Run it with:

```bash
HONUA_CONSOLE_INTEGRATION=true \
HONUA_CONSOLE_SERVER_IMAGE=<honua-server image with honua-server#1171> \
HONUA_CONSOLE_ADMIN_TOKEN=<admin bearer token> \
./scripts/integration-trust-check.sh
```

Or target an already-running server with `HONUA_CONSOLE_EXTERNAL_BASE_URL`. See the script header for the full set of optional environment variables.
