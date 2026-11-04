namespace Honua.Console.Shell.Models;

// A null member keeps the stored value, and an empty GlobalIdField clears the binding.
public sealed record ConsoleLayerEditingChange
{
    public required string ServiceName { get; init; }

    public required int LayerId { get; init; }

    public string? GlobalIdField { get; init; }

    public bool? SupportsAttachments { get; init; }

    public bool? AllowCreate { get; init; }

    public bool? AllowUpdate { get; init; }

    public bool? AllowDelete { get; init; }
}

public sealed record ConsoleSetLayerMetadataResult : ConsoleOperationResult<ConsoleSetLayerMetadataResult>;
