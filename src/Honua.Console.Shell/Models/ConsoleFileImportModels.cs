namespace Honua.Console.Shell.Models;

public sealed record ConsoleFileImportCommand
{
    public required byte[] FileContent { get; init; }

    public required string FileName { get; init; }

    public required string TableName { get; init; }

    public string? TargetSchema { get; init; }
}

public sealed record ConsoleFileImportResult : ConsoleOperationResult<ConsoleFileImportResult>
{
    public string? TableName { get; init; }

    public long FeatureCount { get; init; }

    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
}
