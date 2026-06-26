namespace Honua.Console.Shell.Models;

// Console-side model for the XLSForm (ODK Collect) import surface (Studio "Form · Import XLSForm").
// The conversion engine is server-owned (Honua.Collect.Core XlsFormImporter); the wire contract is
// Honua.Console.Contracts.FormPackageShims (import-xlsform endpoint). Nothing here fabricates a form: an
// import outcome is either a server-produced document (mapped through StudioFormPackageMapper) or carries
// a capability/binding state — and the importer's diagnostics are surfaced, never dropped.

/// <summary>What the operator uploaded for one XLSForm import attempt.</summary>
public sealed record StudioFormImportRequest
{
    /// <summary>Original workbook file name (drives the server-side reader + diagnostics).</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>The workbook content type, when the browser reported one.</summary>
    public string? ContentType { get; init; }

    /// <summary>The raw workbook bytes.</summary>
    public byte[] Content { get; init; } = [];
}

/// <summary>The result of an XLSForm import attempt.</summary>
public sealed record StudioFormImportOutcome
{
    /// <summary>Set only when the surface is unbound; the page shows the shared blocked state.</summary>
    public StudioFormCapabilityState? BindingState { get; init; }

    /// <summary>"imported" | "unsupported" | "rejected" | "error".</summary>
    public string Status { get; init; } = StudioFormImportStatuses.Error;

    /// <summary>The editor state with the converted package applied; present iff status == imported.</summary>
    public StudioFormEditorState? State { get; init; }

    /// <summary>The detail line to show the operator (success summary or failure reason).</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Importer diagnostics (unsupported types, dropped constructs) — surfaced, not dropped.</summary>
    public IReadOnlyList<StudioFormImportDiagnostic> Diagnostics { get; init; } = [];

    public bool IsImported =>
        string.Equals(Status, StudioFormImportStatuses.Imported, StringComparison.Ordinal);

    public static StudioFormImportOutcome Blocked(StudioFormCapabilityState binding) =>
        new() { BindingState = binding, Status = StudioFormImportStatuses.Error };

    public static StudioFormImportOutcome Failed(string status, string message) =>
        new() { Status = status, Message = message };
}

/// <summary>One importer finding the operator should see before saving the imported draft.</summary>
public sealed record StudioFormImportDiagnostic(string Severity, string Code, string? Location, string Message);

public static class StudioFormImportStatuses
{
    public const string Imported = "imported";
    public const string Unsupported = "unsupported";
    public const string Rejected = "rejected";
    public const string Error = "error";
}
