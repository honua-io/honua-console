using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Web.Auth;

internal sealed class ConsoleAuthenticatedServerSession(
    ConsoleServerSessionIdentityRequest identity,
    ConsoleServerSessionIdentityBinding? identityBinding,
    IDisposable authentication) : IConsoleOperatorBearerExchangeLease
{
    private IDisposable? _authentication = authentication;

    internal async Task<ConsoleServerSessionPartition?> VerifyBrowserAsync(
        ConsoleApprovalBrowserVerification request, CancellationToken cancellationToken)
    {
        var target = new ConsoleHumanBrowserTarget
        {
            SelectedProfile = request.Target.Profile,
            AuthorityProfile = request.BoundProfile,
            Authority = request.Target.Authority,
            Partition = identity.Partition
        };
        if (request.Channel?.Authorizes(target) != true) return null;
        var partition = await VerifyAsync(request.BoundProfile, cancellationToken).ConfigureAwait(false);
        return partition is not null && request.Channel.Authorizes(target) ? partition : null;
    }

    internal async Task<ConsoleServerSessionPartition?> VerifyAsync(
        ConsoleEnvironmentProfile profile, CancellationToken cancellationToken)
    {
        if (!MatchesScope(profile))
            return null;
        if (identityBinding is null) return identity.Partition;
        var request = identity with { Profile = profile, ProviderKey = identity.Partition.AuthenticatedProviderKey };
        var valid = await identityBinding.EnsureAsync(request, cancellationToken).ConfigureAwait(false);
        return valid ? identity.Partition : null;
    }

    public async Task<ConsoleOperatorBearerExchangeResult> ExchangeAsync(
        ConsoleEnvironmentProfile profile, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!MatchesScope(profile) || identity.Authority != ConsoleServerAuthority.Serving) return ConsoleOperatorBearerExchangeResult.Unavailable(
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
        && profile.Id == identity.Profile.Id && profile.ServerBaseUri == identity.Profile.ServerBaseUri;

    public void Dispose() => Interlocked.Exchange(ref _authentication, null)?.Dispose();
}

internal sealed record ConsoleApprovalBrowserVerification(
    ConsoleHumanProposalTarget Target, ConsoleEnvironmentProfile BoundProfile, ConsoleApprovalBrowserChannel? Channel);
