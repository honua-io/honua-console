using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Diagnostics;

// The message lists schema violations, not captured payload contents.
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

    public string ExportToJson(DiagnosticBundle bundle) => Encoding.UTF8.GetString(Export(bundle));

    public IReadOnlyList<string> Validate(DiagnosticBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            bundle, DiagnosticBundleJsonContext.Default.DiagnosticBundle);
        using JsonDocument document = JsonDocument.Parse(bytes);
        return _schema.Validate(document.RootElement);
    }
}
