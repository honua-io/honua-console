using Honua.Sdk.Studio.Packages;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Validation;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Docker-free coverage for <see cref="ServerFieldErrorMapper"/>, proving each of the four server
/// validation shapes (Studio diagnostics, Form issues, Workflow failures, RFC-7807 ProblemDetails
/// errors[]) maps onto the single <see cref="ConsoleFieldError"/> vocabulary, and that the
/// FieldKey-resolver hook is honoured.
/// </summary>
public sealed class ServerFieldErrorMapperTests
{
    [Fact]
    public void MapsStudioDiagnostics()
    {
        var mapper = new ServerFieldErrorMapper();
        var result = mapper.Map(new[]
        {
            new StudioValidationDiagnostic
            {
                Code = "studio.map.initial-view.bbox.order",
                Severity = StudioPackageDiagnosticSeverity.Blocker,
                Path = "/map/initialView/bbox",
                Message = "minX must be <= maxX",
            },
            new StudioValidationDiagnostic
            {
                Code = "studio.note",
                Severity = StudioPackageDiagnosticSeverity.Info,
                Path = "/map/title",
                Message = "Consider a clearer title",
            },
        });

        Assert.Equal(2, result.Count);
        var bbox = result[0];
        Assert.Equal("/map/initialView/bbox", bbox.FieldKey);
        Assert.Equal("/map/initialView/bbox", bbox.Path);
        Assert.Equal(ConsoleValidationSeverity.Blocker, bbox.Severity);
        Assert.Equal(ConsoleValidationSeverity.Info, result[1].Severity);
    }

    [Fact]
    public void MapsFormIssues_PrefersFieldIdOverPath()
    {
        var mapper = new ServerFieldErrorMapper();
        var result = mapper.Map(new[]
        {
            new HonuaFormValidationIssue
            {
                Code = "fieldIdDuplicate",
                Severity = "error",
                FieldId = "asset_id",
                Path = "/fields/2/id",
                Message = "Duplicate field id",
            },
            new HonuaFormValidationIssue
            {
                Code = "rangeOrder",
                Severity = "warning",
                FieldId = null,
                Path = "/fields/3/range",
                Message = "min should be <= max",
            },
        });

        Assert.Equal("asset_id", result[0].FieldKey); // fieldId wins
        Assert.Equal(ConsoleValidationSeverity.Error, result[0].Severity);
        Assert.Equal("/fields/3/range", result[1].FieldKey); // falls back to path
        Assert.Equal(ConsoleValidationSeverity.Warning, result[1].Severity);
    }

    [Fact]
    public void MapsWorkflowFailures_AlwaysBlocker()
    {
        var mapper = new ServerFieldErrorMapper();
        var result = mapper.Map(new[]
        {
            new WorkflowPackageValidationFailure
            {
                Code = "graph.cycle",
                Message = "Graph has a cycle",
                FieldPath = "nodes[2].edges",
            },
        });

        Assert.Single(result);
        Assert.Equal("nodes[2].edges", result[0].FieldKey);
        Assert.Equal(ConsoleValidationSeverity.Blocker, result[0].Severity);
        Assert.Equal("graph.cycle", result[0].Code);
    }

    [Fact]
    public void MapsProblemDetailsErrors()
    {
        var mapper = new ServerFieldErrorMapper();
        var result = mapper.Map(new[]
        {
            new ConsoleFieldValidationError
            {
                Code = "publication.title.required",
                Severity = "error",
                Path = "/title",
                Message = "Title is required",
            },
            new ConsoleFieldValidationError
            {
                Code = "publication.slug.format",
                Severity = null, // unknown/absent -> error default
                FieldId = "routeSlug",
                Message = "Slug must be a valid identifier",
            },
        });

        Assert.Equal("/title", result[0].FieldKey);
        Assert.Equal(ConsoleValidationSeverity.Error, result[0].Severity);
        Assert.Equal("routeSlug", result[1].FieldKey); // fieldId preferred
        Assert.Equal(ConsoleValidationSeverity.Error, result[1].Severity);
    }

    [Fact]
    public void NoLocator_FallsBackToFormLevelKey()
    {
        var mapper = new ServerFieldErrorMapper();
        var result = mapper.Map(new[]
        {
            new ConsoleFieldValidationError { Code = "x", Message = "form-level error" },
        });

        Assert.Equal(ServerFieldErrorMapper.FormLevelFieldKey, result[0].FieldKey);
    }

    [Fact]
    public void ResolverHook_RemapsLocatorToFieldKey()
    {
        // Per-editor waves register a pointer -> console FieldKey map; prove the hook is applied and
        // the raw locator is preserved on Path.
        var mapper = new ServerFieldErrorMapper((locator, _) =>
            locator == "/map/initialView/bbox" ? "map.initialExtent" : null);

        var result = mapper.Map(new[]
        {
            new StudioValidationDiagnostic
            {
                Code = "bbox.order",
                Severity = StudioPackageDiagnosticSeverity.Error,
                Path = "/map/initialView/bbox",
                Message = "bad",
            },
        });

        Assert.Equal("map.initialExtent", result[0].FieldKey);
        Assert.Equal("/map/initialView/bbox", result[0].Path);
    }

    [Fact]
    public void NullCollections_ReturnEmpty()
    {
        var mapper = new ServerFieldErrorMapper();
        Assert.Empty(mapper.Map((IEnumerable<StudioValidationDiagnostic>?)null));
        Assert.Empty(mapper.Map((IEnumerable<HonuaFormValidationIssue>?)null));
        Assert.Empty(mapper.Map((IEnumerable<WorkflowPackageValidationFailure>?)null));
        Assert.Empty(mapper.Map((IEnumerable<ConsoleFieldValidationError>?)null));
    }

    [Theory]
    [InlineData("info", ConsoleValidationSeverity.Info)]
    [InlineData("warning", ConsoleValidationSeverity.Warning)]
    [InlineData("warn", ConsoleValidationSeverity.Warning)]
    [InlineData("error", ConsoleValidationSeverity.Error)]
    [InlineData("blocker", ConsoleValidationSeverity.Blocker)]
    [InlineData("nonsense", ConsoleValidationSeverity.Error)]
    [InlineData(null, ConsoleValidationSeverity.Error)]
    public void ParseSeverity_MapsStrings(string? input, ConsoleValidationSeverity expected) =>
        Assert.Equal(expected, ServerFieldErrorMapper.ParseSeverity(input));
}
