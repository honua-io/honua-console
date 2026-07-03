using Honua.Sdk.Studio.Packages;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// Stable console-owned field keys for the Studio dashboard builder. Shared by the client validator
/// (<see cref="StudioDashboardValidator"/>), the inline render surfaces, and the server-diagnostic resolver
/// (<see cref="StudioDashboardServerErrorBinder"/>) so a client finding and a server finding for the same
/// input land on the same key. Per-binding / per-panel keys are derived from the item's <em>position</em>
/// (which the server's <c>/bindings/{n}/…</c> / <c>/panels/{n}/…</c> JSON Pointers address).
/// </summary>
public static class StudioDashboardFieldKeys
{
    public const string Title = "dashboard.title";
    public const string Visibility = "dashboard.visibility";
    public const string Panels = "dashboard.panels";
    public const string Bindings = "dashboard.bindings";

    /// <summary>Per-binding alias key for the binding at <paramref name="index"/>.</summary>
    public static string BindingAlias(int index) => $"dashboard.binding[{index}].alias";

    /// <summary>Per-binding content-ref key for the binding at <paramref name="index"/>.</summary>
    public static string BindingContentRef(int index) => $"dashboard.binding[{index}].contentRef";

    /// <summary>Per-panel binding-alias key for the panel at <paramref name="index"/>.</summary>
    public static string PanelBindingAlias(int index) => $"dashboard.panel[{index}].bindingAlias";

    /// <summary>Per-panel Vega-Lite spec key for the panel at <paramref name="index"/>.</summary>
    public static string PanelVegaLiteSpec(int index) => $"dashboard.panel[{index}].vegaLiteSpec";
}

/// <summary>
/// Pure client-side cross-field / referential / format validator for the Studio dashboard builder,
/// mirroring <see cref="StudioReportValidator"/> (both author the same bindings/panels graph). It examines
/// the console-owned <see cref="StudioDashboardEditorState"/> and emits field-addressable
/// <see cref="ConsoleFieldError"/> findings keyed by <see cref="StudioDashboardFieldKeys"/>: required Title,
/// at least one panel, each binding's alias + content ref, unique binding aliases, each panel's binding
/// alias referencing a declared binding (referential), each chart panel's Vega-Lite spec parsing as JSON
/// and declaring a vega-lite <c>$schema</c> (via <see cref="StudioDashboardChartSpec.DeclaresVegaLiteSchema"/>),
/// and Visibility enum membership. The dashboard publishes through the Studio package draft lifecycle, which
/// owns the server route slug, so this client validator does not enforce a RouteSlug format.
/// </summary>
public sealed class StudioDashboardValidator : IFieldValidator<StudioDashboardEditorState>
{
    /// <summary>Shared singleton; the validator holds no state.</summary>
    public static StudioDashboardValidator Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<ConsoleFieldError> Evaluate(StudioDashboardEditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var errors = new List<ConsoleFieldError>();

        if (string.IsNullOrWhiteSpace(state.Title))
        {
            errors.Add(Blocker(StudioDashboardFieldKeys.Title, "dashboard.title.required", "Give the dashboard a title."));
        }

        StudioPanelBindingRules.EvaluateVisibility(
            VisibilityFor(state),
            HonuaContentPublicationVisibilitiesAll,
            StudioDashboardFieldKeys.Visibility,
            "dashboard",
            errors);

        if (state.Panels.Count == 0)
        {
            errors.Add(Blocker(StudioDashboardFieldKeys.Panels, "dashboard.panels.required", "Add at least one panel."));
        }

        var aliases = StudioPanelBindingRules.EvaluateBindings(
            state.Bindings.Select(b => new StudioPanelBindingRules.BindingView(b.Alias, b.ContentRef)),
            StudioDashboardFieldKeys.BindingAlias,
            StudioDashboardFieldKeys.BindingContentRef,
            errors);

        for (var index = 0; index < state.Panels.Count; index++)
        {
            var panel = state.Panels[index];

            StudioPanelBindingRules.EvaluatePanelBindingAlias(
                panel.BindingAlias,
                aliases,
                StudioDashboardFieldKeys.PanelBindingAlias(index),
                errors);

            if (panel.IsChart && !StudioDashboardChartSpec.DeclaresVegaLiteSchema(panel.VegaLiteSpec))
            {
                errors.Add(Error(
                    StudioDashboardFieldKeys.PanelVegaLiteSpec(index),
                    "dashboard.panel.vegaLite.invalid",
                    "Chart panels must declare a Vega-Lite spec: valid JSON with a vega-lite \"$schema\"."));
            }
        }

        return errors;
    }

