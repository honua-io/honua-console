using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Tests;

// System.Text.Json replaces a collection initializer with null only when the payload sends explicit JSON null, so every share projection coalesces wire collections before LINQ.
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
