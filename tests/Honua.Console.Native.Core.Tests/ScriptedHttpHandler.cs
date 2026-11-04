using System.Net;
using System.Text;

namespace Honua.Console.Native.Core.Tests;

internal sealed record SentRequest(HttpMethod Method, Uri? Uri, string? ApiKey);

internal sealed class ScriptedHttpHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<SentRequest> Requests { get; } = [];

    public static ScriptedHttpHandler Returning(HttpStatusCode status, string? body = null) =>
        new(_ => Task.FromResult(Response(status, body)));

    public static ScriptedHttpHandler Throwing(Exception exception) =>
        new(_ => Task.FromException<HttpResponseMessage>(exception));

    public static ScriptedHttpHandler WaitingForCancellation() =>
        new(async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });

    public static HttpResponseMessage Response(HttpStatusCode status, string? body) =>
        new(status)
        {
            Content = body is null
                ? new ByteArrayContent([])
                : new StringContent(body, Encoding.UTF8, "application/json")
        };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var apiKey = request.Headers.TryGetValues("X-API-Key", out var values) ? values.Single() : null;
        Requests.Add(new SentRequest(request.Method, request.RequestUri, apiKey));
        return respond(cancellationToken);
    }
}
