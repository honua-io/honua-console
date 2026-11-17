using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

// Console-side outcome for XLSForm import and export (Form builder). honua-server converts the workbook
// (Honua.Console.Contracts.FormXlsFormClient); nothing here fabricates a form. A failed import or export keeps
// every blocking cell the server reported so the builder can fix the workbook or the package.
public sealed record StudioFormXlsFormOutcome
{
    public bool Succeeded { get; private init; }

    public string Message { get; private init; } = string.Empty;

    public IReadOnlyList<HonuaFormXlsFormDiagnostic> Diagnostics { get; private init; } = [];

    public StudioFormCapabilityState? Issue { get; private init; }

    // Import: the new draft to open.
    public string? FormId { get; private init; }

    // Export: the workbook to download.
    public HonuaFormXlsFormWorkbook? Workbook { get; private init; }

    public static StudioFormXlsFormOutcome Imported(string message, string formId) =>
        new() { Succeeded = true, Message = message, FormId = formId };

    public static StudioFormXlsFormOutcome Exported(string message, HonuaFormXlsFormWorkbook workbook) =>
        new() { Succeeded = true, Message = message, Workbook = workbook };

    public static StudioFormXlsFormOutcome Rejected(
        string message,
        IReadOnlyList<HonuaFormXlsFormDiagnostic> diagnostics,
        StudioFormCapabilityState? issue = null) =>
        new() { Message = message, Diagnostics = diagnostics, Issue = issue };
}
