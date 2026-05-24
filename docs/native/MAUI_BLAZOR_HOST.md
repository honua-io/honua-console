# Optional MAUI Blazor Hybrid Host

Honua Console remains a browser-deployable Blazor web app. The native host is an optional operator and power-user shell that renders the same shared Razor routes from `Honua.Console.Shell` and adds native profile, certificate, and gRPC wiring.

## Project Layout

- `src/Honua.Console.Shell`: shared Razor component library for the Console shell, route map, environment profile model, account/RBAC session model, and native streaming proof contract.
- `src/Honua.Console.Web`: independently deployable browser Console host. It references `Honua.Console.Shell` only.
- `src/Honua.Console.Native.Core`: testable native services for persisted environment profiles, account-token sessions, mTLS certificate references, native HTTP/gRPC connection setup, and the deterministic telemetry streaming proof.
- `src/Honua.Console.Native`: optional .NET MAUI Blazor Hybrid host. It renders `Honua.Console.Shell.ConsoleRoutes` in a `BlazorWebView` and stores profile/session material through MAUI secure storage.

## Environment Profiles

The native profile store seeds two environments, `dev` and `staging`, and supports adding more with distinct profile state:

- server base URL
- environment kind and tenant ID
- browser HTTP/realtime capabilities
- native gRPC capability
- optional native mTLS capability
- account/RBAC binding metadata
- environment-specific certificate reference
- last route and streaming resume token state

The profile contract is Console-owned UI state, not a duplicate of server API DTOs. Account authorization remains bearer-token account/RBAC based; mTLS is an optional per-environment transport trust layer.

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
dotnet test tests/Honua.Console.Native.Core.Tests/Honua.Console.Native.Core.Tests.csproj
dotnet build src/Honua.Console.Web/Honua.Console.Web.csproj
```

The native MAUI desktop app must be compiled on Windows or macOS. Linux command-line builds use the project fallback Android target for source validation and require an Android SDK, which is not part of the desktop host acceptance path.
