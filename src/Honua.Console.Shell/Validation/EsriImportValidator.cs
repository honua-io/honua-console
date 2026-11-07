using System.Text.Json;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

public static class EsriImportFieldKeys
{
    public const string Source = "esri.import.source";

    public const string Json = "esri.import.json";

    public const string Url = "esri.import.url";
}

public enum EsriIntakeMode
{
    Paste,

    Upload,

    Url,

    ConnectedArcGis,
}

public sealed record EsriImportIntakeState(
    EsriContentKind Kind,
    EsriIntakeMode Mode,
    string? PastedJson = null,
    string? UploadFileName = null,
    string? Url = null);

public sealed class EsriImportValidator : IFieldValidator<EsriImportIntakeState>
{
    private static readonly JsonDocumentOptions DocOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static EsriImportValidator Instance { get; } = new();

    public IReadOnlyList<ConsoleFieldError> Evaluate(EsriImportIntakeState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var errors = new List<ConsoleFieldError>();

        var hasPaste = !string.IsNullOrWhiteSpace(state.PastedJson);
        var hasUpload = !string.IsNullOrWhiteSpace(state.UploadFileName);
        var hasUrl = !string.IsNullOrWhiteSpace(state.Url);
        // Connected-source reads bind a live server: the intake itself carries no console-side payload, so
        // it counts as "a source has been chosen" and defers the actual resolvability to the server binding.
        var hasConnected = state.Mode == EsriIntakeMode.ConnectedArcGis;

        if (!hasPaste && !hasUpload && !hasUrl && !hasConnected)
        {
            errors.Add(Blocker(
                EsriImportFieldKeys.Source,
                "esri.import.source.required",
                "Provide a source: paste JSON, upload a file, enter a URL / item id, or read from a connected source organization."));
        }

        // Pasted JSON must parse and be a JSON object (the shape every source export uses).
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
                "Enter an absolute http(s) URL or a source item id (letters, numbers, '-', '_', '.', ':')."));
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
                : "The pasted content must be a JSON object (a source Web Map / Dashboard / StoryMap document).";
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
