using Microsoft.JSInterop;

namespace Honua.Console.Web.Runtime;

public sealed class ConsoleBrowserSessionBridge(ConsoleBrowserSession session)
{
    [JSInvokable]
    public ConsoleBrowserSessionSnapshot GetSnapshot() => session.Snapshot();

    [JSInvokable]
    public void InvalidateSession(long expectedRevision) => session.ClearIfRevision(expectedRevision);
}

public sealed record ConsoleBrowserSessionSnapshot(
    long Revision,
    bool Authenticated,
    string? PrincipalId,
    string? AccessToken,
    string? CsrfToken,
    DateTimeOffset? ExpiresAt);
