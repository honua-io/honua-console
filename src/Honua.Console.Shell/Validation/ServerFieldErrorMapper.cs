using Honua.Sdk.Studio.Packages;
using System.Text.Json.Serialization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// The RFC-7807 <c>ProblemDetails.errors[]</c> item shape — the server Wave-0 <c>FieldValidationError</c>
/// projection. A generic field-addressable error usable by any console HTTP client that does not have a
/// bespoke validation contract; <see cref="ServerFieldErrorMapper"/> maps it onto
/// <see cref="ConsoleFieldError"/>.
/// </summary>
public sealed record ConsoleFieldValidationError
{
    /// <summary>Machine code for the rule.</summary>
    [JsonPropertyName("code")] public string Code { get; init; } = string.Empty;

    /// <summary>Severity string (<c>info</c>/<c>warning</c>/<c>error</c>/<c>blocker</c>); defaults to error.</summary>
    [JsonPropertyName("severity")] public string? Severity { get; init; }

    /// <summary>Optional JSON-Pointer locator of the offending value.</summary>
    [JsonPropertyName("path")] public string? Path { get; init; }

    /// <summary>Optional field identifier alias (preferred over <see cref="Path"/> when present).</summary>
    [JsonPropertyName("fieldId")] public string? FieldId { get; init; }

    /// <summary>Human-readable, operator-facing message.</summary>
    [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Converts each server-side validation shape the console receives into the single
/// <see cref="ConsoleFieldError"/> vocabulary:
/// <list type="bullet">
///   <item>Studio package diagnostics — <c>{code,severity,path,message}</c> (<see cref="StudioValidationDiagnostic"/>).</item>
///   <item>Form package issues — <c>{code,severity,fieldId,path,message}</c> (<see cref="HonuaFormValidationIssue"/>).</item>
///   <item>Workflow package failures — <c>{code,message,fieldPath}</c> (<see cref="WorkflowPackageValidationFailure"/>).</item>
///   <item>Generic RFC-7807 <c>ProblemDetails.errors[]</c> (<see cref="ConsoleFieldValidationError"/>).</item>
/// </list>
/// A caller-supplied <see cref="FieldKeyResolver"/> hook maps each shape's source locator
/// (JSON-Pointer / fieldId / fieldPath) to the console-owned <see cref="ConsoleFieldError.FieldKey"/>;
/// when no resolver is supplied (Wave 0, before per-editor pointer maps land) the raw locator is used
/// as the key so findings still surface and round-trip.
/// </summary>
public sealed class ServerFieldErrorMapper
{
    /// <summary>
    /// Resolves a server source locator to a console field key. Receives the locator (JSON-Pointer,
    /// fieldId, or fieldPath — whichever the source shape carries, normalised by the mapper) and the
    /// rule code; returns the console <c>FieldKey</c>, or <see langword="null"/> to fall back to the raw
    /// locator. Per-editor waves register concrete resolvers; Wave 0 ships the identity fallback.
    /// </summary>
    public delegate string? FieldKeyResolver(string? locator, string code);

    /// <summary>The console-wide form-level key used when a finding carries no locator at all.</summary>
    public const string FormLevelFieldKey = "_form";

    private readonly FieldKeyResolver _resolve;

    /// <summary>Creates a mapper with an optional <paramref name="resolver"/>; defaults to the identity fallback.</summary>
    public ServerFieldErrorMapper(FieldKeyResolver? resolver = null) =>
        _resolve = resolver ?? ((locator, _) => locator);

    /// <summary>Maps Studio package validate diagnostics (<c>{code,severity,path,message}</c>).</summary>
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

    /// <summary>Maps Form package validate issues (<c>{code,severity,fieldId,path,message}</c>).</summary>
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

    /// <summary>Maps Workflow package validate failures (<c>{code,message,fieldPath}</c>).</summary>
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

    /// <summary>Maps the generic RFC-7807 <c>ProblemDetails.errors[]</c> shape.</summary>
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

    /// <summary>Parses a free server severity string onto the console ladder; unknown/empty defaults to error.</summary>
    public static ConsoleValidationSeverity ParseSeverity(string? severity) =>
        severity?.Trim().ToLowerInvariant() switch
        {
            "info" => ConsoleValidationSeverity.Info,
            "warning" or "warn" => ConsoleValidationSeverity.Warning,
            "blocker" or "fatal" => ConsoleValidationSeverity.Blocker,
            _ => ConsoleValidationSeverity.Error,
        };
}
