using System.Net;
using System.Text;
using Honua.Console.Contracts;
using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Native.Core.Tests;

public sealed class SavedQueryEditorServiceTests
{
    private const string CreateResponse = """
        {
          "item": {
            "itemId": "analysis-content-1",
            "kind": "savedQuery",
            "name": "incidents",
            "title": "Incidents",
            "visibility": "organization",
            "currentVersion": 1,
            "currentVersionId": "analysis-content-1:v1",
            "lifecycle": "active",
            "createdAt": "2026-05-25T10:00:00Z",
            "updatedAt": "2026-05-25T10:00:00Z"
          },
          "version": {
            "versionId": "analysis-content-1:v1",
            "itemId": "analysis-content-1",
            "version": 1,
            "kind": "savedQuery",
            "savedQuery": { "layerId": 42, "serviceName": "public-safety", "previewLimit": 25, "outFields": [] },
            "contentHash": "hash-1",
            "createdAt": "2026-05-25T10:00:00Z"
          }
        }
        """;

    private const string ItemWithFilterResponse = """
        {
          "item": {
            "itemId": "analysis-content-1",
            "kind": "savedQuery",
            "name": "incidents",
            "title": "Incidents",
            "visibility": "organization",
            "currentVersion": 2,
            "currentVersionId": "analysis-content-1:v2",
            "lifecycle": "active",
            "createdAt": "2026-05-25T10:00:00Z",
            "updatedAt": "2026-05-25T10:05:00Z"
          },
          "version": {
            "versionId": "analysis-content-1:v2",
            "itemId": "analysis-content-1",
            "version": 2,
            "kind": "savedQuery",
            "savedQuery": {
              "layerId": 42,
              "serviceName": "public-safety",
              "outFields": ["name", "category"],
              "outputSrid": 4326,
              "previewLimit": 50,
              "units": "meters",
              "filterPlan": {
                "combinator": "and",
                "clauses": [
                  { "type": "comparison", "comparison": { "property": "category", "operator": "eq", "value": "inspection" } },
                  { "type": "temporal", "temporal": { "property": "timestamp", "operator": "after", "start": "2026-01-01T00:00:00Z" } }
                ]
              }
            },
            "contentHash": "hash-2",
            "createdAt": "2026-05-25T10:05:00Z"
          }
        }
        """;

    private const string PreviewResponse = """
        {
          "previewArtifactId": "analysis-preview-1",
          "itemId": "analysis-content-1",
          "version": 1,
          "layerId": 42,
          "features": [
            { "id": 1001, "attributes": { "category": "inspection", "name": "Alpha" }, "hasGeometry": true },
            { "id": 1002, "attributes": { "category": "complaint" }, "hasGeometry": false }
          ],
          "totalCount": 17,
          "exceededPreviewLimit": true,
          "binding": {
            "artifactId": "analysis-preview-1",
            "sourceItemId": "analysis-content-1",
            "sourceVersion": 1,
            "sourceVersionId": "analysis-content-1:v1",
            "role": "preview",
            "targetKind": "map",
            "targetSlot": "source"
          }
        }
        """;

    [Fact]
    public void RuntimeShellRegistrationDoesNotUseMockSavedQueryEditor()
    {
        var services = new ServiceCollection();

        services.AddHonuaConsoleShell();

        using var provider = services.BuildServiceProvider();
        var editor = provider.GetRequiredService<ISavedQueryEditorService>();

        Assert.IsType<UnsupportedSavedQueryEditorService>(editor);
        Assert.NotNull(editor.GetBindingStatus());
    }

    [Fact]
    public void ConfiguredShellRegistrationUsesHonuaServerSavedQueryEditor()
    {
        var services = new ServiceCollection();

        services.AddHonuaConsoleShell("https://server.example", "test-api-key");

        using var provider = services.BuildServiceProvider();
        var editor = provider.GetRequiredService<ISavedQueryEditorService>();

        Assert.IsType<HonuaServerSavedQueryEditorService>(editor);
        Assert.Null(editor.GetBindingStatus());
    }

