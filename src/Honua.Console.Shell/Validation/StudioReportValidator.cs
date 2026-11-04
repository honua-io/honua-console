using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

public static class StudioReportFieldKeys
{
    public const string Title = "report.title";
    public const string RouteSlug = "report.routeSlug";
    public const string Visibility = "report.visibility";
    public const string Panels = "report.panels";
    public const string Bindings = "report.bindings";

    public static string BindingAlias(int index) => $"report.binding[{index}].alias";

    public static string BindingContentRef(int index) => $"report.binding[{index}].contentRef";

    public static string PanelBindingAlias(int index) => $"report.panel[{index}].bindingAlias";

    public static string PanelVegaLiteSpec(int index) => $"report.panel[{index}].vegaLiteSpec";
}

public sealed class StudioReportValidator : IFieldValidator<StudioReportEditorState>
{
    public static StudioReportValidator Instance { get; } = new();

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

public static class StudioReportServerErrorBinder
{
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

public static class StudioReportPointerResolver
{
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
