using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

// Console-side projections of the honua-server Console Share access surface (honua-server#1215),
// scoped to the authenticated Share management page. These are immutable view records the Share panel
// renders; they carry no local share state and never fabricate share data — when the server is unbound,
// the item is missing, or a change is rejected/blocked, the surface renders a capability state instead
// (Console Patterns Charter section 11).

public sealed record ShareCapabilityState(
    string Surface,
    string State,
    string Contract,
    string Detail);

public sealed record ShareAccessView(
    string ItemId,
    string ItemName,
    string? ItemTitle,
    string ItemType,
    string? OwnerId,
    string AccessTier,
    bool PublicLinkEnabled,
    bool EmbedEnabled,
    string? EmbedAudience,
    bool OpenDataEligible,
    bool AnonymousEligible,
    bool CanShare,
    bool CanEmbed,
    bool CanAdminister,
    IReadOnlyList<SharePublicLinkView> PublicLinkTokens,
    DateTimeOffset? UpdatedAt,
    string? UpdatedById)
{
    public string DisplayTitle => string.IsNullOrWhiteSpace(ItemTitle) ? ItemName : ItemTitle!;

    public bool IsPublic => HonuaConsoleShareAccessTiers.IsPublic(AccessTier);
}

// The token value is never carried on this view.
public sealed record SharePublicLinkView(
    string TokenId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string? CreatedById,
    bool IsExpired);

// The server discloses this opaque value exactly once.
public sealed record ShareMintedSecret(
    string Kind,
    string TokenId,
    string Token,
    DateTimeOffset? ExpiresAt,
    string? Audience);

public sealed record ShareDependencyConflictView(
    string ItemId,
    string? ItemType,
    string? ItemName,
    string BlockingReason);

public sealed record ShareDependencyClosureView(
    string ItemId,
    string TargetTier,
    bool IsCompatible,
    IReadOnlyList<ShareDependencyConflictView> Conflicts);

public sealed record ShareAccessLoad(
    ShareAccessView? Share,
    IReadOnlyList<ShareCapabilityState> CapabilityStates)
{
    public bool HasShare => Share is not null;
}

public sealed record ShareCommandResult(
    ShareAccessView? Share,
    ShareMintedSecret? MintedSecret,
    ShareDependencyClosureView? BlockedBy,
    IReadOnlyList<ShareCapabilityState> CapabilityStates)
{
    public bool Succeeded => CapabilityStates.Count == 0 && BlockedBy is null;

    public static ShareCommandResult FromStates(IReadOnlyList<ShareCapabilityState> states) =>
        new(null, null, null, states);
}

public static class ShareAccessMapper
{
    public static ShareAccessView ToView(HonuaConsoleShareProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        // System.Text.Json overrides the [] initializer with null when the server emits an explicit JSON null
        // for the key; coalesce before LINQ (matches every other deserialized-DTO mapper on this surface).
        var permissions = projection.CallerPermissions ?? [];
        var tokens = (projection.PublicLinkTokens ?? [])
            .Select(token => new SharePublicLinkView(
                TokenId: token.TokenId,
                CreatedAt: token.CreatedAt,
                ExpiresAt: token.ExpiresAt,
                CreatedById: token.CreatedById,
                IsExpired: token.IsExpired))
            .OrderByDescending(token => token.CreatedAt)
            .ToArray();

        return new ShareAccessView(
            ItemId: projection.ItemId,
            ItemName: projection.ItemName,
            ItemTitle: projection.ItemTitle,
            ItemType: projection.ItemType,
            OwnerId: projection.OwnerId,
            AccessTier: projection.AccessTier,
            PublicLinkEnabled: projection.PublicLinkEnabled,
            EmbedEnabled: projection.EmbedEnabled,
            EmbedAudience: projection.EmbedAudience,
            OpenDataEligible: projection.OpenDataEligible,
            AnonymousEligible: projection.AnonymousEligible,
            CanShare: HasPermission(permissions, "share"),
            CanEmbed: HasPermission(permissions, "embed"),
            CanAdminister: HasPermission(permissions, "administer"),
            PublicLinkTokens: tokens,
            UpdatedAt: projection.UpdatedAt,
            UpdatedById: projection.UpdatedById);
    }

    public static ShareDependencyClosureView ToView(HonuaConsoleShareDependencyClosure closure)
    {
        ArgumentNullException.ThrowIfNull(closure);

        return new ShareDependencyClosureView(
            ItemId: closure.ItemId,
            TargetTier: closure.TargetTier,
            IsCompatible: closure.IsCompatible,
            Conflicts: (closure.Conflicts ?? [])
                .Select(conflict => new ShareDependencyConflictView(
                    ItemId: conflict.ItemId,
                    ItemType: conflict.ItemType,
                    ItemName: conflict.ItemName,
                    BlockingReason: conflict.BlockingReason))
                .ToArray());
    }

    private static bool HasPermission(IReadOnlyList<string> permissions, string verb) =>
        permissions.Any(permission => string.Equals(permission, verb, StringComparison.Ordinal));
}
