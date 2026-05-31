using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// Stable console-owned field keys for the Studio report builder. The client validator
/// (<see cref="StudioReportValidator"/>), the inline render surfaces, and the server-error resolver
/// (<see cref="StudioReportServerErrorBinder"/>) all share these so a client finding and a server finding
/// for the same input land on the same key. Form-level keys are constants; per-binding and per-panel keys
/// are derived from the item's <em>position</em> (which is exactly what the server's
/// <c>/bindings/{index}/…</c> and <c>/panels/{index}/…</c> JSON Pointers address) so every authored row is
/// independently addressable.
/// </summary>
public static class StudioReportFieldKeys
{
    public const string Title = "report.title";
    public const string RouteSlug = "report.routeSlug";
    public const string Visibility = "report.visibility";
    public const string Panels = "report.panels";
    public const string Bindings = "report.bindings";

    /// <summary>Per-binding alias key for the binding at <paramref name="index"/>.</summary>
    public static string BindingAlias(int index) => $"report.binding[{index}].alias";

    /// <summary>Per-binding content-ref key for the binding at <paramref name="index"/>.</summary>
    public static string BindingContentRef(int index) => $"report.binding[{index}].contentRef";

    /// <summary>Per-panel binding-alias key for the panel at <paramref name="index"/>.</summary>
    public static string PanelBindingAlias(int index) => $"report.panel[{index}].bindingAlias";

    /// <summary>Per-panel Vega-Lite spec key for the panel at <paramref name="index"/>.</summary>
    public static string PanelVegaLiteSpec(int index) => $"report.panel[{index}].vegaLiteSpec";
}

/// <summary>
/// Pure client-side cross-field / referential / format validator for the Studio report builder, mirroring
/// the <see cref="StudioMapValidator"/> / <see cref="StudioFormValidator"/> pattern: it examines the
/// console-owned <see cref="StudioReportEditorState"/> and emits field-addressable
/// <see cref="ConsoleFieldError"/> findings keyed by <see cref="StudioReportFieldKeys"/> so the editor can
/// surface them inline next to the offending input. It complements — never replaces — server validation;
/// it covers the rules expressible against console-owned state:
/// <list type="bullet">
///   <item>required Title, at least one panel, and each binding's alias + content ref;</item>
///   <item>unique binding aliases;</item>
///   <item>each panel's binding alias referencing a declared binding alias (referential);</item>
///   <item>each chart panel's Vega-Lite spec parsing as JSON and declaring a vega-lite <c>$schema</c>
///   (via the existing <see cref="StudioReportChartSpec.DeclaresVegaLiteSchema"/> helper);</item>
///   <item>RouteSlug format and Visibility enum membership.</item>
/// </list>
/// </summary>
public sealed class StudioReportValidator : IFieldValidator<StudioReportEditorState>
{
    /// <summary>Shared singleton; the validator holds no state.</summary>
    public static StudioReportValidator Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<ConsoleFieldError> Evaluate(StudioReportEditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var errors = new List<ConsoleFieldError>();

        if (string.IsNullOrWhiteSpace(state.Title))
        {
            errors.Add(Blocker(StudioReportFieldKeys.Title, "report.title.required", "Give the report a title."));
        }

        StudioPanelBindingRules.EvaluateRouteSlug(
            state.RouteSlug,
            StudioReportFieldKeys.RouteSlug,
            "report",
            errors);

        StudioPanelBindingRules.EvaluateVisibility(
            state.Visibility,
            StudioReportVisibilities.All,
            StudioReportFieldKeys.Visibility,
            "report",
            errors);

        if (state.Panels.Count == 0)
        {
            errors.Add(Blocker(StudioReportFieldKeys.Panels, "report.panels.required", "Add at least one panel."));
        }

        var aliases = StudioPanelBindingRules.EvaluateBindings(
            state.Bindings.Select(b => new StudioPanelBindingRules.BindingView(b.Alias, b.ContentRef)),
            StudioReportFieldKeys.BindingAlias,
            StudioReportFieldKeys.BindingContentRef,
            errors);

        for (var index = 0; index < state.Panels.Count; index++)
        {
            var panel = state.Panels[index];

            StudioPanelBindingRules.EvaluatePanelBindingAlias(
                panel.BindingAlias,
                aliases,
                StudioReportFieldKeys.PanelBindingAlias(index),
                errors);

            if (panel.IsChart && !StudioReportChartSpec.DeclaresVegaLiteSchema(panel.VegaLiteSpec))
            {
                errors.Add(Error(
                    StudioReportFieldKeys.PanelVegaLiteSpec(index),
                    "report.panel.vegaLite.invalid",
                    "Chart panels must declare a Vega-Lite spec: valid JSON with a vega-lite \"$schema\"."));
            }
        }

        return errors;
    }

