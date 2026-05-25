using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Connections;

/// <summary>
/// Native-host capabilities: native gRPC, native mTLS, client-certificate selection, and
/// server-bound trust validation are all available. Registered by <c>AddHonuaConsoleNativeCore()</c>,
/// replacing the browser default.
/// </summary>
public sealed class NativeConsoleHostCapabilities : IConsoleHostCapabilities
{
    public string HostKind => "native";

    public bool SupportsNativeTransports => true;
}
