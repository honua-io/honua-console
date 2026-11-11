using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

public sealed record ConsoleFileImportCommand
{
    public required byte[] FileContent { get; init; }

    public required string FileName { get; init; }

    public required string TableName { get; init; }

    public string? TargetSchema { get; init; }

    public int? SourceSrid { get; init; }

    public int? TargetSrid { get; init; }

    public int? ExpectedSourceSrid { get; init; }

    public string? AuthorityBinding { get; init; }
}

public sealed record ConsoleFileImportResult : ConsoleOperationResult<ConsoleFileImportResult>
{
    public const string CoordinateSystemMismatchState = "Imported with a different coordinate system";

    public bool RequiresReconciliation => State is ConsoleWriteOutcome.UnverifiedState or CoordinateSystemMismatchState;

    public int? SourceSrid { get; init; }

    public string? TableName { get; init; }

    public long FeatureCount { get; init; }

    public int? StoredSrid { get; init; }

    public bool? Reprojected { get; init; }

    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
}

public sealed record ConsoleFileImportPreview : ConsoleOperationResult<ConsoleFileImportPreview>
{
    public const string PreviewTooLargeWarning =
        "This file is larger than the server can check before import. Choose its source coordinate system. The server reports the system it stored.";

    public HonuaAdminImportCoordinateSystem? CoordinateSystem { get; init; }

    public string? AuthorityBinding { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record ConsoleFileImportCoordinates(int? SourceSrid, int? TargetSrid);
