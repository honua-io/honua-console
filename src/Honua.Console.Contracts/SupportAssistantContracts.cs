using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

public sealed record ChatCompletionMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "user";

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;
}

// Temperature stays at zero and this request does not force a submit_diagnosis tool.
public sealed record ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("messages")]
    public IReadOnlyList<ChatCompletionMessage> Messages { get; init; } = [];

    [JsonPropertyName("temperature")]
    public double Temperature { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }
}

public sealed record ChatCompletionChoice
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("message")]
    public ChatCompletionMessage? Message { get; init; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

public sealed record ChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public IReadOnlyList<ChatCompletionChoice> Choices { get; init; } = [];
}

public sealed record SupportKbSource
{
    [JsonPropertyName("family")]
    public string Family { get; init; } = string.Empty;

    [JsonPropertyName("ref")]
    public string Ref { get; init; } = string.Empty;

    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    [JsonPropertyName("license")]
    public string? License { get; init; }
}

public sealed record SupportKbRecord
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("symptoms")]
    public IReadOnlyList<string> Symptoms { get; init; } = [];

    [JsonPropertyName("remediation")]
    public IReadOnlyList<string> Remediation { get; init; } = [];

    [JsonPropertyName("fault_code")]
    public string? FaultCode { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = [];

    [JsonPropertyName("source")]
    public SupportKbSource? Source { get; init; }

    [JsonPropertyName("embedding_text")]
    public string EmbeddingText { get; init; } = string.Empty;

    // embedding / embedding_model are present-but-null in the shipped artifact.
    // Console retrieves lexically today, so they are intentionally NOT modelled;
    // the case-insensitive ignore-missing reader tolerates them on the wire.
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ChatCompletionRequest))]
[JsonSerializable(typeof(ChatCompletionResponse))]
[JsonSerializable(typeof(SupportKbRecord))]
public sealed partial class SupportAssistantJsonContext : JsonSerializerContext;
