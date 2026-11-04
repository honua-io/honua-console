using System.Net.Http.Json;
using System.Text.Json;

namespace Honua.Console.Contracts;

internal interface IAdminResponseEnvelope<out T>
{
    bool Success { get; }

    T? Data { get; }

    string? Message { get; }
}

// Mismatch is the operator-facing prefix of a JSON shape failure; the parser message follows it.
internal sealed record AdminBodyFormat(JsonSerializerOptions Json, AdminBodyRead Read, string Mismatch);

internal abstract class AdminBodyRead
{
    // Streams the body; an empty body is malformed JSON.
    public static readonly AdminBodyRead Streamed = new StreamedRead();

    // Buffers the body; a blank body reads as no value.
    public static readonly AdminBodyRead Buffered = new BufferedRead();

    public abstract Task<TBody?> ReadAsync<TBody>(
        HttpResponseMessage response,
        JsonSerializerOptions json,
        CancellationToken cancellationToken);

    private sealed class StreamedRead : AdminBodyRead
    {
        public override Task<TBody?> ReadAsync<TBody>(
            HttpResponseMessage response,
            JsonSerializerOptions json,
            CancellationToken cancellationToken) where TBody : default =>
            response.Content.ReadFromJsonAsync<TBody>(json, cancellationToken);
    }

    private sealed class BufferedRead : AdminBodyRead
    {
        public override async Task<TBody?> ReadAsync<TBody>(
            HttpResponseMessage response,
            JsonSerializerOptions json,
            CancellationToken cancellationToken) where TBody : default
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(payload) ? default : JsonSerializer.Deserialize<TBody>(payload, json);
        }
    }
}

internal readonly record struct AdminBodyContext(string Contract, int StatusCode)
{
    public HonuaAdminEndpointResult<T> Unavailable<T>(string detail) => Issue<T>("Unavailable", detail);

    public HonuaAdminEndpointResult<T> Unsupported<T>(string detail) => Issue<T>("Unsupported", detail);

    private HonuaAdminEndpointResult<T> Issue<T>(string state, string detail) =>
        HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(state, Contract, detail, StatusCode));
}

internal static class AdminResponseBodies
{
    public static AdminResponseReader<T> Envelope<T>(AdminBodyFormat format, string missingData) =>
        Envelope<HonuaAdminApiResponse<T>, T>(format, missingData);

    public static AdminResponseReader<T> Envelope<TEnvelope, T>(AdminBodyFormat format, string missingData)
        where TEnvelope : IAdminResponseEnvelope<T> =>
        Json<TEnvelope, T>(format, (envelope, context) => envelope is { Success: true, Data: { } data }
            ? HonuaAdminEndpointResult<T>.FromData(data)
            : context.Unavailable<T>(envelope?.Message ?? missingData));

    public static AdminResponseReader<T> Required<T>(AdminBodyFormat format, string missingBody) =>
        Required<T, T>(format, missingBody, body => body);

    public static AdminResponseReader<T> Required<TBody, T>(AdminBodyFormat format, string missingBody, Func<TBody, T> project) =>
        Json<TBody, T>(format, (body, context) => body is null
            ? context.Unavailable<T>(missingBody)
            : HonuaAdminEndpointResult<T>.FromData(project(body)));

    public static AdminResponseReader<T> Optional<TBody, T>(AdminBodyFormat format, Func<TBody?, T> project) =>
        Json<TBody, T>(format, (body, _) => HonuaAdminEndpointResult<T>.FromData(project(body)));

    public static AdminResponseReader<T> Json<TBody, T>(
        AdminBodyFormat format,
        Func<TBody?, AdminBodyContext, HonuaAdminEndpointResult<T>> interpret) =>
        async (contract, response, cancellationToken) =>
        {
            var context = new AdminBodyContext(contract, (int)response.StatusCode);
            TBody? body;
            try
            {
                body = await format.Read.ReadAsync<TBody>(response, format.Json, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                return context.Unsupported<T>($"{format.Mismatch}: {ex.Message}");
            }

            return interpret(body, context);
        };
}
