namespace Honua.Console.Shell.Models;

/// <summary>
/// The single console-wide validation vocabulary. Every client-computed rule and every server
/// validation shape (Studio diagnostics <c>{code,severity,path,message}</c>, Form issues
/// <c>{code,severity,fieldId,path,message}</c>, Workflow failures <c>{code,message,fieldPath}</c>,
/// and the RFC-7807 <c>ProblemDetails.errors[]</c> envelope) maps onto this severity ladder so a
/// single rendering and merge surface can present them all. Ordered least-to-most severe.
/// </summary>
public enum ConsoleValidationSeverity
{
    /// <summary>Advisory note; never blocks a save/publish.</summary>
    Info,

    /// <summary>A concern the operator should review but that does not block the operation.</summary>
    Warning,

    /// <summary>A field-level error that should block the field's save until corrected.</summary>
    Error,

    /// <summary>A hard gate; the package cannot be published/saved while present.</summary>
    Blocker,
}

/// <summary>
/// A single field-addressable validation finding. The whole console normalises onto this shape:
/// client-side cross-field/business-rule evaluators emit it directly, and
/// <see cref="Honua.Console.Shell.Validation.ServerFieldErrorMapper"/> converts each server validation
/// shape into it. Findings are keyed for rendering by <see cref="FieldKey"/> — the stable
/// console-owned identifier of the input the finding belongs to (resolved from a server JSON-Pointer /
/// fieldId / fieldPath where necessary).
/// </summary>
/// <param name="FieldKey">
/// Stable console-owned key of the field this finding belongs to (e.g. <c>"map.initialExtent"</c>).
/// Findings with no resolvable field use a shared form-level key so they still surface.
/// </param>
/// <param name="Code">Machine code for the rule (e.g. <c>"bbox.order"</c> or a server diagnostic code).</param>
/// <param name="Severity">Where the finding sits on the console severity ladder.</param>
/// <param name="Message">Human-readable, operator-facing message.</param>
/// <param name="Path">
/// Optional source locator the finding originated from (JSON-Pointer / fieldId / fieldPath) before it
/// was resolved to <see cref="FieldKey"/>. Preserved for diagnostics and round-tripping.
/// </param>
public sealed record ConsoleFieldError(
    string FieldKey,
    string Code,
    ConsoleValidationSeverity Severity,
    string Message,
    string? Path = null)
{
    /// <summary>True when this finding blocks the operation (<see cref="ConsoleValidationSeverity.Error"/> or above).</summary>
    public bool IsBlocking => Severity is ConsoleValidationSeverity.Error or ConsoleValidationSeverity.Blocker;
}
