using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// Stable console-owned field keys for the Studio analysis builder. The client validator
/// (<see cref="StudioAnalysisValidator"/>), the inline render surfaces, and the server-error resolver
/// (<see cref="StudioAnalysisServerErrorBinder"/>) all share these so a client finding and a server finding
/// for the same input land on the same key. Form-level keys are constants; per-input, per-parameter, and
/// per-output-field keys are derived from the item's <em>position</em> (which is what the server's
/// <c>/inputs/{n}/…</c> / <c>/outputSchema/{n}/…</c> field paths address) so every authored row is
/// independently addressable.
/// </summary>
public static class StudioAnalysisFieldKeys
{
    public const string Method = "analysis.method";
    public const string Inputs = "analysis.inputs";
    public const string OutputSchema = "analysis.outputSchema";

    /// <summary>Per-input layer-id key for the input at <paramref name="index"/>.</summary>
    public static string InputLayerId(int index) => $"analysis.input[{index}].layerId";

    /// <summary>Per-parameter name key for the parameter at <paramref name="index"/>.</summary>
    public static string ParameterName(int index) => $"analysis.parameter[{index}].name";

    /// <summary>Per-output-field name key for the output field at <paramref name="index"/>.</summary>
    public static string OutputFieldName(int index) => $"analysis.outputField[{index}].name";
}

/// <summary>
/// Pure client-side validator for the Studio analysis builder, mirroring the other Studio validators: it
/// examines the console-owned <see cref="StudioAnalysisPlanEditor"/> and emits field-addressable
/// <see cref="ConsoleFieldError"/> findings keyed by <see cref="StudioAnalysisFieldKeys"/> so the editor can
/// surface them inline next to the offending input. It complements — never replaces — server validation; it
/// covers the rules expressible against console-owned state:
/// <list type="bullet">
///   <item>required: a recognised Method, and each input's LayerId &gt;= 0;</item>
///   <item>each authored parameter must declare a name (param presence);</item>
///   <item>output field names must be present and unique.</item>
/// </list>
/// </summary>
public sealed class StudioAnalysisValidator : IFieldValidator<StudioAnalysisPlanEditor>
{
    /// <summary>Shared singleton; the validator holds no state.</summary>
    public static StudioAnalysisValidator Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<ConsoleFieldError> Evaluate(StudioAnalysisPlanEditor state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var errors = new List<ConsoleFieldError>();

        if (string.IsNullOrWhiteSpace(state.Method))
        {
            errors.Add(Blocker(StudioAnalysisFieldKeys.Method, "analysis.method.required", "Choose an analysis method."));
        }
        else if (!StudioAnalysisMethods.All.Contains(state.Method.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(Error(
                StudioAnalysisFieldKeys.Method,
                "analysis.method.invalid",
                $"Method '{state.Method}' is not a recognised analysis method ({string.Join(", ", StudioAnalysisMethods.All)})."));
        }

        if (state.Inputs.Count == 0)
        {
            errors.Add(Blocker(StudioAnalysisFieldKeys.Inputs, "analysis.inputs.required", "Bind at least one input service/layer."));
        }

        for (var index = 0; index < state.Inputs.Count; index++)
        {
            if (state.Inputs[index].LayerId < 0)
            {
                errors.Add(Error(
                    StudioAnalysisFieldKeys.InputLayerId(index),
                    "analysis.input.layerId.range",
                    "Layer id must be zero or greater."));
            }
        }

        for (var index = 0; index < state.Parameters.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(state.Parameters[index].Name))
            {
                errors.Add(Error(
                    StudioAnalysisFieldKeys.ParameterName(index),
                    "analysis.parameter.name.required",
                    "Give this parameter a name."));
            }
        }

        EvaluateOutputFieldUniqueness(state, errors);

        return errors;
    }

    private static void EvaluateOutputFieldUniqueness(StudioAnalysisPlanEditor state, List<ConsoleFieldError> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < state.OutputSchema.Count; index++)
        {
            var name = state.OutputSchema[index].Name?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                errors.Add(Blocker(
                    StudioAnalysisFieldKeys.OutputFieldName(index),
                    "analysis.outputField.name.required",
                    "Give this output field a name."));
                continue;
            }

