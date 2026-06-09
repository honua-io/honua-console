using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

/// <summary>
/// A branch version projected for the Operate version-manager surface (/operate/versions). Carries the
/// operator-facing identity (name, owner, access, state) plus the created/modified instants converted from
/// the server's epoch-millisecond moments. <see cref="EditCountLabel"/> is a display hint; the server does
/// not currently project a per-version edit count, so it renders a neutral placeholder rather than a
/// fabricated number.
/// </summary>
public sealed record OperateVersionView
{
    public required string VersionGuid { get; init; }

    public required string VersionName { get; init; }

    public required string Owner { get; init; }

    public required string Access { get; init; }

    public required string Status { get; init; }

    public string? Description { get; init; }

    public string? ParentVersionGuid { get; init; }

    public DateTimeOffset? Created { get; init; }

    public DateTimeOffset? Modified { get; init; }

    /// <summary>Display hint for the version's edit count column (the server does not yet project a count).</summary>
    public string EditCountLabel { get; init; } = "—";

    internal static OperateVersionView FromContract(HonuaVersionInfo info) => new()
    {
        VersionGuid = info.VersionGuid,
        VersionName = info.VersionName,
        Owner = info.Owner,
        Access = info.Access,
        Status = info.Status,
        Description = info.Description,
        ParentVersionGuid = info.ParentVersionGuid,
        Created = info.CreationMoment > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(info.CreationMoment) : null,
        Modified = info.ModifiedMoment > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(info.ModifiedMoment) : null
    };
}

/// <summary>The list of versions for a service, or an explicit missing/unavailable binding state.</summary>
public sealed record OperateVersionListView
{
    public bool Bound { get; init; }

    public string? State { get; init; }

    public string? Detail { get; init; }

    public string? Contract { get; init; }

    public IReadOnlyList<OperateVersionView> Versions { get; init; } = [];

    public static OperateVersionListView Unbound(string state, string detail, string? contract = null) => new()
    {
        Bound = false,
        State = state,
        Detail = detail,
        Contract = contract
    };
}

/// <summary>Operator intent to create a branch version.</summary>
public sealed record CreateVersionCommand
{
    public required string ServiceId { get; init; }

    public required string VersionName { get; init; }

    public string? Owner { get; init; }

    public string Access { get; init; } = "private";

    public string? Description { get; init; }
}

/// <summary>Operator intent to alter a branch version (null fields are left unchanged).</summary>
public sealed record AlterVersionCommand
{
    public required string ServiceId { get; init; }

    public required string VersionGuid { get; init; }

    public string? VersionName { get; init; }

    public string? Access { get; init; }

    public string? Description { get; init; }
}

/// <summary>Operator intent to reconcile a branch version against DEFAULT with an auto-resolution policy.</summary>
public sealed record ReconcileVersionCommand
{
    public required string ServiceId { get; init; }

    public required string VersionGuid { get; init; }

    /// <summary>One of <c>none</c>, <c>last-write-wins</c>, <c>version-wins</c>, <c>default-wins</c>.</summary>
    public string Policy { get; init; } = "none";

    public bool AbortIfConflicts { get; init; }
}

/// <summary>
/// Outcome of a version lifecycle operation (create/alter/delete/reconcile/post). On success
/// <see cref="Succeeded"/> is <c>true</c> with a state token and detail; on failure (missing binding,
/// validation rejection, transport error) it carries the neutral state vocabulary token and the server's
/// rejection reason. Never fabricates success.
/// </summary>
public sealed record VersionOperationResult
{
    public bool Succeeded { get; init; }

    public required string State { get; init; }

    public string? Detail { get; init; }

    public static VersionOperationResult Success(string state, string detail) => new()
    {
        Succeeded = true,
        State = state,
        Detail = detail
    };

    public static VersionOperationResult Failure(string state, string detail) => new()
    {
        Succeeded = false,
        State = state,
        Detail = detail
    };

    public static VersionOperationResult MissingBinding(string detail) => new()
    {
        Succeeded = false,
        State = "Missing binding",
        Detail = detail
    };
}

/// <summary>Outcome of a reconcile: the operation result plus the auto-resolved/remaining conflict counts.</summary>
public sealed record ReconcileResultView
{
    public required VersionOperationResult Operation { get; init; }

