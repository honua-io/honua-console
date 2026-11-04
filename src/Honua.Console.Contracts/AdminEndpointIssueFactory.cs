using System.Globalization;
using System.Net;

namespace Honua.Console.Contracts;

public static class AdminEndpointIssueFactory
{
    public static string MapState(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Missing permission",
        HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented => "Unsupported",
        HttpStatusCode.Conflict or HttpStatusCode.PreconditionRequired or HttpStatusCode.PreconditionFailed => "Conflict",
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => "Rejected",
        _ => "Unavailable"
    };

    public static HonuaAdminEndpointIssue CreateIssue(string contract, HttpStatusCode statusCode)
    {
        var receipt = ConsoleFailureReceiptParser.FromStatus(statusCode);
        return CreateIssue(contract, statusCode, receipt);
    }

    public static HonuaAdminEndpointIssue CreateIssue(
        string contract,
        HttpResponseMessage response,
        string? body = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        var receipt = ConsoleFailureReceiptParser.Parse(response, body);
        return CreateIssue(contract, response.StatusCode, receipt);
    }

    public static async Task<HonuaAdminEndpointIssue> CreateIssueAsync(
        string contract,
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        string? body = null;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
        {
            // Headers and status remain a useful terminal receipt when a broken body stream cannot be read.
        }

        return CreateIssue(contract, response, body);
    }

    private static HonuaAdminEndpointIssue CreateIssue(
        string contract,
        HttpStatusCode statusCode,
        TerminalFailureReceipt receipt)
    {
        var detail = statusCode switch
        {
            HttpStatusCode.Unauthorized => "The Honua server rejected the request because admin authentication is missing.",
            HttpStatusCode.Forbidden => "The Honua server rejected the request because the current principal lacks admin permission.",
            HttpStatusCode.NotFound => "The Honua server does not expose this admin API contract.",
            HttpStatusCode.MethodNotAllowed => "The Honua server exposes the route but not the required admin API verb.",
            HttpStatusCode.NotImplemented => "The Honua server reports this admin capability is not implemented.",
            HttpStatusCode.Conflict or HttpStatusCode.PreconditionRequired or HttpStatusCode.PreconditionFailed =>
                "The Honua server reported a conflict for the admin request; reload before retrying.",
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity =>
                "The Honua server rejected the admin request; resolve the reported issues before retrying.",
            _ => string.Format(
                CultureInfo.InvariantCulture,
                "The Honua server returned HTTP {0} ({1}).",
                (int)statusCode,
                statusCode)
        };

        return new HonuaAdminEndpointIssue(MapState(statusCode), contract, detail, (int)statusCode)
        {
            Receipt = receipt,
            FieldErrors = receipt.FieldErrors.Select(error => new HonuaFieldValidationError
            {
                Code = error.Code ?? string.Empty,
                Severity = error.Severity,
                Path = error.Path,
                FieldId = error.FieldId,
                Message = error.Message ?? string.Empty
            }).ToArray()
        };
    }
}
