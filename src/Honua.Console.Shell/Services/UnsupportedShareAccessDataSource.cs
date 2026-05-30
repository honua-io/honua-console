using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Share management data source used when no honua-server base address is configured. It renders an
/// explicit missing-binding state instead of fabricating share data, keeping the merged runtime free of a
/// standing in-memory Console Share client (Console Patterns Charter section 11). The Share management
/// surface stays bound to the real honua-server Console Share API (honua-server#1215) or shows this state.
/// </summary>
public sealed class UnsupportedShareAccessDataSource : IShareAccessDataSource
{
    private const string Surface = "Share access";

    private static readonly ShareCapabilityState MissingBinding = new(
        Surface,
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the Share panel can bind the server-owned Console Share access API (honua-server#1215).");

    public Task<ShareAccessLoad> LoadAsync(string itemId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ShareAccessLoad(null, [MissingBinding]));

    public Task<ShareCommandResult> UpdateAccessTierAsync(
        string itemId,
        string accessTier,
        bool allowDependencyConflicts,
        CancellationToken cancellationToken = default) =>
        MissingBindingCommand();

    public Task<ShareCommandResult> PreviewDependenciesAsync(
        string itemId,
        string targetTier,
        CancellationToken cancellationToken = default) =>
        MissingBindingCommand();

    public Task<ShareCommandResult> MintPublicLinkAsync(
        string itemId,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken = default) =>
        MissingBindingCommand();

    public Task<ShareCommandResult> RevokePublicLinkAsync(
        string itemId,
        string tokenId,
        CancellationToken cancellationToken = default) =>
        MissingBindingCommand();

    public Task<ShareCommandResult> SetEmbedAsync(
        string itemId,
        bool enabled,
        string? audience,
        bool allowDependencyConflicts,
        CancellationToken cancellationToken = default) =>
        MissingBindingCommand();

    public Task<ShareCommandResult> MintEmbedTokenAsync(
        string itemId,
        string audience,
        int? ttlSeconds,
        CancellationToken cancellationToken = default) =>
        MissingBindingCommand();

    private static Task<ShareCommandResult> MissingBindingCommand() =>
        Task.FromResult(ShareCommandResult.FromStates([MissingBinding]));
}
