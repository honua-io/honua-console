using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// L0 self-service deflection (honua-console#165). Two Console-side contract
// boundaries that power the assistant box + KB search shown BEFORE the ticket
// form on /support:
//
//   1. The OpenAI-compatible chat-completions surface
//      (POST {endpoint}/v1/chat/completions) that the Honua-GIS triage model
//      (qwen via NIM / vLLM / llama.cpp / Ollama) exposes. This is the SAME
//      inference path honua-support's L1 triage consumes, documented in
//      honua-gis-llm docs/support/triage-endpoint-contract.md. honua-console
//      takes NO code dependency on honua-support or the model repo; these
//      records mirror the OpenAI chat surface (the subset Console needs:
//      model + messages + temperature -> choices[].message.content).
//
//   2. The support KB record produced by honua-gis-llm#26
//      (scripts/build_support_kb.py -> kb/v0.1/support-kb.jsonl), validated
//      against kb/schema/kb_record.schema.json. Console retrieves over it
//      LEXICALLY today: embeddings are a documented stub (embedding=null) in
//      the upstream artifact, so retrieval ranks on embedding_text / title /
//      symptoms until an embeddings backend is wired in (TODO below).
//
// JSON on both wires is the source default: snake_case for KB records (matches
// the upstream JSONL), and the OpenAI default field names for chat.

// ---------------------------------------------------------------------------
// OpenAI-compatible chat completions (qwen triage endpoint)
// ---------------------------------------------------------------------------

/// <summary>
/// One chat message on the OpenAI-compatible wire (<c>role</c> + <c>content</c>).
/// Roles used by the Console assistant: <c>system</c> (GIS triage framing),
/// <c>user</c> (customer symptom), <c>assistant</c> (model reply).
/// </summary>
public sealed record ChatCompletionMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "user";

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;
}

/// <summary>
/// Mirrors the request body of <c>POST {endpoint}/v1/chat/completions</c>
/// (the subset Console sends). <c>temperature</c> defaults to 0 to match the
/// triage-endpoint contract's deterministic default, though the assistant is
/// conversational rather than schema-locked, so it does not force the
/// <c>submit_diagnosis</c> framing.
/// </summary>
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

/// <summary>
/// Mirrors the response body of <c>POST {endpoint}/v1/chat/completions</c>.
/// Unmodelled fields (usage, id, created, ...) are tolerated so the Console
/// keeps reading across NIM / vLLM / llama.cpp / Ollama backends.
/// </summary>
public sealed record ChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public IReadOnlyList<ChatCompletionChoice> Choices { get; init; } = [];
}

// ---------------------------------------------------------------------------
// Support KB record (honua-gis-llm#26 kb/v0.1/support-kb.jsonl)
// ---------------------------------------------------------------------------

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

/// <summary>
/// One retrieval-ready KB entry from the honua-gis-llm support KB
/// (<c>kb/v0.1/support-kb.jsonl</c>, validated against
/// <c>kb/schema/kb_record.schema.json</c>). <see cref="Embedding"/> is
/// <c>null</c> in the shipped artifact (documented stub); Console retrieves
/// lexically over <see cref="EmbeddingText"/> / <see cref="Title"/> /
/// <see cref="Symptoms"/> until an embeddings backend is configured.
/// </summary>
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

/// <summary>
/// Source-generated serialization for the L0 deflection contracts (chat +
/// KB record), keeping the surface trim/AOT-safe like
/// <see cref="SupportTicketJsonContext"/> and
/// <see cref="OperateObservabilityJsonContext"/>. Property names are carried by
/// explicit <see cref="JsonPropertyNameAttribute"/> so no naming policy is set.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ChatCompletionRequest))]
[JsonSerializable(typeof(ChatCompletionResponse))]
[JsonSerializable(typeof(SupportKbRecord))]
public sealed partial class SupportAssistantJsonContext : JsonSerializerContext;
