using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

// Console-side projections of the honua-server Console Share access surface (honua-server#1215),
// scoped to the authenticated Share management page. These are immutable view records the Share panel
// renders; they carry no local share state and never fabricate share data — when the server is unbound,
// the item is missing, or a change is rejected/blocked, the surface renders a capability state instead
// (Console Patterns Charter section 11).

/// <summary>
/// A binding/permission/conflict/missing surface for the Share management page, mirroring the report and
/// form builder capability-state pattern so missing bindings, missing permissions, rejected requests,
/// dependency-closure conflicts, and unsupported contracts render consistently.
/// </summary>
public sealed record ShareCapabilityState(
    string Surface,
    string State,
    string Contract,
    string Detail);

/// <summary>
/// The server-owned Share access projection of a content item: access tier, public-link and embed state,
/// open-data eligibility, anonymous eligibility, the caller's share/embed/administer permissions, and the
/// active public-link tokens. This is the read model the Share panel renders from without resolving share
/// state client-side.
/// </summary>
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

/// <summary>One public-link token projected for the token panel. Token value is never carried here.</summary>
public sealed record SharePublicLinkView(
    string TokenId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string? CreatedById,
    bool IsExpired);

/// <summary>
/// A freshly minted secret returned by a mint command. The opaque value is disclosed exactly once by the
/// server, so the page surfaces it once for copy and never re-reads it.
/// </summary>
public sealed record ShareMintedSecret(
    string Kind,
    string TokenId,
    string Token,
    DateTimeOffset? ExpiresAt,
    string? Audience);

/// <summary>A single dependency blocking a public sharing or embed enablement request.</summary>
public sealed record ShareDependencyConflictView(
    string ItemId,
    string? ItemType,
    string? ItemName,
    string BlockingReason);

/// <summary>Result of a dependency-closure preview against a target tier.</summary>
public sealed record ShareDependencyClosureView(
    string ItemId,
    string TargetTier,
    bool IsCompatible,
    IReadOnlyList<ShareDependencyConflictView> Conflicts);

/// <summary>
/// Result of loading the Share projection for an item: the view when the server returned data, otherwise
/// the capability states explaining why (missing binding, missing permission, not found, unsupported).
/// </summary>
public sealed record ShareAccessLoad(
    ShareAccessView? Share,
    IReadOnlyList<ShareCapabilityState> CapabilityStates)
{
    public bool HasShare => Share is not null;
}

/// <summary>
/// Result of a Share mutation (access tier change, embed toggle): the refreshed projection on success, a
/// minted secret when one was produced, a dependency closure when blocked, or capability states on failure.
/// </summary>
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

/// <summary>Maps the honua-server Console Share wire records to the Share management view records.</summary>
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
