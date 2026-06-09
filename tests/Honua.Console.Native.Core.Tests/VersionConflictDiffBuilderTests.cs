using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Unit coverage for <see cref="VersionConflictDiffBuilder"/>, the pure projection that turns the server's raw
/// three-way conflict images into the operator-facing 3-way diff view used by the conflict-resolution surface
/// (honua-console#177). Pins the changed-flag computation that drives the per-field and geometry highlighting:
/// a field/geometry is "changed" exactly when its base/DEFAULT/version images are not all equal.
/// </summary>
public sealed class VersionConflictDiffBuilderTests
{
    [Fact]
    public void Build_FlagsDivergingField_AndUnchangedField()
    {
        var conflict = new HonuaVersionConflict
        {
            LayerId = 3,
            ObjectId = 42,
            ConflictType = "attribute",
            FieldDiffs =
            [
                new HonuaVersionConflictFieldDiff { Name = "status", Base = "open", Default = "open", Version = "closed" },
                new HonuaVersionConflictFieldDiff { Name = "owner", Base = "alex", Default = "alex", Version = "alex" }
            ]
        };

        var view = VersionConflictDiffBuilder.Build(conflict);

        Assert.Equal("3:42", view.Key);
        var status = view.FieldDiffs.Single(f => f.Name == "status");
        Assert.True(status.Changed);
        var owner = view.FieldDiffs.Single(f => f.Name == "owner");
        Assert.False(owner.Changed);
    }

    [Fact]
    public void Build_FlagsGeometryChange_WhenWktDiffers()
    {
        var conflict = new HonuaVersionConflict
        {
            LayerId = 1,
            ObjectId = 7,
            ConflictType = "geometry",
            BaseGeometry = "POINT(0 0)",
            DefaultGeometry = "POINT(0 0)",
            VersionGeometry = "POINT(1 1)"
        };

        var view = VersionConflictDiffBuilder.Build(conflict);

        Assert.True(view.GeometryChanged);
    }

    [Fact]
    public void Build_DoesNotFlagGeometry_WhenAllImagesEqual()
    {
        var conflict = new HonuaVersionConflict
        {
            LayerId = 1,
            ObjectId = 7,
            ConflictType = "attribute",
            BaseGeometry = "POINT(0 0)",
            DefaultGeometry = "POINT(0 0)",
            VersionGeometry = "POINT(0 0)"
        };

        var view = VersionConflictDiffBuilder.Build(conflict);

        Assert.False(view.GeometryChanged);
    }

    [Fact]
    public void Build_TreatsNullBaseAndNonNullEdit_AsChanged()
    {
        // A delete/update conflict has a null base (deleted) but a non-null edit image — that is a change.
        var conflict = new HonuaVersionConflict
        {
            LayerId = 1,
            ObjectId = 7,
            ConflictType = "deleteUpdate",
            FieldDiffs =
            [
                new HonuaVersionConflictFieldDiff { Name = "name", Base = null, Default = null, Version = "added" }
            ]
        };

        var view = VersionConflictDiffBuilder.Build(conflict);

        Assert.True(view.FieldDiffs.Single().Changed);
    }
}
