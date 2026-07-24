using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleCatalogReadContextResolver
{
    Task<CatalogReadContext> ResolveAsync(
        string? publicLinkToken,
        CancellationToken cancellationToken = default);

    async Task<ConsoleCatalogReadAccess> ResolveAccessAsync(
        string? publicLinkToken,
        CancellationToken cancellationToken = default)
    {
        var context = await ResolveAsync(publicLinkToken, cancellationToken).ConfigureAwait(false);
        return new ConsoleCatalogReadAccess(
            context,
            context.Anonymous
                ? ConsoleCatalogAccessState.SignInRequired
                : ConsoleCatalogAccessState.Authenticated);
    }
}

public enum ConsoleCatalogAccessState
{
    Authenticated,
    NoActiveEnvironment,
    SignInRequired,
    PublicLink,
}

public sealed record ConsoleCatalogReadAccess(
    CatalogReadContext Context,
    ConsoleCatalogAccessState State);

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
        CancellationToken cancellationToken = default) =>
        (await ResolveAccessAsync(publicLinkToken, cancellationToken).ConfigureAwait(false)).Context;

    public async Task<ConsoleCatalogReadAccess> ResolveAccessAsync(
        string? publicLinkToken,
        CancellationToken cancellationToken = default)
    {
        var activeProfile = await _profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (activeProfile is null)
        {
            return PublicLinkOr(
                publicLinkToken,
                CatalogReadContext.AnonymousPublicLink(null),
                ConsoleCatalogAccessState.NoActiveEnvironment);
        }

        if (activeProfile.Account.AuthMode != ConsoleAccountAuthMode.Anonymous)
        {
            var session = await _sessions.GetSessionAsync(activeProfile.Id, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(session?.AccessToken))
            {
                return new ConsoleCatalogReadAccess(
                    CatalogReadContext.Authenticated,
                    ConsoleCatalogAccessState.Authenticated);
            }
        }

        return PublicLinkOr(
            publicLinkToken,
            CatalogReadContext.AnonymousPublicLink(null),
            ConsoleCatalogAccessState.SignInRequired);
    }

    private static ConsoleCatalogReadAccess PublicLinkOr(
        string? publicLinkToken,
        CatalogReadContext fallback,
        ConsoleCatalogAccessState fallbackState)
    {
        return !string.IsNullOrWhiteSpace(publicLinkToken)
            ? new ConsoleCatalogReadAccess(
                CatalogReadContext.AnonymousPublicLink(publicLinkToken),
                ConsoleCatalogAccessState.PublicLink)
            : new ConsoleCatalogReadAccess(fallback, fallbackState);
    }
}
