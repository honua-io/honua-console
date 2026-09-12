using System.Net;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class TerminalFailureReceiptTests
{
    private static readonly Manifest Contract = JsonSerializer.Deserialize<Manifest>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "terminal-error-receipts.v1.json")),
        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    public static TheoryData<string, FailureClass> ConsoleCells()
    {
        TheoryData<string, FailureClass> cells = [];
        foreach (var resultType in new[] { "admin", "studio", "support" })
        {
            foreach (var failure in Contract.FailureClasses)
            {
                cells.Add(resultType, failure);
            }
        }
        return cells;
    }

    [Fact]
    public void Contract_has_exactly_15_console_cells() =>
        Assert.Equal(15, 3 * Contract.FailureClasses.Count);

    [Theory]
    [MemberData(nameof(ConsoleCells))]
    public void Every_console_result_preserves_the_machine_receipt(string resultType, FailureClass failure)
    {
        const string correlationId = "console-contract-correlation";
        var body = JsonSerializer.Serialize(new
        {
            version = "1.0",
            kind = failure.Kind,
            code = failure.Code,
            correlationId,
            retryable = failure.Retryable,
            retryAfterSeconds = failure.RetryAfterSeconds,
            errors = failure.Errors
        });
        using var response = new HttpResponseMessage((HttpStatusCode)failure.HttpStatus)
        {
            Content = new StringContent(body)
        };
        response.Headers.Add("X-Correlation-ID", correlationId);
        response.Headers.TryAddWithoutValidation("Authorization", "secret");
        if (failure.RetryAfterSeconds is { } retryAfter)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfter));
        }

        var receipt = ConsoleFailureReceiptParser.Parse(response, body);
        var surfaced = resultType switch
        {
            "admin" => AdminEndpointIssueFactory.CreateIssue("contract", response, body).Receipt,
            "studio" => new StudioEndpointIssue(State(failure), "contract", failure.Detail, failure.HttpStatus)
            {
                Receipt = receipt
            }.Receipt,
            "support" => SupportTicketResult.Denied(Status(failure), failure.Detail, receipt).Receipt,
            _ => throw new InvalidOperationException(resultType)
        };

        Assert.NotNull(surfaced);
        Assert.Equal(failure.HttpStatus, surfaced.TransportStatus);
        Assert.Null(surfaced.ProtocolCode);
        Assert.Equal(ParseKind(failure.Kind), surfaced.Kind);
        Assert.Equal(failure.Code, surfaced.Code);
        Assert.Equal(failure.Retryable, surfaced.Retryable);
        Assert.Equal(failure.RetryAfterSeconds, surfaced.RetryAfterSeconds);
        Assert.Equal(correlationId, surfaced.CorrelationId);
        Assert.Equal(failure.Errors?.GetArrayLength() ?? 0, surfaced.FieldErrors.Count);
        Assert.DoesNotContain(surfaced.ProtocolMetadata.Initial.Keys,
            key => key.Equals("authorization", StringComparison.OrdinalIgnoreCase));

        if (resultType == "admin")
        {
            var issue = AdminEndpointIssueFactory.CreateIssue("contract", response, body);
            Assert.Equal(State(failure), issue.State);
            Assert.Equal(surfaced.FieldErrors.Count, issue.FieldErrors.Count);
        }
    }

    private static string State(FailureClass failure) => failure.Kind switch
    {
        "authorization" => "Missing permission",
        "not-found" => "Unsupported",
        "validation" => "Rejected",
        "conflict" => "Conflict",
        _ => "Unavailable"
    };

    private static OperateSectionStatus Status(FailureClass failure) => failure.Kind switch
    {
        "authorization" => OperateSectionStatus.Forbidden,
        "not-found" => OperateSectionStatus.Missing,
        "validation" => OperateSectionStatus.Rejected,
        "conflict" => OperateSectionStatus.Conflict,
        _ => OperateSectionStatus.Unavailable
    };

    private static TerminalFailureKind ParseKind(string kind) => kind switch
    {
        "authorization" => TerminalFailureKind.Authorization,
        "not-found" => TerminalFailureKind.NotFound,
        "validation" => TerminalFailureKind.Validation,
        "conflict" => TerminalFailureKind.Conflict,
        "throttled" => TerminalFailureKind.Throttled,
        _ => TerminalFailureKind.Unknown
    };

    public sealed record Manifest(IReadOnlyList<FailureClass> FailureClasses);
    public sealed record FailureClass(
        string Id,
        int HttpStatus,
        string Kind,
        string Code,
        bool Retryable,
        double? RetryAfterSeconds,
        string Detail,
        JsonElement? Errors);
}
