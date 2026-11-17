using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

// Console-side outcomes for XLSForm import and export (Form builder). honua-server converts the workbook
// (Honua.Console.Contracts.FormXlsFormShims); nothing here fabricates a form. A failed import or export keeps
// every blocking cell the server reported so the builder can fix the workbook or the package.
public sealed record StudioFormXlsFormRequest(string ServiceId, int LayerId, string FileName, byte[] Content);

public sealed record StudioFormXlsFormDiagnostic(string Sheet, int Row, string Column, string Message)
{
    public static StudioFormXlsFormDiagnostic From(HonuaFormXlsFormDiagnostic diagnostic) =>
        new(diagnostic.Sheet, diagnostic.Row, diagnostic.Column, diagnostic.Message);
}

public sealed record StudioFormXlsFormOutcome(
    bool Succeeded,
    string Message,
    IReadOnlyList<StudioFormXlsFormDiagnostic> Diagnostics,
    StudioFormCapabilityState? Issue = null)
{
    // Import: the new draft to open. Export: the workbook to download.
    public string? FormId { get; init; }

    public HonuaFormXlsFormWorkbook? Workbook { get; init; }
}