    [Fact]
    public async Task UnsupportedServiceReturnsMissingBindingForEveryOperation()
    {
        var editor = new UnsupportedSavedQueryEditorService();

        var status = editor.GetBindingStatus();
        var create = await editor.CreateAsync(new SavedQueryDefinitionDraft());
        var open = await editor.OpenAsync("analysis-content-1");
        var preview = await editor.PreviewAsync("analysis-content-1", 1, 25);

        Assert.Equal("Missing binding", status!.State);
        Assert.Equal("Honua:Server:BaseUrl", status.Contract);
        Assert.False(create.IsSuccess);
        Assert.Equal("Missing binding", create.BindingState!.State);
        Assert.False(open.IsSuccess);
        Assert.False(preview.IsSuccess);
    }

    [Fact]
    public async Task ServerServiceCreatesSavedQueryFromLiveResponse()
    {
        var service = CreateService((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v1/analysis/content/items", request.RequestUri!.AbsolutePath);
            Assert.True(request.Headers.Contains("X-API-Key"));
            return Json(HttpStatusCode.Created, CreateResponse);
        });

        var draft = new SavedQueryDefinitionDraft { LayerId = 42, ServiceName = "public-safety", Title = "Incidents" };
        var result = await service.CreateAsync(draft);

        Assert.True(result.IsSuccess);
        Assert.Equal("analysis-content-1", result.Handle!.ItemId);
        Assert.Equal(1, result.Handle.CurrentVersion);
        Assert.Equal("analysis-content-1:v1", result.Handle.CurrentVersionId);
        Assert.Equal("analysis-content-1@v1", result.Handle.ReuseRef);
    }

    [Fact]
    public async Task ServerServiceOpenMapsContentAndFilterPlanToDraft()
    {
        var service = CreateService((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/v1/analysis/content/items/analysis-content-1", request.RequestUri!.AbsolutePath);
            return Json(HttpStatusCode.OK, ItemWithFilterResponse);
        });

        var result = await service.OpenAsync("analysis-content-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Definition!.LayerId);
        Assert.Equal("public-safety", result.Definition.ServiceName);
        Assert.Equal("name, category", result.Definition.OutFields);
        Assert.Equal(4326, result.Definition.OutputSrid);
        Assert.Equal(FilterPlanCombinator.And, result.Definition.Combinator);
        Assert.Equal(2, result.Definition.Predicates.Count);
        Assert.Contains(result.Definition.Predicates, predicate =>
            predicate.Kind == SavedQueryPredicateKind.Attribute && predicate.Property == "category" && predicate.Value == "inspection");
        Assert.Contains(result.Definition.Predicates, predicate =>
            predicate.Kind == SavedQueryPredicateKind.Temporal && predicate.Property == "timestamp" && predicate.Operator == "after");
        Assert.Equal(2, result.Handle!.CurrentVersion);
    }

    [Fact]
    public async Task ServerServicePreviewMapsRowsColumnsAndMapSummary()
    {
        var service = CreateService((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                "/api/v1/analysis/content/items/analysis-content-1/versions/1/preview",
                request.RequestUri!.AbsolutePath);
            return Json(HttpStatusCode.OK, PreviewResponse);
        });

        var outcome = await service.PreviewAsync("analysis-content-1", 1, 25);

        Assert.True(outcome.IsSuccess);
        var preview = outcome.Preview!;
        Assert.Equal(2, preview.FeatureCount);
        Assert.Equal(1, preview.GeometryCount);
        Assert.Equal(17, preview.TotalCount);
        Assert.True(preview.ExceededPreviewLimit);
        Assert.Contains("category", preview.Columns);
        Assert.Contains("name", preview.Columns);
        Assert.Equal(2, preview.Rows.Count);
        Assert.Equal("inspection", preview.Rows[0].Cells["category"]);
        Assert.Equal("Alpha", preview.Rows[0].Cells["name"]);
        Assert.Equal(string.Empty, preview.Rows[1].Cells["name"]);
        Assert.Equal("analysis-preview-1", preview.Binding.ArtifactId);
        Assert.Equal("preview", preview.Binding.Role);
    }

