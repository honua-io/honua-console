namespace Honua.Console.Contracts;

// SHIM: Synchronous file-import receipts have no published SDK projection; removal is tracked in SDK_SHIM_POLICY.md.
public sealed record HonuaAdminImportResult
{
    public string? PhysicalTableName { get; init; }

    public string? Schema { get; init; }

    public int? SourceSrid { get; init; }

    public int? StoredSrid { get; init; }

    public bool? Reprojected { get; init; }

    public bool Success { get; init; }

    public long FeatureCount { get; init; }

    public string? TableName { get; init; }

    public string? Format { get; init; }

    public string? ErrorMessage { get; init; }

    public IReadOnlyList<HonuaAdminImportValidationIssue> ValidationErrors { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}
