using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Security;

public sealed class NativeSecretStoreAccountTokenProvider : IConsoleAccountTokenProvider
{
    private readonly IConsoleAccountSessionStore _sessions;

    public NativeSecretStoreAccountTokenProvider(IConsoleAccountSessionStore sessions)
    {
        _sessions = sessions;
    }

    public async ValueTask<string?> GetAccessTokenAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Account.AuthMode == ConsoleAccountAuthMode.Anonymous)
        {
            return null;
        }

        var session = await _sessions.GetSessionAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(session?.AccessToken) ? null : session.AccessToken;
    }
}
