using Honua.Sdk.Studio.Packages;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Validation;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Docker-free coverage for the Wave-3 Studio report + dashboard client validators and their server-error
/// binders: required presence, unique binding aliases, panel→binding referential integrity, Vega-Lite spec
/// (JSON + vega-lite <c>$schema</c>), RouteSlug format / Visibility enum, and JSON-Pointer → field-key
/// resolution for server findings.
/// </summary>
public sealed class StudioReportDashboardValidatorTests
{
    private const string ValidVegaSpec =
        """{ "$schema": "https://vega.github.io/schema/vega-lite/v5.json", "mark": "bar" }""";

    private static StudioReportEditorState ValidReport()
    {
        var state = new StudioReportEditorState { Title = "Quarterly", RouteSlug = "quarterly-report" };
        state.Bindings.Add(new StudioReportBindingEditor { Alias = "incidents", ContentRef = "content:incidents" });
        state.Panels.Add(new StudioReportPanelEditor
        {
            Kind = StudioReportPanelKinds.Chart,
            BindingAlias = "incidents",
            VegaLiteSpec = ValidVegaSpec,
        });
        return state;
    }

    [Fact]
    public void Report_ValidState_HasNoFindings()
    {
        var errors = StudioReportValidator.Instance.Evaluate(ValidReport());
        Assert.Empty(errors);
    }

    [Fact]
    public void Report_MissingTitleAndNoPanels_AreBlockers()
    {
        var errors = StudioReportValidator.Instance.Evaluate(new StudioReportEditorState());

        Assert.Contains(errors, e => e.FieldKey == StudioReportFieldKeys.Title && e.Severity == ConsoleValidationSeverity.Blocker);
        Assert.Contains(errors, e => e.FieldKey == StudioReportFieldKeys.Panels && e.Severity == ConsoleValidationSeverity.Blocker);
    }

    [Fact]
    public void Report_DuplicateBindingAlias_IsFlagged()
    {
        var state = ValidReport();
        state.Bindings.Add(new StudioReportBindingEditor { Alias = "incidents", ContentRef = "content:other" });

        var errors = StudioReportValidator.Instance.Evaluate(state);
        Assert.Contains(errors, e => e.FieldKey == StudioReportFieldKeys.BindingAlias(1) && e.Code == "binding.alias.duplicate");
    }