    private static ConsoleFieldError Blocker(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Blocker, message);

    private static ConsoleFieldError Error(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Error, message);
}

/// <summary>
/// Binds the server-returned content-publication validation errors (the RFC-7807 ProblemDetails
/// <c>errors[]</c> of <c>FieldValidationError</c> the report publish/republish endpoint now returns,
/// honua-server Wave 3) onto the report editor's <see cref="ValidationState"/> server channel, keyed by the
/// same <see cref="StudioReportFieldKeys"/> the client validator uses. Each error's JSON Pointer
/// (e.g. <c>/panels/2/bindingAlias</c>, <c>/bindings/0/alias</c>) is resolved to the matching console field
/// key; an unresolvable pointer falls back to the raw locator / form-level key so it still surfaces.
/// </summary>
public static class StudioReportServerErrorBinder
{
    /// <summary>Maps <paramref name="errors"/> onto console field keys via the report JSON-Pointer resolver.</summary>
    public static IReadOnlyList<ConsoleFieldError> Map(
        IEnumerable<ConsoleFieldValidationError>? errors)
    {
        if (errors is null)
        {
            return Array.Empty<ConsoleFieldError>();
        }

        var mapper = new ServerFieldErrorMapper((locator, _) => StudioReportPointerResolver.Resolve(locator));
        return mapper.Map(errors);
    }

    /// <summary>
    /// Maps the content-publication client's parsed <see cref="HonuaFieldValidationError"/>s (the wire
    /// shape carried on a 400 publish/republish rejection) onto console field keys, by projecting each onto
    /// the generic <see cref="ConsoleFieldValidationError"/> the mapper consumes.
    /// </summary>
    public static IReadOnlyList<ConsoleFieldError> Map(
        IEnumerable<HonuaFieldValidationError>? errors)
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
/// Resolves a content-publication report-document JSON Pointer (the <c>path</c> on a server
/// <c>FieldValidationError</c>) to the console-owned <see cref="StudioReportFieldKeys"/> for the offending
/// input. The server addresses the publish request body, so pointers look like <c>/bindings/0/alias</c>,
/// <c>/bindings/0/contentRef</c>, <c>/panels/2/bindingAlias</c>, <c>/panels/2/chartSpec</c>, plus the
/// scalar request fields <c>/policy/visibility</c> and the route slug. Returns <see langword="null"/> for
/// an unrecognised pointer so the mapper falls back to the raw locator.
/// </summary>
public static class StudioReportPointerResolver
{
    /// <summary>Resolves <paramref name="pointer"/> to a console field key, or <see langword="null"/>.</summary>
    public static string? Resolve(string? pointer) =>
        StudioPanelBindingRules.ResolvePointer(
            pointer,
            bindingAlias: StudioReportFieldKeys.BindingAlias,
            bindingContentRef: StudioReportFieldKeys.BindingContentRef,
            bindings: StudioReportFieldKeys.Bindings,
            panelBindingAlias: StudioReportFieldKeys.PanelBindingAlias,
            panelChartSpec: StudioReportFieldKeys.PanelVegaLiteSpec,
            panels: StudioReportFieldKeys.Panels,
            visibility: StudioReportFieldKeys.Visibility,
            routeSlug: StudioReportFieldKeys.RouteSlug,
            title: StudioReportFieldKeys.Title);
}
