using System.Globalization;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// Stable console-owned field keys for the Studio query builder. Shared by the client validator
/// (<see cref="StudioQueryValidator"/>), the inline render surfaces, and the server-diagnostic resolver
/// (<see cref="StudioQueryPointerResolver"/>) so a client finding and a server finding for the same input
/// land on the same key. Form-level keys are constants; per-predicate keys are derived from the predicate's
/// <em>position</em> (which is exactly what the server's <c>/body/predicates/{index}/…</c> JSON-Pointer
/// addresses) so every authored predicate row is independently addressable.
/// </summary>
public static class StudioQueryFieldKeys
{
    public const string ServiceName = "query.serviceName";
    public const string LayerId = "query.layerId";
    public const string OutputSrid = "query.outputSrid";
    public const string PreviewLimit = "query.previewLimit";

    /// <summary>Per-predicate temporal-range key for the predicate at <paramref name="index"/>.</summary>
    public static string PredicateRange(int index) => $"query.predicate[{index}].range";

    /// <summary>Per-predicate distance key for the predicate at <paramref name="index"/>.</summary>
    public static string PredicateDistance(int index) => $"query.predicate[{index}].distance";

    /// <summary>Per-predicate geometry key for the predicate at <paramref name="index"/>.</summary>
    public static string PredicateGeometry(int index) => $"query.predicate[{index}].geometry";
}

/// <summary>
/// Pure client-side cross-field / bounds / format validator for the Studio query builder, mirroring the
/// <see cref="StudioFormValidator"/> pattern: it examines the console-owned <see cref="StudioQueryEditor"/>
/// and emits field-addressable <see cref="ConsoleFieldError"/> findings keyed by
/// <see cref="StudioQueryFieldKeys"/> so the editor can surface them inline next to the offending input. It
/// complements — never replaces — server validation; it covers only the rules expressible against
/// console-owned state (presence, bounds, temporal ordering, dwithin distance, and a GeoJSON shape gate).
/// </summary>
public sealed class StudioQueryValidator : IFieldValidator<StudioQueryEditor>
{
    /// <summary>Shared singleton; the validator holds no state.</summary>
    public static StudioQueryValidator Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<ConsoleFieldError> Evaluate(StudioQueryEditor state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var errors = new List<ConsoleFieldError>();

        EvaluateSource(state, errors);
        EvaluateOutput(state, errors);
        EvaluatePredicates(state, errors);

        return errors;
    }

    private static void EvaluateSource(StudioQueryEditor state, List<ConsoleFieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(state.ServiceName))
        {
            errors.Add(Blocker(StudioQueryFieldKeys.ServiceName, "query.serviceName.required", "Bind a source service name."));
        }

        // LayerId is a non-nullable int; presence is always satisfied. The catalog rule is LayerId >= 0.
        if (state.LayerId < 0)
        {
            errors.Add(Blocker(StudioQueryFieldKeys.LayerId, "query.layerId.min", "Layer id must be zero or greater."));
        }
    }

    private static void EvaluateOutput(StudioQueryEditor state, List<ConsoleFieldError> errors)
    {
        // OutputSrid is optional; when set it must be positive.
        if (state.OutputSrid is { } srid && !NumericBoundsRule.IsWithin(srid, min: 1))
        {
            errors.Add(Error(StudioQueryFieldKeys.OutputSrid, "query.outputSrid.positive", "Output SRID must be a positive number."));
        }

        if (!NumericBoundsRule.IsWithin(state.PreviewLimit, min: 1))
        {
            errors.Add(Error(StudioQueryFieldKeys.PreviewLimit, "query.previewLimit.min", "Preview limit must be at least 1."));
        }
    }

    private static void EvaluatePredicates(StudioQueryEditor state, List<ConsoleFieldError> errors)
    {
        for (var index = 0; index < state.Predicates.Count; index++)
        {
            var predicate = state.Predicates[index];

            if (string.Equals(predicate.Kind, StudioQueryPredicateKinds.Temporal, StringComparison.OrdinalIgnoreCase))
            {
                EvaluateTemporal(predicate, index, errors);
            }
            else if (string.Equals(predicate.Kind, StudioQueryPredicateKinds.Spatial, StringComparison.OrdinalIgnoreCase))
            {
                EvaluateSpatial(predicate, index, errors);
            }
        }
    }

    private static void EvaluateTemporal(StudioQueryPredicateEditor predicate, int index, List<ConsoleFieldError> errors)
    {
        var range = IsoDateRule.CheckRange(predicate.Start, predicate.End);
        if (range == IsoDateRule.RangeError.None)
        {
            return;
        }

        var message = range switch
        {
            IsoDateRule.RangeError.FromUnparseable => "Start must be an ISO-8601 date or datetime.",
            IsoDateRule.RangeError.ToUnparseable => "End must be an ISO-8601 date or datetime.",
            _ => "Start must be on or before End.",
        };

        var code = range == IsoDateRule.RangeError.Inverted ? "query.predicate.temporal.order" : "query.predicate.temporal.format";
        errors.Add(Error(StudioQueryFieldKeys.PredicateRange(index), code, message));
    }

    private static void EvaluateSpatial(StudioQueryPredicateEditor predicate, int index, List<ConsoleFieldError> errors)
    {
        // A dwithin clause needs a positive distance (carried in Value).
        if (string.Equals(predicate.Operator, "dwithin", StringComparison.OrdinalIgnoreCase))
        {
            var hasDistance = double.TryParse(
                predicate.Value?.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var distance);

            if (!hasDistance || !NumericBoundsRule.IsWithin(distance, min: double.Epsilon))
            {
                errors.Add(Error(
                    StudioQueryFieldKeys.PredicateDistance(index),
                    "query.predicate.dwithin.distance",
                    "A dwithin clause requires a distance greater than 0."));
            }
        }

        // The spatial geometry literal must parse as GeoJSON. An empty geometry is a presence concern the
        // server enforces; only a non-empty-but-unparseable literal is a client format error.
        if (!string.IsNullOrWhiteSpace(predicate.Geometry) && !GeoJsonRule.IsValidGeometry(predicate.Geometry))
        {
            errors.Add(Error(
                StudioQueryFieldKeys.PredicateGeometry(index),
                "query.predicate.geometry.geojson",
                "Geometry must be a valid GeoJSON geometry."));
        }
    }

    private static ConsoleFieldError Blocker(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Blocker, message);

    private static ConsoleFieldError Error(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Error, message);
}

