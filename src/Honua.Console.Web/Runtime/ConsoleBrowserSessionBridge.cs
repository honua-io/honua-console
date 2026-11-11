using Microsoft.JSInterop;

namespace Honua.Console.Web.Runtime;

public sealed class ConsoleBrowserSessionBridge(ConsoleBrowserSession session)
{
    [JSInvokable]
    public ConsoleBrowserSessionSnapshot GetSnapshot() => new(
        session.Revision,
        session.IsAuthenticated,
        session.Principal.AccountId(),
        session.IsAuthenticated ? session.AccessToken : null,
        session.IsAuthenticated ? session.CsrfToken : null,
        session.ExpiresAt);
}

public sealed record ConsoleBrowserSessionSnapshot(
    long Revision,
    bool Authenticated,
    string? PrincipalId,
    string? AccessToken,
    string? CsrfToken,
    DateTimeOffset? ExpiresAt);
