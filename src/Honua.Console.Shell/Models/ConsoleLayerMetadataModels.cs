namespace Honua.Console.Shell.Models;

/// <summary>
/// A partial change to a layer's editing policy within one FeatureServer service. A null field leaves the
/// server value unchanged; an empty <see cref="GlobalIdField"/> clears the declared global-id binding.
/// </summary>
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
