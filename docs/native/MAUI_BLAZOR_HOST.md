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
| `/environments` | Read-only profile list. Native gRPC, native mTLS, connect/disconnect, and trust validation render as "Native host only" unsupported states. | Multi-environment list with connect/disconnect, last-seen, transport indicators, trust pill, and acknowledge/revalidate. |
| `/environments/new` | Renders an unsupported state (profile creation is native-only). | First-run / add-environment form persisting through `IConsoleEnvironmentProfileStore`. |
| `/environments/{id}` | Read-only connection diagnostics. | Per-profile transport, trust state, server fingerprint, issuer, last-seen, resume token, and connect/acknowledge/revalidate actions. |
| `/operate/native-stream` | Renders a native-proof unavailable state because the web host does not register native gRPC services. | Resolves the active profile, streams deterministic telemetry proof events, and saves resume diagnostics. |

The browser host registers `AddHonuaConsoleShell()` only. The MAUI host registers `AddHonuaConsoleShell()` and `AddHonuaConsoleNativeCore()`, which replaces the shell's in-memory profile/session stores with JSON native-core stores and adds certificate, token, connection, trust, and streaming services. The test host keeps in-memory profile/secret adapters; the MAUI host binds those adapters to `NativeSecureStorage`. This keeps browser startup and deployment independent from MAUI workloads and native gRPC dependencies.

### Host-capability seam (web renders native-only as unsupported)

Shared routes gate native capabilities on the shell-owned `IConsoleHostCapabilities` seam and resolve `IConsoleConnectionManager` as an optional service:

- The web host keeps the default `BrowserConsoleHostCapabilities` (`SupportsNativeTransports = false`) and never registers `IConsoleConnectionManager`. Native gRPC, native mTLS, certificate selection, connect/disconnect, and trust validation render through the shell's consistent unsupported/empty surfaces. `Honua.Console.Web` references `Honua.Console.Shell` only - no native or `Grpc.Net.Client` dependency.
- `AddHonuaConsoleNativeCore()` replaces the seam with `NativeConsoleHostCapabilities` (`SupportsNativeTransports = true`) and registers the connection manager, trust gate, server-certificate probe, and validation client.

## Environment Profiles

The native profile store seeds two environments, `dev` and `staging`, and supports adding more with distinct profile state stored as JSON under `honua.console.native.environment-profiles.v1`:

- server base URL
- environment kind and tenant ID
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
| `ServerBaseUri`, `EnvironmentKind`, `TenantId` | Active Honua Server target and tenant/environment identity. |
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

`NativeHonuaConnectionFactory` creates one profile-scoped `HttpClient` and one `GrpcChannel` for the selected `ServerBaseUri`. If a profile has a saved account session, the factory attaches the session access token as a bearer token. If the profile has an enabled certificate reference that resolves successfully, the factory attaches that certificate to the native HTTP handler and the stream transport is reported as `grpc/native+mtls`.

Certificate references are environment-local. File path references load a PKCS#12/PFX file, with an optional password looked up by `SecretName`. Store references search the configured `StoreName` and `StoreLocation`, defaulting to `My` and `CurrentUser` when parsing fails.

## Trust Gate, Connection Lifecycle, And Cert-Changed Blocking

`IConsoleConnectionManager` (native-only) wraps the connection factory with a server-bound trust gate:

1. Resolve the bound client certificate (if any) and observe the server's TLS certificate fingerprint via `IConsoleServerCertificateProbe` (a TLS handshake; the server exposes no fingerprint endpoint, so pinning is client-side).
2. When a client certificate is bound, validate it against the real server through `IConsoleClientCertificateValidationClient` - a thin `HttpClient` that posts the certificate's public PEM to honua-server#1171's `POST /api/v1/admin/security/client-certificates/validate` and maps the stable `client_certificate_*` codes onto `HonuaCertificateValidationStatus`.
3. `ConsoleTrustEvaluator` (pure, host-agnostic) decides the resulting `HonuaEnvironmentTrustState` and whether to **block**:
   - the observed server fingerprint differs from the pinned (acknowledged) value (`server_certificate_changed`),
   - the bound client-certificate identity changed (`client_certificate_changed`), or
   - validation returned a blocking status (`Untrusted`, `Rejected`, `WrongEnvironment`, `Expired`, `Missing`).
4. A blocked result returns **no usable connection**; the profile state records `TrustBlocked = true`. **Acknowledge** re-pins the newly observed server identity and clears the block; **Revalidate** re-runs validation against the server. First use pins on a trust-on-first-use basis. `ExpiringSoon` is a non-blocking warning.

Private keys are never persisted; only certificate references and the sanitized `HonuaEnvironmentTrustState` (status, server fingerprint, issuer summary, reason code, sanitized message) are stored. Server-side mTLS policy, trust registration, revocation, and capability advertisement are honua-server#1171 (closed) and remain Scope Out of the Console UI work.

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

`NativeGrpcTelemetryStreamingProof` returns no events when `TransportCapabilities.NativeGrpc` is disabled. For enabled profiles, the deterministic fixture emits three events and the route saves the final resume token, last route `/operate/native-stream`, connection timestamp, proof name, and final transport in profile state. This is CI/local smoke evidence for native host wiring until shared SDK/server job or telemetry streams are available.

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
- trust-gate cert-changed blocking, acknowledge/revalidate, and stable-code → trust-status mapping (`ConsoleTrustEvaluatorTests`, `ConsoleConnectionManagerTests`)

### Real-server trust integration lane (opt-in)

`tests/Honua.Console.IntegrationTests` boots a real `honua-server` (with PostgreSQL) via Testcontainers, configures mTLS, and asserts client-certificate validation and cert-changed blocking against live data (Console Patterns Charter section 11). It is **off by default** and skips every fact with a clear reason unless opted in. It is intentionally **not** part of `fast-local-check.sh`. Run it with:

```bash
HONUA_CONSOLE_INTEGRATION=true \
HONUA_CONSOLE_SERVER_IMAGE=<honua-server image with honua-server#1171> \
HONUA_CONSOLE_ADMIN_TOKEN=<admin bearer token> \
./scripts/integration-trust-check.sh
```

Or target an already-running server with `HONUA_CONSOLE_EXTERNAL_BASE_URL`. See the script header for the full set of optional environment variables.
