namespace Honua.Console.Shell.Services;

/// <summary>
/// Reports which transports and trust features the current Console host supports so shared
/// Razor routes can render native-only capabilities (native gRPC, native mTLS, certificate
/// selection, trust validation) as unsupported on the browser host without taking a native
/// dependency. The browser host keeps the default <see cref="BrowserConsoleHostCapabilities"/>;
/// the native host replaces it through <c>AddHonuaConsoleNativeCore()</c>.
/// </summary>
public interface IConsoleHostCapabilities
{
    /// <summary>Stable host kind identifier: <c>browser</c> or <c>native</c>.</summary>
    string HostKind { get; }

    /// <summary>
    /// Whether native transports and trust features (native gRPC, native mTLS, client-certificate
    /// selection, and server-bound trust validation) are available on this host.
    /// </summary>
    bool SupportsNativeTransports { get; }
}

/// <summary>
/// Default browser-host capabilities. Native gRPC, native mTLS, certificate selection, and
/// trust validation are unsupported; the browser keeps HTTP and browser realtime transports.
/// </summary>
public sealed class BrowserConsoleHostCapabilities : IConsoleHostCapabilities
{
    public string HostKind => "browser";

    public bool SupportsNativeTransports => false;
}