    public bool HasConflicts { get; init; }

    public bool CanPost { get; init; }

    public int AutoResolvedCount { get; init; }

    public int RemainingConflictCount { get; init; }
}

/// <summary>The pending conflict set for a branch version, or an explicit missing/unavailable binding state.</summary>
public sealed record VersionConflictsView
{
    public bool Bound { get; init; }

    public string? State { get; init; }

    public string? Detail { get; init; }

    public string? Contract { get; init; }

    public bool HasConflicts { get; init; }

    public IReadOnlyList<VersionConflictView> Conflicts { get; init; } = [];

    public bool CanPost => Bound && !HasConflicts;

    public static VersionConflictsView Unbound(string state, string detail, string? contract = null) => new()
    {
        Bound = false,
        State = state,
        Detail = detail,
        Contract = contract
    };
}

/// <summary>
/// One conflicting feature prepared for the 3-way diff surface: the conflict type, the base/DEFAULT/version
/// geometry images (WKT) with a changed-flag, and the per-field three-way diffs with per-field
/// changed-flags so the UI can highlight the differing fields.
/// </summary>
public sealed record VersionConflictView
{
    public required int LayerId { get; init; }

    public required long ObjectId { get; init; }

    public required string ConflictType { get; init; }

    public string? BaseGeometry { get; init; }

    public string? DefaultGeometry { get; init; }

    public string? VersionGeometry { get; init; }

    /// <summary>True when DEFAULT or version geometry differs from base (or from each other).</summary>
    public bool GeometryChanged { get; init; }

    public IReadOnlyList<VersionFieldDiffView> FieldDiffs { get; init; } = [];

    /// <summary>Stable per-conflict key for selection state (layerId:objectId).</summary>
    public string Key => $"{LayerId}:{ObjectId}";
}

/// <summary>One field's three-way diff with a changed-flag for per-field highlighting.</summary>
public sealed record VersionFieldDiffView
{
    public required string Name { get; init; }

    public string? Base { get; init; }

    public string? Default { get; init; }

    public string? Version { get; init; }

    /// <summary>True when DEFAULT or version differs from base (or from each other).</summary>
    public bool Changed { get; init; }
}

/// <summary>
/// Builds the operator-facing 3-way diff views from the raw server conflict images, computing the
/// per-field and geometry changed-flags so the UI can highlight only the differing values. Pure
/// projection logic kept out of the Razor page so it is unit-testable.
/// </summary>
public static class VersionConflictDiffBuilder
{
    /// <summary>Projects a raw server conflict into the UI 3-way diff view.</summary>
    public static VersionConflictView Build(HonuaVersionConflict conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);

        var fieldDiffs = conflict.FieldDiffs
            .Select(diff => new VersionFieldDiffView
            {
                Name = diff.Name,
                Base = diff.Base,
                Default = diff.Default,
                Version = diff.Version,
                Changed = IsChanged(diff.Base, diff.Default, diff.Version)
            })
            .ToArray();

        return new VersionConflictView
        {
            LayerId = conflict.LayerId,
            ObjectId = conflict.ObjectId,
            ConflictType = conflict.ConflictType,
            BaseGeometry = conflict.BaseGeometry,
            DefaultGeometry = conflict.DefaultGeometry,
            VersionGeometry = conflict.VersionGeometry,
            GeometryChanged = IsChanged(conflict.BaseGeometry, conflict.DefaultGeometry, conflict.VersionGeometry),
            FieldDiffs = fieldDiffs
        };
    }

    /// <summary>Projects a sequence of raw server conflicts into UI 3-way diff views.</summary>
    public static IReadOnlyList<VersionConflictView> Build(IEnumerable<HonuaVersionConflict> conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        return conflicts.Select(Build).ToArray();
    }

    // A value triple is "changed" when the three images are not all equal (an ordinal compare of the JSON/WKT
    // text images), i.e. DEFAULT or version diverged from base. Used to highlight only the differing rows.
    private static bool IsChanged(string? @base, string? @default, string? version) =>
        !(string.Equals(@base, @default, StringComparison.Ordinal)
          && string.Equals(@base, version, StringComparison.Ordinal));
}
