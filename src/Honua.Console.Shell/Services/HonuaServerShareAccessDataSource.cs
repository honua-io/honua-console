using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// An HTTP 409 on a public-tier or embed change re-previews the dependency closure so the panel can show the blocking dependencies.
public sealed class HonuaServerShareAccessDataSource : IShareAccessDataSource
{
    private const string Surface = "Share access";

    private readonly IHonuaConsoleShareClient _client;

    public HonuaServerShareAccessDataSource(IHonuaConsoleShareClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ShareAccessLoad> LoadAsync(string itemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        var result = await _client.GetShareAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new ShareAccessLoad(null, [ToCapabilityState(issue)]);
        }

        return new ShareAccessLoad(ShareAccessMapper.ToView(result.Data!), []);
    }

    public async Task<ShareCommandResult> UpdateAccessTierAsync(
        string itemId,
        string accessTier,
        bool allowDependencyConflicts,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessTier);

        var result = await _client.UpdateAccessTierAsync(
            itemId,
            new HonuaUpdateShareAccessRequest { AccessTier = accessTier, AllowDependencyConflicts = allowDependencyConflicts },
            cancellationToken).ConfigureAwait(false);

        return await ProjectionOrBlockedAsync(itemId, accessTier, result, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ShareCommandResult> PreviewDependenciesAsync(
        string itemId,
        string targetTier,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTier);

        var result = await _client.PreviewDependenciesAsync(itemId, targetTier, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return ShareCommandResult.FromStates([ToCapabilityState(issue)]);
        }

        return new ShareCommandResult(null, null, ShareAccessMapper.ToView(result.Data!), []);
    }

    public async Task<ShareCommandResult> MintPublicLinkAsync(
        string itemId,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        var result = await _client.MintPublicLinkAsync(
            itemId,
            new HonuaMintPublicLinkRequest { ExpiresAt = expiresAt },
            cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return ShareCommandResult.FromStates([ToCapabilityState(issue)]);
        }

        var token = result.Data!;
        var secret = new ShareMintedSecret(
            Kind: "public-link",
            TokenId: token.TokenId,
            Token: token.Token ?? string.Empty,
            ExpiresAt: token.ExpiresAt,
            Audience: null);

        // Reload the projection so the token panel reflects the new active token alongside the one-time secret.
        var reload = await LoadAsync(itemId, cancellationToken).ConfigureAwait(false);
        return new ShareCommandResult(reload.Share, secret, null, reload.CapabilityStates);
    }

    public async Task<ShareCommandResult> RevokePublicLinkAsync(
        string itemId,
        string tokenId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);

        var result = await _client.RevokePublicLinkAsync(itemId, tokenId, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return ShareCommandResult.FromStates([ToCapabilityState(issue)]);
        }

        var reload = await LoadAsync(itemId, cancellationToken).ConfigureAwait(false);
        return new ShareCommandResult(reload.Share, null, null, reload.CapabilityStates);
    }

    public async Task<ShareCommandResult> SetEmbedAsync(
        string itemId,
        bool enabled,
        string? audience,
        bool allowDependencyConflicts,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        var result = await _client.SetEmbedAsync(
            itemId,
            new HonuaSetEmbedRequest { Enabled = enabled, Audience = audience, AllowDependencyConflicts = allowDependencyConflicts },
            cancellationToken).ConfigureAwait(false);

        // Enabling embed is dependency-closure validated against public-link strength; surface a block the
        // same way an access-tier change does so the panel can render the conflicts.
        return await ProjectionOrBlockedAsync(itemId, HonuaConsoleShareAccessTiers.PublicLink, result, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ShareCommandResult> MintEmbedTokenAsync(
        string itemId,
        string audience,
        int? ttlSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);

        var result = await _client.MintEmbedTokenAsync(
            itemId,
            new HonuaMintEmbedTokenRequest { Audience = audience, TtlSeconds = ttlSeconds },
            cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return ShareCommandResult.FromStates([ToCapabilityState(issue)]);
        }

        var token = result.Data!;
        var secret = new ShareMintedSecret(
            Kind: "embed",
            TokenId: token.TokenId,
            Token: token.Token ?? string.Empty,
            ExpiresAt: token.ExpiresAt,
            Audience: token.Audience);

        var reload = await LoadAsync(itemId, cancellationToken).ConfigureAwait(false);
        return new ShareCommandResult(reload.Share, secret, null, reload.CapabilityStates);
    }

    // Maps the result of a change that returns the refreshed projection. A 409 ("Conflict") is the
    // dependency-closure block: re-preview the closure so the panel renders the blocking dependencies
    // instead of only the server message.
    private async Task<ShareCommandResult> ProjectionOrBlockedAsync(
        string itemId,
        string closureTier,
        HonuaAdminEndpointResult<HonuaConsoleShareProjection> result,
        CancellationToken cancellationToken)
    {
        if (result.Issue is null)
        {
            return new ShareCommandResult(ShareAccessMapper.ToView(result.Data!), null, null, []);
        }

        var issue = result.Issue;
        if (string.Equals(issue.State, "Conflict", StringComparison.Ordinal))
        {
            var closure = await _client.PreviewDependenciesAsync(itemId, closureTier, cancellationToken).ConfigureAwait(false);
            if (closure.Issue is null)
            {
                return new ShareCommandResult(null, null, ShareAccessMapper.ToView(closure.Data!), []);
            }
        }

        return ShareCommandResult.FromStates([ToCapabilityState(issue)]);
    }

    private static ShareCapabilityState ToCapabilityState(HonuaAdminEndpointIssue issue) =>
        new(
            Surface,
            issue.State,
            issue.Contract,
            issue.StatusCode is null
                ? issue.Detail
                : $"{issue.Detail} HTTP {issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}.");
}
