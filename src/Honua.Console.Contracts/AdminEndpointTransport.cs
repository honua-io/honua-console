namespace Honua.Console.Contracts;

internal delegate Task<HonuaAdminEndpointResult<T>> AdminResponseReader<T>(
    string contract,
    HttpResponseMessage response,
    CancellationToken cancellationToken);

internal sealed record AdminEndpointCall<T>(
    Func<HttpRequestMessage> CreateRequest,
    string Contract,
    AdminResponseReader<T> ReadFailure,
    AdminResponseReader<T> ReadBody)
{
    public AdminEndpointCall(
        Func<HttpRequestMessage> createRequest,
        string contract,
        AdminFailureMapping mapFailure,
        AdminResponseReader<T> readBody)
        : this(createRequest, contract, AdminFailureMappings.AsReader<T>(mapFailure), readBody)
    {
    }

    public HttpCompletionOption Completion { get; init; } = HttpCompletionOption.ResponseContentRead;
}

internal sealed class AdminEndpointTransport(HttpClient httpClient, string? apiKey, AdminTransportFailure transportFailure)
{
    private const string ApiKeyHeader = "X-API-Key";

    public async Task<HonuaAdminEndpointResult<T>> SendAsync<T>(AdminEndpointCall<T> call, CancellationToken cancellationToken)
    {
        using var request = call.CreateRequest();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.TryAddWithoutValidation(ApiKeyHeader, apiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, call.Completion, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (transportFailure.Matches(ex, cancellationToken))
        {
            return HonuaAdminEndpointResult<T>.FromIssue(transportFailure.CreateIssue(call.Contract, ex));
        }

        using (response)
        {
            var read = response.IsSuccessStatusCode ? call.ReadBody : call.ReadFailure;
            return await read(call.Contract, response, cancellationToken).ConfigureAwait(false);
        }
    }
}

internal sealed class AdminTransportFailure
{
    private readonly string _endpoint;
    private readonly Func<Exception, CancellationToken, bool> _matches;

    private AdminTransportFailure(string endpoint, Func<Exception, CancellationToken, bool> matches)
    {
        _endpoint = endpoint;
        _matches = matches;
    }

    // A TaskCanceledException without caller cancellation is an HttpClient timeout; caller cancellation
    // propagates so it cancels the calling operation instead of being masked as a transport failure.
    public static AdminTransportFailure Unreachable(string endpoint) =>
        new(endpoint, static (ex, cancellationToken) =>
            ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested));

    public static AdminTransportFailure UnreachableOrCancelled(string endpoint) =>
        new(endpoint, static (ex, _) => ex is HttpRequestException or TaskCanceledException);

    public bool Matches(Exception exception, CancellationToken cancellationToken) => _matches(exception, cancellationToken);

    public HonuaAdminEndpointIssue CreateIssue(string contract, Exception exception) =>
        new("Unavailable", contract, $"{_endpoint} could not be reached: {exception.Message}");
}