            if (!seen.Add(name))
            {
                errors.Add(Error(
                    StudioAnalysisFieldKeys.OutputFieldName(index),
                    "analysis.outputField.name.duplicate",
                    $"Output field name '{name}' is already used. Output field names must be unique."));
            }
        }
    }

    private static ConsoleFieldError Blocker(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Blocker, message);

    private static ConsoleFieldError Error(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Error, message);
}

/// <summary>
/// Binds the server-returned analysis-content field-addressable validation errors (the RFC-7807
/// ProblemDetails <c>errors[]</c> of <c>FieldValidationError</c> the analysis-content endpoints now return,
/// honua-server Wave 4) onto the analysis editor's <see cref="ValidationState"/> server channel, keyed by
/// the same <see cref="StudioAnalysisFieldKeys"/> the client validator uses. Each error's path
/// (e.g. <c>/inputs/0/layerId</c>, <c>/outputSchema/2/name</c>, <c>/method</c>) is resolved to the matching
/// console field key; an unresolvable path falls back to the raw locator / form-level key so it still
/// surfaces.
/// </summary>
public static class StudioAnalysisServerErrorBinder
{
    /// <summary>Maps the generic <see cref="ConsoleFieldValidationError"/>s onto console field keys.</summary>
    public static IReadOnlyList<ConsoleFieldError> Map(IEnumerable<ConsoleFieldValidationError>? errors)
    {
        if (errors is null)
        {
            return Array.Empty<ConsoleFieldError>();
        }

        var mapper = new ServerFieldErrorMapper((locator, _) => StudioAnalysisPointerResolver.Resolve(locator));
        return mapper.Map(errors);
    }

    /// <summary>
    /// Maps the analysis-content client's parsed <see cref="Contracts.HonuaFieldValidationError"/>s (the wire
    /// shape carried on a 400 rejection) onto console field keys, by projecting each onto the generic
    /// <see cref="ConsoleFieldValidationError"/> the mapper consumes.
    /// </summary>
    public static IReadOnlyList<ConsoleFieldError> Map(IEnumerable<Contracts.HonuaFieldValidationError>? errors)
    {
        if (errors is null)
        {
            return Array.Empty<ConsoleFieldError>();
        }

        return Map(errors.Select(error => new ConsoleFieldValidationError
        {
            Code = error.Code,
            Severity = error.Severity,
            Path = error.Path,
            FieldId = error.FieldId,
            Message = error.Message,
        }));
    }
}

/// <summary>
/// Resolves an analysis-content JSON Pointer / path (the <c>path</c> on a server
/// <c>FieldValidationError</c>) to the console-owned <see cref="StudioAnalysisFieldKeys"/> for the offending
/// input. The server addresses the analysis content body, so paths look like <c>/inputs/0/layerId</c>,
/// <c>/parameters/1/name</c>, <c>/outputSchema/2/name</c>, plus the scalar <c>/method</c>. A leading
/// <c>body</c> token is tolerated. Returns <see langword="null"/> for an unrecognised path so the mapper
/// falls back to the raw locator.
/// </summary>
public static class StudioAnalysisPointerResolver
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

        var head = segments[index].ToLowerInvariant();

        if (head == "inputs")
        {
            if (index + 1 < segments.Count && int.TryParse(segments[index + 1], out var inputIndex) && inputIndex >= 0)
            {
                return StudioAnalysisFieldKeys.InputLayerId(inputIndex);
            }

            return StudioAnalysisFieldKeys.Inputs;
        }

        if (head == "parameters")
        {
            if (index + 1 < segments.Count && int.TryParse(segments[index + 1], out var paramIndex) && paramIndex >= 0)
            {
                return StudioAnalysisFieldKeys.ParameterName(paramIndex);
            }

            return null;
        }

        if (head is "outputschema" or "outputs" or "outputfields")
        {
            if (index + 1 < segments.Count && int.TryParse(segments[index + 1], out var fieldIndex) && fieldIndex >= 0)
            {
                return StudioAnalysisFieldKeys.OutputFieldName(fieldIndex);
            }

            return StudioAnalysisFieldKeys.OutputSchema;
        }

        return head switch
        {
            "method" => StudioAnalysisFieldKeys.Method,
            _ => null,
        };
    }
}
