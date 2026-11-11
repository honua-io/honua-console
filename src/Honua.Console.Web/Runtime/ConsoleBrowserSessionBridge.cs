using Microsoft.JSInterop;

namespace Honua.Console.Web.Runtime;

public sealed class ConsoleBrowserSessionBridge(ConsoleBrowserSession session)
{
    [JSInvokable]
    public ConsoleBrowserSessionSnapshot GetSnapshot() => session.Snapshot();
}

public sealed record ConsoleBrowserSessionSnapshot(
    long Revision,
    bool Authenticated,
    string? PrincipalId,
    string? AccessToken,
    string? CsrfToken,
    DateTimeOffset? ExpiresAt);
