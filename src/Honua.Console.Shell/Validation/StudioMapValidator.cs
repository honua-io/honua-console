using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// Stable console-owned field keys for the Studio map builder. The client validator
/// (<see cref="StudioMapValidator"/>), the inline render surfaces, and the server-diagnostic resolver
/// (<see cref="StudioMapServerErrorBinder"/>) all share these so a client finding and a server finding for the
/// same input land on the same key. Form-level keys are constants; per-layer keys are derived from the
/// layer's <em>position</em> (which is exactly what the server's <c>/body/layers/{index}/…</c> JSON-Pointer
/// addresses) so every authored layer row is independently addressable.
/// </summary>
public static class StudioMapFieldKeys
{
    public const string Title = "map.title";
    public const string Layers = "map.layers";
    public const string Basemap = "map.basemap";
    public const string InitialExtent = "map.initialExtent";

    /// <summary>Per-layer source-ref key for the layer at <paramref name="index"/>.</summary>
    public static string LayerSourceRef(int index) => $"map.layer[{index}].sourceRef";
}

/// <summary>
/// Pure client-side cross-field / bounds / format validator for the Studio map builder, mirroring the
/// <see cref="StudioFormValidator"/> pattern: it examines the console-owned <see cref="StudioMapEditorState"/>
/// and emits field-addressable <see cref="ConsoleFieldError"/> findings keyed by <see cref="StudioMapFieldKeys"/>
/// so the editor can surface them inline next to the offending input. It complements — never replaces —
/// server validation; it covers only the rules expressible against console-owned state (presence + the
/// bbox parse/ordering rule the server also enforces as <c>studio.map.initial-view.bbox.order</c>).
/// </summary>
public sealed class StudioMapValidator : IFieldValidator<StudioMapEditorState>
{
    /// <summary>Shared singleton; the validator holds no state.</summary>
    public static StudioMapValidator Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<ConsoleFieldError> Evaluate(StudioMapEditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var errors = new List<ConsoleFieldError>();

        if (string.IsNullOrWhiteSpace(state.Title))
        {
            errors.Add(Blocker(StudioMapFieldKeys.Title, "map.title.required", "Give the map a title."));
        }

        if (state.Layers.Count == 0)
        {
            errors.Add(Blocker(StudioMapFieldKeys.Layers, "map.layers.required", "Add at least one layer."));
        }

        for (var index = 0; index < state.Layers.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(state.Layers[index].SourceRef))
            {
                errors.Add(Blocker(
                    StudioMapFieldKeys.LayerSourceRef(index),
                    "map.layer.sourceRef.required",
                    "Bind a source reference for this layer."));
            }
        }

        if (string.IsNullOrWhiteSpace(state.Basemap))
        {
            errors.Add(Blocker(StudioMapFieldKeys.Basemap, "map.basemap.required", "Select a basemap."));
        }

        EvaluateInitialExtent(state, errors);

        return errors;
    }

    private static void EvaluateInitialExtent(StudioMapEditorState state, List<ConsoleFieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(state.InitialExtent))
        {
            errors.Add(Blocker(StudioMapFieldKeys.InitialExtent, "map.initialExtent.required", "Set the initial extent."));
            return;
        }

        if (BboxParser.TryParse(state.InitialExtent, out _, out var error))
        {
            return;
        }

        var message = error switch
        {
            BboxParser.BboxError.XOrder => "Initial extent is inverted: minX must be less than or equal to maxX.",
            BboxParser.BboxError.YOrder => "Initial extent is inverted: minY must be less than or equal to maxY.",
            _ => "Initial extent must be four numbers \"minX,minY,maxX,maxY\".",
        };

        var code = error switch
        {
            BboxParser.BboxError.XOrder or BboxParser.BboxError.YOrder => "map.initialExtent.order",
            _ => "map.initialExtent.malformed",
        };

        errors.Add(Error(StudioMapFieldKeys.InitialExtent, code, message));
    }

    private static ConsoleFieldError Blocker(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Blocker, message);

    private static ConsoleFieldError Error(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Error, message);
}

/// <summary>
/// Binds the server-returned Studio validation diagnostics (<c>{code,severity,path,message}</c>, carried on a
/// non-2xx response by <c>HttpStudioPackageLifecycleClient</c>) onto the map editor's
/// <see cref="ValidationState"/> server channel, keyed by the same <see cref="StudioMapFieldKeys"/> the client
/// validator uses, by resolving each diagnostic's JSON Pointer (e.g. <c>/body/layers/2/sourceRef</c>) to the
/// matching console field key. A diagnostic whose pointer cannot be resolved falls back to the raw locator /
/// form-level key so it still surfaces. It complements server validation — the authoritative result still
/// drives the page status; this binder only re-homes the diagnostics onto fields.
/// </summary>
public static class StudioMapServerErrorBinder
{
    /// <summary>Maps <paramref name="diagnostics"/> onto console field keys via the JSON-Pointer resolver.</summary>
    public static IReadOnlyList<ConsoleFieldError> Map(
        IEnumerable<Contracts.StudioValidationDiagnostic>? diagnostics)
    {
        if (diagnostics is null)
        {
            return Array.Empty<ConsoleFieldError>();
        }

        var mapper = new ServerFieldErrorMapper((locator, _) => StudioMapPointerResolver.Resolve(locator));
        return mapper.Map(diagnostics);
    }
}

/// <summary>
/// Resolves a Studio map-package JSON Pointer (the <c>path</c> on a server diagnostic) to the console-owned
/// <see cref="StudioMapFieldKeys"/> for the offending input. The server addresses the envelope body, so the
/// pointers are rooted at <c>/body</c> (e.g. <c>/body/layers/2/sourceRef</c>, <c>/body/initialExtent</c>,
/// <c>/body/title</c>, <c>/body/basemap</c>). Returns <see langword="null"/> for an unrecognised pointer so the
/// mapper falls back to the raw locator.
/// </summary>
public static class StudioMapPointerResolver
{
    /// <summary>Resolves <paramref name="pointer"/> to a console field key, or <see langword="null"/>.</summary>
    public static string? Resolve(string? pointer)
    {
        var segments = JsonPointer.Split(pointer);
        if (segments.Count == 0)
        {
            return null;
        }

        // Pointers are rooted at the package envelope body; drop a leading "body" so /body/layers/2/sourceRef
        // and /layers/2/sourceRef both resolve.
        var index = 0;
        if (string.Equals(segments[0], "body", StringComparison.OrdinalIgnoreCase))
        {
            index = 1;
        }

        if (index >= segments.Count)
        {
            return null;
        }

        var head = segments[index];

        if (string.Equals(head, "layers", StringComparison.OrdinalIgnoreCase))
        {
            // /…/layers/{n}/sourceRef -> the layer's source-ref row.
            if (index + 1 < segments.Count && int.TryParse(segments[index + 1], out var layerIndex) && layerIndex >= 0)
            {
                return StudioMapFieldKeys.LayerSourceRef(layerIndex);
            }

            return StudioMapFieldKeys.Layers;
        }

        return head.ToLowerInvariant() switch
        {
            "title" => StudioMapFieldKeys.Title,
            "basemap" => StudioMapFieldKeys.Basemap,
            "initialextent" => StudioMapFieldKeys.InitialExtent,
            _ => null,
        };
    }
}
