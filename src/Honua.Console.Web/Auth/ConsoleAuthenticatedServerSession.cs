using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Web.Auth;

internal sealed class ConsoleAuthenticatedServerSession(
    ConsoleServerSessionPartition partition,
    ConsoleEnvironmentProfile boundProfile,
    string operatorKey,
    ConsoleServerSessionIdentityBinding? identityBinding,
    IDisposable authentication) : IConsoleOperatorBearerExchangeLease
{
    private IDisposable? _authentication = authentication;

    internal async Task<ConsoleServerSessionPartition?> VerifyAsync(
        ConsoleEnvironmentProfile profile, CancellationToken cancellationToken)
    {
        if (!MatchesScope(profile))
            return null;
        return identityBinding is null || await identityBinding.EnsureAsync(
            partition, profile, operatorKey, partition.AuthenticatedProviderKey, cancellationToken).ConfigureAwait(false)
                ? partition : null;
    }

    public async Task<ConsoleOperatorBearerExchangeResult> ExchangeAsync(
        ConsoleEnvironmentProfile profile, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!MatchesScope(profile)) return ConsoleOperatorBearerExchangeResult.Unavailable(
                "The authenticated environment scope changed.");
            var verified = await VerifyAsync(profile, cancellationToken).ConfigureAwait(false);
            if (verified is null)
                return ConsoleOperatorBearerExchangeResult.Denied(
                    "The server session does not match the authenticated Console operator or environment. Sign in again.");
            return await new HttpConsoleOperatorBearerExchange(verified.Client)
                .ExchangeAsync(profile, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return ConsoleOperatorBearerExchangeResult.Unavailable(
                "The per-operator honua-server session is unavailable. Sign in to the server again.");
        }
    }

    private bool MatchesScope(ConsoleEnvironmentProfile profile) => _authentication is not null
        && profile.Id == boundProfile.Id && profile.ServerBaseUri == boundProfile.ServerBaseUri;

    public void Dispose() => Interlocked.Exchange(ref _authentication, null)?.Dispose();
}
