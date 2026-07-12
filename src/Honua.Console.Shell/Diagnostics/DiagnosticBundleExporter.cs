using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Diagnostics;

/// <summary>
/// Thrown when a diagnostic bundle fails canonical-schema validation. The export is blocked so a
/// non-conforming (or unsafe) bundle can never be downloaded or uploaded (honua-console#307).
/// The message is safe to surface to an operator; it names the violations, not payload contents.
/// </summary>
public sealed class DiagnosticBundleValidationException : Exception
{
    public DiagnosticBundleValidationException(IReadOnlyList<string> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(IReadOnlyList<string> errors)
    {
        string detail = errors.Count == 0
            ? "unknown validation error"
            : string.Join("; ", errors);
        return $"Diagnostic bundle does not conform to diagnostic-bundle.v1 and was not exported: {detail}";
    }
}

/// <summary>
/// Serializes a <see cref="DiagnosticBundle"/> and validates the exact bytes against the pinned
/// canonical schema BEFORE they can be downloaded or uploaded. Serialization omits null optionals
/// (never explicit <c>null</c>), and validation is the authoritative gate: a bundle that violates
/// the schema — or that a sanitization slip left non-conforming — throws
/// <see cref="DiagnosticBundleValidationException"/> instead of producing output.
/// </summary>
public sealed class DiagnosticBundleExporter
{
    private readonly DiagnosticBundleSchema _schema;

    public DiagnosticBundleExporter()
        : this(new DiagnosticBundleSchema())
    {
    }

    public DiagnosticBundleExporter(DiagnosticBundleSchema schema)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
    }

    /// <summary>
    /// Serializes and validates the bundle, returning the UTF-8 JSON bytes ready for download or
    /// upload. Throws <see cref="DiagnosticBundleValidationException"/> if the bundle does not
    /// conform to the canonical schema.
    /// </summary>
    public byte[] Export(DiagnosticBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            bundle, DiagnosticBundleJsonContext.Default.DiagnosticBundle);

        using JsonDocument document = JsonDocument.Parse(bytes);
        IReadOnlyList<string> errors = _schema.Validate(document.RootElement);
        if (errors.Count > 0)
            throw new DiagnosticBundleValidationException(errors);

        return bytes;
    }

    /// <summary>Convenience over <see cref="Export"/> returning the JSON as a UTF-8 string.</summary>
    public string ExportToJson(DiagnosticBundle bundle) => Encoding.UTF8.GetString(Export(bundle));

    /// <summary>
    /// Validates a bundle without producing output. Returns the (possibly empty) list of schema
    /// violations so a caller can gate a download affordance before offering it.
    /// </summary>
    public IReadOnlyList<string> Validate(DiagnosticBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            bundle, DiagnosticBundleJsonContext.Default.DiagnosticBundle);
        using JsonDocument document = JsonDocument.Parse(bytes);
        return _schema.Validate(document.RootElement);
    }
}
