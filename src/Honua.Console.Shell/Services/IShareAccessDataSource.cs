using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IShareAccessDataSource
{
    Task<ShareAccessLoad> LoadAsync(string itemId, CancellationToken cancellationToken = default);

    Task<ShareCommandResult> UpdateAccessTierAsync(
        string itemId,
        string accessTier,
        bool allowDependencyConflicts,
        CancellationToken cancellationToken = default);

    Task<ShareCommandResult> PreviewDependenciesAsync(
        string itemId,
        string targetTier,
        CancellationToken cancellationToken = default);

    Task<ShareCommandResult> MintPublicLinkAsync(
        string itemId,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken = default);

    Task<ShareCommandResult> RevokePublicLinkAsync(
        string itemId,
        string tokenId,
        CancellationToken cancellationToken = default);

    Task<ShareCommandResult> SetEmbedAsync(
        string itemId,
        bool enabled,
        string? audience,
        bool allowDependencyConflicts,
        CancellationToken cancellationToken = default);

    Task<ShareCommandResult> MintEmbedTokenAsync(
        string itemId,
        string audience,
        int? ttlSeconds,
        CancellationToken cancellationToken = default);
}
