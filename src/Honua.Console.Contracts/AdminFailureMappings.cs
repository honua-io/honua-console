using System.Net;
using System.Text.Json;

namespace Honua.Console.Contracts;

internal delegate Task<HonuaAdminEndpointIssue> AdminFailureMapping(
    string contract,
    HttpResponseMessage response,
    CancellationToken cancellationToken);

internal static class AdminFailureMappings
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] FailureMessageProperties = ["message", "detail", "title"];

    public static readonly AdminFailureMapping StatusDetail = AdminEndpointIssueFactory.CreateIssueAsync;

    public static readonly AdminFailureMapping ServerMessage = async (contract, response, cancellationToken) =>
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return WithServerMessage(AdminEndpointIssueFactory.CreateIssue(contract, response, payload), payload);
    };

    public static readonly AdminFailureMapping ServerMessageAndFieldErrors = async (contract, response, cancellationToken) =>
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var issue = WithServerMessage(AdminEndpointIssueFactory.CreateIssue(contract, response, payload), payload);
        return issue with { FieldErrors = ParseFieldErrors(payload) };
    };

    public static AdminFailureMapping FromStatus(Func<string, HttpStatusCode, HonuaAdminEndpointIssue> createIssue) =>
        (contract, response, _) => Task.FromResult(createIssue(contract, response.StatusCode));

    public static AdminResponseReader<T> AsReader<T>(AdminFailureMapping mapFailure) =>
        async (contract, response, cancellationToken) => HonuaAdminEndpointResult<T>.FromIssue(
            await mapFailure(contract, response, cancellationToken).ConfigureAwait(false));

    private static HonuaAdminEndpointIssue WithServerMessage(HonuaAdminEndpointIssue issue, string payload) =>
        ParseServerMessage(payload) is { } message && !string.IsNullOrWhiteSpace(message)
            ? issue with { Detail = message }
            : issue;

    // A flat ApiResponse failure ({ success:false, message }) or an RFC 7807 ProblemDetails ({ detail, title }).
    public static string? ParseServerMessage(string? payload)
    {
        if (TryParseObject(payload) is not { } document)
        {
            return null;
        }

        using (document)
        {
            return FailureMessageProperties
                .Select(name => document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null)
                .FirstOrDefault(message => !string.IsNullOrEmpty(message));
        }
    }

    // The RFC 7807 errors[] extension carrying honua-server FieldValidationError items.
    private static IReadOnlyList<HonuaFieldValidationError> ParseFieldErrors(string payload)
    {
        if (TryParseObject(payload) is not { } document)
        {
            return [];
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            try
            {
                return errors.EnumerateArray()
                    .Where(error => error.ValueKind == JsonValueKind.Object)
                    .Select(error => error.Deserialize<HonuaFieldValidationError>(JsonOptions))
                    .Where(error => !string.IsNullOrWhiteSpace(error?.Message))
                    .Select(error => error!)
                    .ToArray();
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }

    public static JsonDocument? TryParseObject(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document;
            }

            document.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
