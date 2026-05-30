using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Console Share management data source. Binds the authenticated Share access surface — projection read,
/// access-tier change, dependency-closure preview, public-link mint/revoke, and embed enablement/token
/// mint — to the real honua-server Console Share API (honua-server#1215) through the Honua.Console.Contracts
/// shim. There is no standing in-memory share client in the merged result (Console Patterns Charter
/// section 11): when no server base URL is configured, the unsupported implementation surfaces an explicit
/// missing-binding state rather than fabricating share data.
/// </summary>
public interface IShareAccessDataSource
{
    /// <summary>Loads the server-owned Share projection for an item, or capability states when it cannot be read.</summary>
    Task<ShareAccessLoad> LoadAsync(string itemId, CancellationToken cancellationToken = default);

    /// <summary>Changes the item's Share access tier, returning the refreshed projection, a block, or states.</summary>
    Task<ShareCommandResult> UpdateAccessTierAsync(
        string itemId,
        string accessTier,
        bool allowDependencyConflicts,
        CancellationToken cancellationToken = default);

    /// <summary>Previews whether the item's provenance closure is shareable by a target tier (no commit).</summary>
    Task<ShareCommandResult> PreviewDependenciesAsync(
        string itemId,
        string targetTier,
        CancellationToken cancellationToken = default);

    /// <summary>Mints a public-link token, surfacing the opaque value exactly once.</summary>
    Task<ShareCommandResult> MintPublicLinkAsync(
        string itemId,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes a public-link token by id and reloads the projection.</summary>
    Task<ShareCommandResult> RevokePublicLinkAsync(
        string itemId,
        string tokenId,
        CancellationToken cancellationToken = default);

    /// <summary>Enables or disables embedding with an audience scope.</summary>
    Task<ShareCommandResult> SetEmbedAsync(
        string itemId,
        bool enabled,
        string? audience,
        bool allowDependencyConflicts,
        CancellationToken cancellationToken = default);

    /// <summary>Mints an embed token, surfacing the opaque value exactly once.</summary>
    Task<ShareCommandResult> MintEmbedTokenAsync(
        string itemId,
        string audience,
        int? ttlSeconds,
        CancellationToken cancellationToken = default);
}
