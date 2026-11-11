using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<WorkflowStepFailurePolicy>))]
public enum WorkflowStepFailurePolicy { Fail, Skip }

[JsonConverter(typeof(JsonStringEnumConverter<WorkflowStepConditionKind>))]
public enum WorkflowStepConditionKind
{
    UpstreamSucceeded, UpstreamSkipped, HasArtifact, NoArtifact, ArtifactCountAtLeast, ArtifactCountAtMost
}

public sealed record StepRetryPolicy
{
    [JsonPropertyName("maxAttempts")] public required int MaxAttempts { get; init; }
    [JsonPropertyName("initialDelaySeconds")] public required int InitialDelaySeconds { get; init; }
    [JsonPropertyName("backoffMultiplier")] public double BackoffMultiplier { get; init; } = 2.0;
    [JsonPropertyName("maxDelaySeconds")] public int MaxDelaySeconds { get; init; } = 300;
}

public sealed record WorkflowStepCondition
{
    [JsonPropertyName("sourceStepId")] public required string SourceStepId { get; init; }
    [JsonPropertyName("kind")] public required WorkflowStepConditionKind Kind { get; init; }
    [JsonPropertyName("threshold")] public int Threshold { get; init; }
    [JsonPropertyName("artifactLabel")] public string? ArtifactLabel { get; init; }
    [JsonPropertyName("negate")] public bool Negate { get; init; }
}

public sealed record WorkflowForEachSpec(
    [property: JsonPropertyName("items")] IReadOnlyList<string> Items,
    [property: JsonPropertyName("itemPlaceholder")] string ItemPlaceholder = "${item}",
    [property: JsonPropertyName("maxIterations")] int? MaxIterations = null);

public sealed partial record WorkflowNode
{
    [JsonPropertyName("retryPolicy")] public StepRetryPolicy? RetryPolicy { get; init; }
    [JsonPropertyName("failurePolicy")] public WorkflowStepFailurePolicy FailurePolicy { get; init; } = WorkflowStepFailurePolicy.Fail;
    [JsonPropertyName("timeoutSeconds")] public int? TimeoutSeconds { get; init; }
    [JsonPropertyName("condition")] public WorkflowStepCondition? Condition { get; init; }
    [JsonPropertyName("forEach")] public WorkflowForEachSpec? ForEach { get; init; }
}
