using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleCatalogReadContextResolver
{
    Task<CatalogReadContext> ResolveAsync(
        string? publicLinkToken,
        CancellationToken cancellationToken = default);
}

public sealed class ConsoleCatalogReadContextResolver : IConsoleCatalogReadContextResolver
{
    private readonly IConsoleEnvironmentProfileStore _profiles;
    private readonly IConsoleAccountSessionStore _sessions;

    public ConsoleCatalogReadContextResolver(
        IConsoleEnvironmentProfileStore profiles,
        IConsoleAccountSessionStore sessions)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public async Task<CatalogReadContext> ResolveAsync(
        string? publicLinkToken,
        CancellationToken cancellationToken = default)
    {
        if (await HasSignedInSessionAsync(cancellationToken).ConfigureAwait(false))
        {
            return CatalogReadContext.Authenticated;
        }

        if (!string.IsNullOrWhiteSpace(publicLinkToken))
        {
            return CatalogReadContext.AnonymousPublicLink(publicLinkToken);
        }

        return CatalogReadContext.AnonymousPublicLink(null);
    }

    private async Task<bool> HasSignedInSessionAsync(CancellationToken cancellationToken)
    {
        var activeProfile = await _profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (activeProfile is null || activeProfile.Account.AuthMode == ConsoleAccountAuthMode.Anonymous)
        {
            return false;
        }

        var session = await _sessions.GetSessionAsync(activeProfile.Id, cancellationToken).ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(session?.AccessToken);
    }
}