    // The dashboard editor does not carry a free visibility field today; this hook keeps the rule in place
    // for when the editor surfaces one. Returns null so the (optional) visibility check no-ops until then.
    private static string? VisibilityFor(StudioDashboardEditorState state) => null;

    // Dashboards publish to the same content publication registry as reports, so they share the visibility
    // vocabulary (private/organization/team/public).
    private static readonly IReadOnlyList<string> HonuaContentPublicationVisibilitiesAll =
    [
        HonuaContentPublicationVisibilities.Private,
        HonuaContentPublicationVisibilities.Organization,
        HonuaContentPublicationVisibilities.Team,
        HonuaContentPublicationVisibilities.Public,
    ];

    private static ConsoleFieldError Blocker(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Blocker, message);

    private static ConsoleFieldError Error(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Error, message);
}

/// <summary>
/// Binds the server-returned Studio package validation diagnostics (<c>{code,severity,path,message}</c>,
/// carried back from the dashboard package-draft validate path) onto the dashboard editor's
/// <see cref="ValidationState"/> server channel, keyed by the same <see cref="StudioDashboardFieldKeys"/>
/// the client validator uses. Each diagnostic's JSON Pointer (e.g. <c>/body/bindings/0</c>,
/// <c>/body/panels/2/bindingAlias</c>) is resolved to the matching console field key; an unresolvable
/// pointer falls back to the raw locator / form-level key so it still surfaces.
/// </summary>
public static class StudioDashboardServerErrorBinder
{
    /// <summary>Maps <paramref name="diagnostics"/> onto console field keys via the dashboard JSON-Pointer resolver.</summary>
    public static IReadOnlyList<ConsoleFieldError> Map(
        IEnumerable<StudioValidationDiagnostic>? diagnostics)
    {
        if (diagnostics is null)
        {
            return Array.Empty<ConsoleFieldError>();
        }

        var mapper = new ServerFieldErrorMapper((locator, _) => StudioDashboardPointerResolver.Resolve(locator));
        return mapper.Map(diagnostics);
    }
}

/// <summary>
/// Resolves a dashboard package-envelope JSON Pointer to the console-owned
/// <see cref="StudioDashboardFieldKeys"/>. The Studio package validator addresses the envelope body, so
/// pointers are rooted at <c>/body</c> (e.g. <c>/body/bindings/0</c>, <c>/body/panels/2/bindingAlias</c>,
/// <c>/body/panels/2/chartSpec</c>); the shared <see cref="StudioPanelBindingRules.ResolvePointer"/> drops
/// the leading <c>body</c> token. Returns <see langword="null"/> for an unrecognised pointer.
/// </summary>
public static class StudioDashboardPointerResolver
{
    /// <summary>Resolves <paramref name="pointer"/> to a console field key, or <see langword="null"/>.</summary>
    public static string? Resolve(string? pointer) =>
        StudioPanelBindingRules.ResolvePointer(
            pointer,
            bindingAlias: StudioDashboardFieldKeys.BindingAlias,
            bindingContentRef: StudioDashboardFieldKeys.BindingContentRef,
            bindings: StudioDashboardFieldKeys.Bindings,
            panelBindingAlias: StudioDashboardFieldKeys.PanelBindingAlias,
            panelChartSpec: StudioDashboardFieldKeys.PanelVegaLiteSpec,
            panels: StudioDashboardFieldKeys.Panels,
            visibility: StudioDashboardFieldKeys.Visibility,
            routeSlug: StudioDashboardFieldKeys.Title,
            title: StudioDashboardFieldKeys.Title);
}