    [Fact]
    public void Report_PanelAliasNotDeclared_IsReferentialError()
    {
        var state = ValidReport();
        state.Panels[0].BindingAlias = "missing";

        var errors = StudioReportValidator.Instance.Evaluate(state);
        Assert.Contains(errors, e => e.FieldKey == StudioReportFieldKeys.PanelBindingAlias(0) && e.Code == "panel.bindingAlias.unresolved");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ \"mark\": \"bar\" }")] // valid JSON but no vega-lite $schema
    public void Report_ChartPanelWithoutVegaLiteSchema_IsFlagged(string spec)
    {
        var state = ValidReport();
        state.Panels[0].VegaLiteSpec = spec;

        var errors = StudioReportValidator.Instance.Evaluate(state);
        Assert.Contains(errors, e => e.FieldKey == StudioReportFieldKeys.PanelVegaLiteSpec(0) && e.Code == "report.panel.vegaLite.invalid");
    }

    [Fact]
    public void Report_NonChartPanel_DoesNotRequireVegaSpec()
    {
        var state = ValidReport();
        state.Panels[0].Kind = StudioReportPanelKinds.Table;
        state.Panels[0].VegaLiteSpec = string.Empty;

        var errors = StudioReportValidator.Instance.Evaluate(state);
        Assert.DoesNotContain(errors, e => e.FieldKey == StudioReportFieldKeys.PanelVegaLiteSpec(0));
    }

    [Theory]
    [InlineData("Bad Slug")]
    [InlineData("UPPER")]
    [InlineData("trailing-")]
    public void Report_InvalidRouteSlug_IsFlagged(string slug)
    {
        var state = ValidReport();
        state.RouteSlug = slug;

        var errors = StudioReportValidator.Instance.Evaluate(state);
        Assert.Contains(errors, e => e.FieldKey == StudioReportFieldKeys.RouteSlug && e.Code == "report.routeSlug.format");
    }

    [Fact]
    public void Report_InvalidVisibility_IsFlagged()
    {
        var state = ValidReport();
        state.Visibility = "galaxy";

        var errors = StudioReportValidator.Instance.Evaluate(state);
        Assert.Contains(errors, e => e.FieldKey == StudioReportFieldKeys.Visibility && e.Code == "report.visibility.invalid");
    }

    [Fact]
    public void ReportServerErrorBinder_ResolvesPointersToFieldKeys()
    {
        var errors = new[]
        {
            new HonuaFieldValidationError { Code = "publication.panel.bindingAlias.unresolved", Severity = "error", Path = "/panels/2/bindingAlias", Message = "x" },
            new HonuaFieldValidationError { Code = "publication.binding.alias.required", Severity = "blocker", Path = "/bindings/0/alias", Message = "y" },
            new HonuaFieldValidationError { Code = "publication.panel.chartSpec.vegaLite", Severity = "error", Path = "/panels/2/chartSpec", Message = "z" },
        };

        var mapped = StudioReportServerErrorBinder.Map(errors);

        Assert.Contains(mapped, e => e.FieldKey == StudioReportFieldKeys.PanelBindingAlias(2));
        Assert.Contains(mapped, e => e.FieldKey == StudioReportFieldKeys.BindingAlias(0));
        Assert.Contains(mapped, e => e.FieldKey == StudioReportFieldKeys.PanelVegaLiteSpec(2));
    }

    private static StudioDashboardEditorState ValidDashboard()
    {
        var state = new StudioDashboardEditorState { Title = "Ops" };
        state.Bindings.Add(new StudioDashboardBindingEditor { Alias = "requests", ContentRef = "content:requests" });
        state.Panels.Add(new StudioDashboardPanelEditor
        {
            Kind = StudioDashboardPanelKinds.Chart,
            BindingAlias = "requests",
            VegaLiteSpec = ValidVegaSpec,
        });
        return state;
    }

    [Fact]
    public void Dashboard_ValidState_HasNoFindings()
    {
        var errors = StudioDashboardValidator.Instance.Evaluate(ValidDashboard());
        Assert.Empty(errors);
    }

    [Fact]
    public void Dashboard_DuplicateAlias_UnresolvedPanel_AndBadVega_AreFlagged()
    {
        var state = ValidDashboard();
        state.Bindings.Add(new StudioDashboardBindingEditor { Alias = "requests", ContentRef = "content:dup" });
        state.Panels.Add(new StudioDashboardPanelEditor
        {
            Kind = StudioDashboardPanelKinds.Chart,
            BindingAlias = "missing",
            VegaLiteSpec = "{}",
        });

        var errors = StudioDashboardValidator.Instance.Evaluate(state);
        Assert.Contains(errors, e => e.FieldKey == StudioDashboardFieldKeys.BindingAlias(1) && e.Code == "binding.alias.duplicate");
        Assert.Contains(errors, e => e.FieldKey == StudioDashboardFieldKeys.PanelBindingAlias(1) && e.Code == "panel.bindingAlias.unresolved");
        Assert.Contains(errors, e => e.FieldKey == StudioDashboardFieldKeys.PanelVegaLiteSpec(1) && e.Code == "dashboard.panel.vegaLite.invalid");
    }

    [Fact]
    public void DashboardServerErrorBinder_ResolvesBodyRootedPointers()
    {
        var diagnostics = new[]
        {
            new StudioValidationDiagnostic { Code = "studio.binding.key.duplicate", Severity = StudioPackageDiagnosticSeverity.Error, Path = "/body/bindings/1", Message = "dup" },
            new StudioValidationDiagnostic { Code = "studio.panel.alias", Severity = StudioPackageDiagnosticSeverity.Error, Path = "/body/panels/0/bindingAlias", Message = "x" },
        };

        var mapped = StudioDashboardServerErrorBinder.Map(diagnostics);

        Assert.Contains(mapped, e => e.FieldKey == StudioDashboardFieldKeys.BindingAlias(1));
        Assert.Contains(mapped, e => e.FieldKey == StudioDashboardFieldKeys.PanelBindingAlias(0));
    }
}
