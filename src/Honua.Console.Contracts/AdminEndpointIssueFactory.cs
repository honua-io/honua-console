using System.Globalization;
using System.Net;

namespace Honua.Console.Contracts;

/// <summary>
/// Shared mapper from an admin HTTP status code to a <see cref="HonuaAdminEndpointIssue"/> state
/// (honua-console#279 PA-239). The status→state switch was copy-pasted across ~10 admin contract shims
/// and drifted apart: <c>OperateAdminShims</c> never gained the <c>Conflict</c>/<c>BadRequest</c> arms, so
/// 409 and 400 admin responses were mis-reported to operators as <c>"Unavailable"</c> instead of
/// <c>"Conflict"</c> / <c>"Rejected"</c> — hiding an editable conflict or a validation rejection behind a
/// generic "server unavailable" surface.
///
/// Centralising the state vocabulary here keeps every admin shim on one canonical mapping. Shims that
/// carry a domain-specific detail message call <see cref="MapState"/> and supply their own detail; shims
/// with a generic detail use <see cref="CreateIssue"/> directly. As part of the consolidation the
/// precondition family (<c>412 Precondition Failed</c> / <c>428 Precondition Required</c>) now maps
/// consistently to <c>"Conflict"</c> everywhere it can occur, matching the mappers that already handled it.
/// </summary>
public static class AdminEndpointIssueFactory
{
    /// <summary>
    /// Canonical admin status→state mapping shared by every admin endpoint shim. Auth failures are
    /// <c>"Missing permission"</c>, route/verb/capability gaps are <c>"Unsupported"</c>, the conflict /
    /// precondition family is <c>"Conflict"</c>, a rejected request body is <c>"Rejected"</c>, and every
    /// other status is a neutral <c>"Unavailable"</c>.
    /// </summary>
    public static string MapState(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Missing permission",
        HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented => "Unsupported",
        HttpStatusCode.Conflict or HttpStatusCode.PreconditionRequired or HttpStatusCode.PreconditionFailed => "Conflict",
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => "Rejected",
        _ => "Unavailable"
    };

    /// <summary>
    /// Builds an issue with the canonical <see cref="MapState"/> state and a generic detail message. Shims
    /// with a domain-specific detail call <see cref="MapState"/> directly and supply their own detail.
    /// </summary>
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

    /// <summary>Reads the failure payload before creating the issue so no caller drops its receipt.</summary>
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
