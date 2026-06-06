using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Binds the L0 deflection assistant to the OpenAI-compatible Honua-GIS triage
/// endpoint (<c>POST {endpoint}/v1/chat/completions</c>, qwen via NIM / vLLM /
/// llama.cpp / Ollama — see honua-gis-llm
/// <c>docs/support/triage-endpoint-contract.md</c>). This is the SAME inference
/// path honua-support's L1 triage drives; the Console entry point is
/// conversational. Endpoint base URL, model, and an optional bearer key come
/// from Console config/env at registration; a single shared
/// <see cref="HttpClient"/> is reused (mirroring
/// <see cref="HttpConsoleOperateObservabilityClient"/>). Serialization is
/// source-generated for trim/AOT safety.
/// </summary>
public sealed class HttpConsoleSupportAssistantClient : IConsoleSupportAssistantClient
{
    // GIS triage system prompt spirit (concise, reproducible GIS help). Matches
    // the framing of the honua-support L1 path / triage-endpoint contract but is
    // tuned for a conversational L0 box rather than the schema-locked
    // submit_diagnosis object.
    private const string SystemPrompt =
        "You are the Honua GIS support assistant, helping a user self-serve before they file a support ticket. " +
        "Honua is a geospatial platform (FeatureServer/MapServer/OGC services, tiling, spatial analysis, publishing, Esri compatibility). " +
        "Give concise, reproducible answers: state the likely cause, then ordered, copy-pasteable steps the user can run to confirm and fix it. " +
        "Prefer read-only diagnosis first; flag clearly when a step needs operator/admin write access. " +
        "If you are not confident or the issue needs a human, say so plainly and suggest filing a ticket. " +
        "Do not invent CRS codes, field names, endpoints, or commands you are unsure about.";

    private readonly HttpClient _http;
    private readonly Uri _chatCompletionsUri;
    private readonly string _model;
    private readonly string? _apiKey;

    public HttpConsoleSupportAssistantClient(HttpClient http, Uri baseUri, string model, string? apiKey)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        _chatCompletionsUri = ResolveChatCompletionsUri(baseUri);
        _model = model.Trim();
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    public bool IsConfigured => true;

    public async Task<SupportAssistantResult> AskAsync(
        IReadOnlyList<ChatCompletionMessage> conversation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        var messages = new List<ChatCompletionMessage>(conversation.Count + 1)
        {
            new() { Role = "system", Content = SystemPrompt }
        };
        messages.AddRange(conversation);

        var request = new ChatCompletionRequest
        {
            Model = _model,
            Messages = messages,
            // Deterministic default per the triage-endpoint contract; the L0 box
            // does not force the submit_diagnosis schema, so it stays free-form.
            Temperature = 0,
            Stream = false
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, _chatCompletionsUri)
        {
            Content = JsonContent.Create(request, SupportAssistantJsonContext.Default.ChatCompletionRequest)
        };
        if (_apiKey is not null)
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        try
        {
            using var response = await _http
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return SupportAssistantResult.Denied(
                    MapStatus(response.StatusCode),
                    $"The Honua-GIS assistant endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer
                .DeserializeAsync(stream, SupportAssistantJsonContext.Default.ChatCompletionResponse, cancellationToken)
                .ConfigureAwait(false);

            var reply = value?.Choices.FirstOrDefault()?.Message?.Content;
            return string.IsNullOrWhiteSpace(reply)
                ? SupportAssistantResult.Denied(OperateSectionStatus.Unavailable, "The assistant endpoint returned an empty reply.")
                : SupportAssistantResult.Allowed(reply.Trim());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return SupportAssistantResult.Denied(
                OperateSectionStatus.Unavailable,
                "The Honua-GIS assistant endpoint is unreachable or returned an unreadable response.");
        }
    }

    // The endpoint may be configured as the "/v1" base (per the contract) or a
    // bare host; normalize to the absolute "/v1/chat/completions" target either
    // way so both "http://host:8000/v1" and "http://host:8000" work.
    private static Uri ResolveChatCompletionsUri(Uri baseUri)
    {
        var absolute = baseUri.AbsoluteUri.TrimEnd('/');
        if (absolute.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(absolute, UriKind.Absolute);
        }

        if (absolute.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(absolute + "/chat/completions", UriKind.Absolute);
        }

        return new Uri(absolute + "/v1/chat/completions", UriKind.Absolute);
    }

    private static OperateSectionStatus MapStatus(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OperateSectionStatus.Forbidden,
        HttpStatusCode.NotFound => OperateSectionStatus.Missing,
        HttpStatusCode.NotImplemented => OperateSectionStatus.Unsupported,
        _ => OperateSectionStatus.Unavailable
    };
}

/// <summary>
/// Active when no LLM endpoint is configured. The /support page hides the
/// assistant box entirely (KB search + ticket form remain the deflection path),
/// so this never renders a dead input; it only answers calls defensively with
/// the neutral unsupported state.
/// </summary>
public sealed class UnsupportedConsoleSupportAssistantClient : IConsoleSupportAssistantClient
{
    public bool IsConfigured => false;

    public Task<SupportAssistantResult> AskAsync(
        IReadOnlyList<ChatCompletionMessage> conversation,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SupportAssistantResult.Denied(
            OperateSectionStatus.Unsupported,
            "The Honua-GIS assistant is not configured for this Console build."));
}