/// <summary>
/// Resolves a Studio query JSON Pointer (the <c>path</c> on a server diagnostic) to the console-owned
/// <see cref="StudioQueryFieldKeys"/> for the offending input. The server addresses the envelope body, so the
/// pointers are rooted at <c>/body</c> (e.g. <c>/body/predicates/1/start</c>, <c>/body/serviceName</c>,
/// <c>/body/layerId</c>). Returns <see langword="null"/> for an unrecognised pointer so the mapper falls back
/// to the raw locator.
/// </summary>
public static class StudioQueryPointerResolver
{
    /// <summary>Resolves <paramref name="pointer"/> to a console field key, or <see langword="null"/>.</summary>
    public static string? Resolve(string? pointer)
    {
        var segments = JsonPointer.Split(pointer);
        if (segments.Count == 0)
        {
            return null;
        }

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

        if (string.Equals(head, "predicates", StringComparison.OrdinalIgnoreCase)
            && index + 1 < segments.Count
            && int.TryParse(segments[index + 1], out var predicateIndex)
            && predicateIndex >= 0)
        {
            var leaf = index + 2 < segments.Count ? segments[index + 2].ToLowerInvariant() : string.Empty;
            return leaf switch
            {
                "start" or "end" => StudioQueryFieldKeys.PredicateRange(predicateIndex),
                "distance" or "value" => StudioQueryFieldKeys.PredicateDistance(predicateIndex),
                "geometry" => StudioQueryFieldKeys.PredicateGeometry(predicateIndex),
                // No specific leaf — default a temporal/range finding onto the predicate's range row.
                _ => StudioQueryFieldKeys.PredicateRange(predicateIndex),
            };
        }

        return head.ToLowerInvariant() switch
        {
            "servicename" => StudioQueryFieldKeys.ServiceName,
            "layerid" => StudioQueryFieldKeys.LayerId,
            "outputsrid" => StudioQueryFieldKeys.OutputSrid,
            "previewlimit" => StudioQueryFieldKeys.PreviewLimit,
            _ => null,
        };
    }
}

/// <summary>
/// Binds the server-returned Studio validation diagnostics onto the query editor's
/// <see cref="ValidationState"/> server channel, keyed by <see cref="StudioQueryFieldKeys"/> via
/// <see cref="StudioQueryPointerResolver"/>. Kept symmetric with <see cref="StudioMapServerErrorBinder"/> so
/// the query builder can reuse the same diagnostics channel if/when its data source carries Studio
/// diagnostics.
/// </summary>
public static class StudioQueryServerErrorBinder
{
    /// <summary>Maps <paramref name="diagnostics"/> onto console field keys via the JSON-Pointer resolver.</summary>
    public static IReadOnlyList<ConsoleFieldError> Map(
        IEnumerable<Contracts.StudioValidationDiagnostic>? diagnostics)
    {
        if (diagnostics is null)
        {
            return Array.Empty<ConsoleFieldError>();
        }

        var mapper = new ServerFieldErrorMapper((locator, _) => StudioQueryPointerResolver.Resolve(locator));
        return mapper.Map(diagnostics);
    }
}
