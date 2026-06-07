using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Null-safety coverage for <see cref="ShareAccessMapper"/> against the deserialized-DTO contract: every
/// share read/mutation re-projects the server wire records through <c>ToView</c>, and System.Text.Json
/// overrides a collection's <c>[]</c> initializer with null when the server emits an explicit JSON
/// <c>null</c> for the key. The mapper must coalesce before LINQ rather than throwing and tearing down the
/// Blazor circuit (Console Patterns Charter section 11 — bind the real server, never throw on an
/// honest-but-empty payload).
/// </summary>
public sealed class ShareAccessMapperTests
{
    [Fact]
    public void ToView_Projection_NullCollections_ProjectsEmptyViewWithoutThrowing()
    {
        var projection = new HonuaConsoleShareProjection
        {
            ItemId = "item-1",
            ItemName = "Parcels",
            ItemType = "feature-service",
            AccessTier = HonuaConsoleShareAccessTiers.Private,
            // Server emitted explicit JSON null for both collection keys.
            PublicLinkTokens = null!,
            CallerPermissions = null!
        };

        var view = ShareAccessMapper.ToView(projection);

        Assert.Equal("item-1", view.ItemId);
        Assert.Empty(view.PublicLinkTokens);
        // Permissions absent => no capability granted, rather than an NRE.
        Assert.False(view.CanShare);
        Assert.False(view.CanEmbed);
        Assert.False(view.CanAdminister);
    }

    [Fact]
    public void ToView_DependencyClosure_NullConflicts_ProjectsEmptyViewWithoutThrowing()
    {
        var closure = new HonuaConsoleShareDependencyClosure
        {
            ItemId = "item-1",
            TargetTier = HonuaConsoleShareAccessTiers.PublicLink,
            IsCompatible = true,
            // Server emitted explicit JSON null for "conflicts".
            Conflicts = null!
        };

        var view = ShareAccessMapper.ToView(closure);

        Assert.Equal("item-1", view.ItemId);
        Assert.True(view.IsCompatible);
        Assert.Empty(view.Conflicts);
    }
}
