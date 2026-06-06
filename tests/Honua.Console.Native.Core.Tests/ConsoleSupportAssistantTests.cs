using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class ConsoleSupportAssistantTests
{
    private static readonly Uri LlmBaseUri = new("http://localhost:8000/v1");

    [Fact]
    public async Task AssistantPostsToChatCompletionsWithSystemPromptAndReturnsReply()
    {
        var handler = new RecordingHandler(_ => ChatResponse("Reproject the working layer to the target CRS, then rerun."));
        var client = new HttpConsoleSupportAssistantClient(new HttpClient(handler), LlmBaseUri, "honua-gis", apiKey: "k");

        var result = await client.AskAsync([new ChatCompletionMessage { Role = "user", Content = "overlay returns empty geometry" }]);

        Assert.True(client.IsConfigured);
        Assert.True(result.IsAllowed);
        Assert.Contains("Reproject", result.Reply);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v1/chat/completions", request.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);

        var sent = JsonSerializer.Deserialize(handler.LastBody!, SupportAssistantJsonContext.Default.ChatCompletionRequest);
        Assert.Equal("honua-gis", sent!.Model);
        // System framing is prepended, then the caller's user turn.
        Assert.Equal("system", sent.Messages[0].Role);
        Assert.Contains("Honua GIS support assistant", sent.Messages[0].Content);
        Assert.Equal("user", sent.Messages[1].Role);
    }

    [Fact]
    public async Task AssistantResolvesBareBaseUriToV1ChatCompletions()
    {
        var handler = new RecordingHandler(_ => ChatResponse("ok"));
        var client = new HttpConsoleSupportAssistantClient(new HttpClient(handler), new Uri("http://localhost:11434"), "honua-gis", apiKey: null);

        await client.AskAsync([new ChatCompletionMessage { Role = "user", Content = "hi" }]);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v1/chat/completions", request.RequestUri!.AbsolutePath);
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task AssistantNonSuccessMapsToNeutralStatus()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var client = new HttpConsoleSupportAssistantClient(new HttpClient(handler), LlmBaseUri, "honua-gis", apiKey: null);

        var result = await client.AskAsync([new ChatCompletionMessage { Role = "user", Content = "hi" }]);

        Assert.False(result.IsAllowed);
        Assert.Equal(OperateSectionStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task UnsupportedAssistantReportsNotConfiguredWithoutCall()
    {
        var client = new UnsupportedConsoleSupportAssistantClient();

        var result = await client.AskAsync([new ChatCompletionMessage { Role = "user", Content = "hi" }]);

        Assert.False(client.IsConfigured);
        Assert.False(result.IsAllowed);
        Assert.Equal(OperateSectionStatus.Unsupported, result.Status);
    }

    private static HttpResponseMessage ChatResponse(string content)
    {
        var payload = new ChatCompletionResponse
        {
            Choices = [new ChatCompletionChoice { Index = 0, Message = new ChatCompletionMessage { Role = "assistant", Content = content } }]
        };
        var json = JsonSerializer.Serialize(payload, SupportAssistantJsonContext.Default.ChatCompletionResponse);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            if (request.Headers.Authorization is { } auth)
            {
                clone.Headers.Authorization = new AuthenticationHeaderValue(auth.Scheme, auth.Parameter);
            }

            Requests.Add(clone);
            return responder(request);
        }
    }
}
