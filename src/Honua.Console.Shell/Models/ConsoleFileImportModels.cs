namespace Honua.Console.Shell.Models;

/// <summary>
/// Operator intent for the file-import operation: the uploaded file bytes + name and the target table /
/// schema. The console reads the browser file (with progress) into <see cref="FileContent"/> before calling.
/// </summary>
public sealed record ConsoleFileImportCommand
{
    public required byte[] FileContent { get; init; }

    public required string FileName { get; init; }

    public required string TableName { get; init; }

    public string? TargetSchema { get; init; }
}

/// <summary>
/// Outcome of a file import. On success, <see cref="Succeeded"/> is <c>true</c> and <see cref="TableName"/> /
/// <see cref="FeatureCount"/> describe the created table. On failure, <see cref="State"/> carries the neutral
/// state vocabulary token and <see cref="Detail"/> the operator-facing reason.
/// </summary>
public sealed record ConsoleFileImportResult : ConsoleOperationResult<ConsoleFileImportResult>
{
    public string? TableName { get; init; }

    public long FeatureCount { get; init; }

    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
}
