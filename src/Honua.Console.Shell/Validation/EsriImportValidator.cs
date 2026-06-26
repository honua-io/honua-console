using System.Text.Json;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// Stable console-owned field keys for the Esri content-import intake surfaces (#100 Web Map, #101
/// Dashboard, #104 StoryMap, #158 Instant App, #159 Notebook) and the import wizard (#102).
/// </summary>
public static class EsriImportFieldKeys
{
    /// <summary>The intake source as a whole — at least one of paste / upload / URL must be supplied.</summary>
    public const string Source = "esri.import.source";

    /// <summary>The pasted (or uploaded) JSON body.</summary>
    public const string Json = "esri.import.json";

    /// <summary>The intake URL / item-id when the URL mode is active.</summary>
    public const string Url = "esri.import.url";
}

/// <summary>
/// The intake mode an operator has selected on the shared Esri intake bar. Paste / Upload are
/// deterministic console-side parses; URL / item-id and connected-ArcGIS need a live server fetch.
/// </summary>
public enum EsriIntakeMode
{
    /// <summary>Paste raw content JSON.</summary>
    Paste,

    /// <summary>Upload a content JSON file.</summary>
    Upload,

    /// <summary>Fetch by URL or item id (needs a bound honua-server).</summary>
    Url,

    /// <summary>Read from a connected ArcGIS organization (needs a bound honua-server).</summary>
    ConnectedArcGis,
}

/// <summary>
/// Console-owned snapshot of the Esri intake the client validator evaluates: the active intake
/// <see cref="Mode"/>, the pasted JSON (paste mode), the selected upload file name (upload mode), and
/// the typed URL / item id (URL mode). The validator gates the intake before a parse / fetch is issued;
/// it never contacts a server.
/// </summary>
/// <param name="Kind">Which content surface this intake belongs to (drives the shape expectation).</param>
/// <param name="Mode">The active intake mode.</param>
/// <param name="PastedJson">The pasted JSON body (paste mode). Null/blank when not in paste mode.</param>
/// <param name="UploadFileName">The chosen upload file name (upload mode). Null when nothing is staged.</param>
/// <param name="Url">The typed URL / item id (URL mode). Null/blank when not in URL mode.</param>
public sealed record EsriImportIntakeState(
    EsriContentKind Kind,
    EsriIntakeMode Mode,
    string? PastedJson = null,
    string? UploadFileName = null,
    string? Url = null);

/// <summary>
/// Pure client-side validator for the Esri content-import intake forms (#100 / #101 / #104). It enforces,
/// before a parse or server fetch is issued:
/// <list type="bullet">
///   <item>a source must be provided — paste JSON, an uploaded file, a URL / item id, or a connected ArcGIS read;</item>
///   <item>pasted JSON must parse <em>and</em> be a JSON object (the shape every Esri Web Map / Dashboard / StoryMap export uses);</item>
///   <item>an intake URL must be a valid absolute http(s) URL (a bare item id is also accepted as an identifier).</item>
/// </list>
/// Mirrors the <see cref="ShareManageValidator"/> pattern; keyed by <see cref="EsriImportFieldKeys"/>.
/// Console-only — the deterministic parse owns rich per-row fidelity; this validator only gates the intake.
/// </summary>
public sealed class EsriImportValidator : IFieldValidator<EsriImportIntakeState>
{
    private static readonly JsonDocumentOptions DocOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Shared singleton; the validator holds no state.</summary>
    public static EsriImportValidator Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<ConsoleFieldError> Evaluate(EsriImportIntakeState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var errors = new List<ConsoleFieldError>();

        var hasPaste = !string.IsNullOrWhiteSpace(state.PastedJson);
        var hasUpload = !string.IsNullOrWhiteSpace(state.UploadFileName);
        var hasUrl = !string.IsNullOrWhiteSpace(state.Url);
        // Connected-ArcGIS reads bind a live server: the intake itself carries no console-side payload, so
        // it counts as "a source has been chosen" and defers the actual resolvability to the server binding.
        var hasConnected = state.Mode == EsriIntakeMode.ConnectedArcGis;

        if (!hasPaste && !hasUpload && !hasUrl && !hasConnected)
        {
            errors.Add(Blocker(
                EsriImportFieldKeys.Source,
                "esri.import.source.required",
                "Provide a source: paste JSON, upload a file, enter a URL / item id, or read from a connected ArcGIS organization."));
        }

        // Pasted JSON must parse and be a JSON object (the shape every Esri export uses).
        if (hasPaste)
        {
            var jsonError = ValidateJsonObject(state.PastedJson!);
            if (jsonError is not null)
            {
                errors.Add(Error(EsriImportFieldKeys.Json, "esri.import.json.invalid", jsonError));
            }
        }

        // A URL-mode intake must be either an absolute http(s) URL or a plausible item-id identifier.
        if (state.Mode == EsriIntakeMode.Url && hasUrl
            && !UrlRule.IsAbsoluteHttp(state.Url) && !IdentifierRule.IsValid(state.Url))
        {
            errors.Add(Error(
                EsriImportFieldKeys.Url,
                "esri.import.url.invalid",
                "Enter an absolute http(s) URL or an ArcGIS item id (letters, numbers, '-', '_', '.', ':')."));
        }

        return errors;
    }

    private static string? ValidateJsonObject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, DocOptions);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? null
                : "The pasted content must be a JSON object (an Esri Web Map / Dashboard / StoryMap document).";
        }
        catch (JsonException ex)
        {
            return $"Not valid JSON: {ex.Message}";
        }
    }

    private static ConsoleFieldError Blocker(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Blocker, message);

    private static ConsoleFieldError Error(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Error, message);
}
