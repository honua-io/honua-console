using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

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

    // The server does not project an edit count, so this stays a placeholder.
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

public sealed record CreateVersionCommand
{
    public required string ServiceId { get; init; }

    public required string VersionName { get; init; }

    public string? Owner { get; init; }

    public string Access { get; init; } = "private";

    public string? Description { get; init; }
}

// Null fields are left unchanged on the server.
public sealed record AlterVersionCommand
{
    public required string ServiceId { get; init; }

    public required string VersionGuid { get; init; }

    public string? VersionName { get; init; }

    public string? Access { get; init; }

    public string? Description { get; init; }
}

public sealed record ReconcileVersionCommand
{
    public required string ServiceId { get; init; }

    public required string VersionGuid { get; init; }

    public string Policy { get; init; } = "none";

    public bool AbortIfConflicts { get; init; }
}

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

public sealed record ReconcileResultView
{
    public required VersionOperationResult Operation { get; init; }

    public bool HasConflicts { get; init; }

    public bool CanPost { get; init; }

    public int AutoResolvedCount { get; init; }

    public int RemainingConflictCount { get; init; }
}

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

public sealed record VersionConflictView
{
    public required int LayerId { get; init; }

    public required long ObjectId { get; init; }

    public required string ConflictType { get; init; }

    public string? BaseGeometry { get; init; }

    public string? DefaultGeometry { get; init; }

    public string? VersionGeometry { get; init; }

    public bool GeometryChanged { get; init; }

    public IReadOnlyList<VersionFieldDiffView> FieldDiffs { get; init; } = [];

    public string Key => $"{LayerId}:{ObjectId}";
}

public sealed record VersionFieldDiffView
{
    public required string Name { get; init; }

    public string? Base { get; init; }

    public string? Default { get; init; }

    public string? Version { get; init; }

    public bool Changed { get; init; }
}

public static class VersionConflictDiffBuilder
{
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
