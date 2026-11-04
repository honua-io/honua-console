using Honua.Sdk.Studio.Packages;
using System.Text.Json.Serialization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

public sealed record ConsoleFieldValidationError
{
    [JsonPropertyName("code")] public string Code { get; init; } = string.Empty;

    [JsonPropertyName("severity")] public string? Severity { get; init; }

    [JsonPropertyName("path")] public string? Path { get; init; }

    [JsonPropertyName("fieldId")] public string? FieldId { get; init; }

    [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
}

public sealed class ServerFieldErrorMapper
{
    public delegate string? FieldKeyResolver(string? locator, string code);

    public const string FormLevelFieldKey = "_form";

    private readonly FieldKeyResolver _resolve;

    public ServerFieldErrorMapper(FieldKeyResolver? resolver = null) =>
        _resolve = resolver ?? ((locator, _) => locator);

    public IReadOnlyList<ConsoleFieldError> Map(IEnumerable<StudioValidationDiagnostic>? diagnostics)
    {
        if (diagnostics is null)
        {
            return Array.Empty<ConsoleFieldError>();
        }

        return [.. diagnostics.Select(diagnostic => Create(
            locator: diagnostic.Path,
            code: diagnostic.Code,
            severity: MapStudioSeverity(diagnostic.Severity),
            message: diagnostic.Message))];
    }

    public IReadOnlyList<ConsoleFieldError> Map(IEnumerable<HonuaFormValidationIssue>? issues)
    {
        if (issues is null)
        {
            return Array.Empty<ConsoleFieldError>();
        }

        // fieldId is the more precise locator when present; otherwise the JSON-Pointer path.
        return [.. issues.Select(issue => Create(
            locator: !string.IsNullOrWhiteSpace(issue.FieldId) ? issue.FieldId : issue.Path,
            code: issue.Code,
            severity: ParseSeverity(issue.Severity),
            message: issue.Message))];
    }

    public IReadOnlyList<ConsoleFieldError> Map(IEnumerable<WorkflowPackageValidationFailure>? failures)
    {
        if (failures is null)
        {
            return Array.Empty<ConsoleFieldError>();
        }

        // Workflow failures carry no severity — every failure is a hard blocker by contract.
        return [.. failures.Select(failure => Create(
            locator: failure.FieldPath,
            code: failure.Code,
            severity: ConsoleValidationSeverity.Blocker,
            message: failure.Message))];
    }

    public IReadOnlyList<ConsoleFieldError> Map(IEnumerable<ConsoleFieldValidationError>? errors)
    {
        if (errors is null)
        {
            return Array.Empty<ConsoleFieldError>();
        }

        return [.. errors.Select(error => Create(
            locator: !string.IsNullOrWhiteSpace(error.FieldId) ? error.FieldId : error.Path,
            code: error.Code,
            severity: ParseSeverity(error.Severity),
            message: error.Message))];
    }

    private ConsoleFieldError Create(string? locator, string code, ConsoleValidationSeverity severity, string message)
    {
        var resolved = _resolve(locator, code);
        var fieldKey = !string.IsNullOrWhiteSpace(resolved)
            ? resolved!
            : !string.IsNullOrWhiteSpace(locator) ? locator! : FormLevelFieldKey;

        return new ConsoleFieldError(fieldKey, code, severity, message, locator);
    }

    private static ConsoleValidationSeverity MapStudioSeverity(StudioPackageDiagnosticSeverity severity) => severity switch
    {
        StudioPackageDiagnosticSeverity.Info => ConsoleValidationSeverity.Info,
        StudioPackageDiagnosticSeverity.Warning => ConsoleValidationSeverity.Warning,
        StudioPackageDiagnosticSeverity.Error => ConsoleValidationSeverity.Error,
        StudioPackageDiagnosticSeverity.Blocker => ConsoleValidationSeverity.Blocker,
        _ => ConsoleValidationSeverity.Error,
    };

    public static ConsoleValidationSeverity ParseSeverity(string? severity) =>
        severity?.Trim().ToLowerInvariant() switch
        {
            "info" => ConsoleValidationSeverity.Info,
            "warning" or "warn" => ConsoleValidationSeverity.Warning,
            "blocker" or "fatal" => ConsoleValidationSeverity.Blocker,
            _ => ConsoleValidationSeverity.Error,
        };
}