    [Fact]
    public async Task ServerServiceMapsForbiddenToMissingPermissionState()
    {
        var service = CreateService((_, _) =>
            Json(HttpStatusCode.Forbidden, """{"title":"Forbidden","detail":"admin permission required","status":403}"""));

        var result = await service.CreateAsync(new SavedQueryDefinitionDraft { LayerId = 1 });

        Assert.False(result.IsSuccess);
        Assert.Equal("Missing permission", result.BindingState!.State);
        Assert.Contains("admin permission required", result.BindingState.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerServiceMapsNotFoundOnReopenToUnsupportedState()
    {
        var service = CreateService((_, _) =>
            Json(HttpStatusCode.NotFound, """{"title":"Not Found","detail":"item missing","status":404}"""));

        var result = await service.OpenAsync("missing-item");

        Assert.False(result.IsSuccess);
        Assert.Equal("Unsupported", result.BindingState!.State);
    }

    [Fact]
    public void MapperRoundTripsDefinitionThroughContent()
    {
        var draft = new SavedQueryDefinitionDraft
        {
            LayerId = 7,
            ServiceName = "planning",
            OutFields = "name, status",
            OutputSrid = 3857,
            Units = "meters",
            PreviewLimit = 100,
            Combinator = FilterPlanCombinator.Or,
            Predicates =
            [
                new SavedQueryPredicateDraft { Kind = SavedQueryPredicateKind.Attribute, Property = "status", Operator = "eq", Value = "open" },
                new SavedQueryPredicateDraft { Kind = SavedQueryPredicateKind.Temporal, Property = "filed", Operator = "during", Start = "2026-01-01", End = "2026-12-31" }
            ]
        };

        var content = SavedQueryContentMapper.ToContent(draft);
        Assert.Equal(7, content.LayerId);
        Assert.Equal(["name", "status"], content.OutFields);
        Assert.NotNull(content.FilterPlan);
        Assert.Equal(FilterPlanCombinator.Or, content.FilterPlan!.Combinator);
        Assert.Equal(2, content.FilterPlan.Clauses.Count);

        var roundTrip = SavedQueryContentMapper.ToDraft(content);
        Assert.Equal(7, roundTrip.LayerId);
        Assert.Equal("planning", roundTrip.ServiceName);
        Assert.Equal("name, status", roundTrip.OutFields);
        Assert.Equal(FilterPlanCombinator.Or, roundTrip.Combinator);
        Assert.Equal(2, roundTrip.Predicates.Count);
    }

    [Fact]
    public void FilterReadoutReflectsSourceAndPredicates()
    {
        var draft = new SavedQueryDefinitionDraft
        {
            LayerId = 7,
            ServiceName = "planning",
            OutFields = "name",
            PreviewLimit = 25,
            Combinator = FilterPlanCombinator.And,
            Predicates =
            [
                new SavedQueryPredicateDraft { Kind = SavedQueryPredicateKind.Attribute, Property = "status", Operator = "eq", Value = "open" },
                new SavedQueryPredicateDraft { Kind = SavedQueryPredicateKind.Spatial, Operator = "dwithin", GeometryGeoJson = "{\"type\":\"Point\",\"coordinates\":[0,0]}", Distance = 500, DistanceUnit = "meters" }
            ]
        };

        var readout = SavedQueryContentMapper.RenderFilterReadout(draft);

        Assert.Contains("SELECT name FROM planning/layer:7", readout, StringComparison.Ordinal);
        Assert.Contains("status = open", readout, StringComparison.Ordinal);
        Assert.Contains("ST_DWithin(geometry, <geojson>, 500 meters)", readout, StringComparison.Ordinal);
        Assert.Contains("LIMIT 25", readout, StringComparison.Ordinal);
    }

    [Fact]
    public void FilterReadoutNotesEmptyPredicateSet()
    {
        var readout = SavedQueryContentMapper.RenderFilterReadout(new SavedQueryDefinitionDraft { LayerId = 3 });

        Assert.Contains("SELECT * FROM layer:3", readout, StringComparison.Ordinal);
        Assert.Contains("no predicates", readout, StringComparison.Ordinal);
    }

    private static HonuaServerSavedQueryEditorService CreateService(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://server.example") };
        var client = new AnalysisContentHttpClient(
            httpClient,
            new AnalysisContentClientOptions(new Uri("https://server.example"), "test-api-key"));
        return new HonuaServerSavedQueryEditorService(client);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request, cancellationToken));
    }
}
