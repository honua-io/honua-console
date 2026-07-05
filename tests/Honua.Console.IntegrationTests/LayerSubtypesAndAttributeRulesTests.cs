using System.Net;
using System.Net.Http.Json;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the layer subtypes + attribute-rules authoring surface (gapc/subtypes-rules): a new
/// <c>GET/PUT /api/v1/admin/metadata/layers/{id}/subtypes</c> and <c>.../attribute-rules</c> client + operation
/// + page. These assert the real route/verb/body the Save mutations issue (over a recording admin client), the
/// result mapping, and — critically — that a missing-binding result is surfaced HONESTLY through real DI rather
/// than fabricated (Console Patterns Charter section 11). The page tests prove the GET load renders bound rows
/// and the merged-build Unsupported* source renders the honest missing-binding state. They run in PR CI (no
/// Docker).
/// </summary>
public sealed class LayerSubtypesAndAttributeRulesTests
{
    private const string ResourceId = "conn-1-layer-1";
    private static readonly Uri BaseAddress = new("https://honua.test");

    // ---- Client: subtypes route + body ----

    [Fact]
    public async Task UpdateSubtypes_IssuesPutToSubtypesRoute_WithSubtypeBody()
    {
        string? path = null;
        HttpMethod? method = null;
        string? body = null;
        var operation = new HonuaServerConsoleLayerSubtypesOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminLayerSubtypes { LayerId = 1 });
        }));

        var result = await operation.SetSubtypesAsync(
            layerId: 1,
            subtypeField: "status",
            defaultSubtypeCode: "1",
            clear: false,
            subtypes:
            [
                new ConsoleLayerSubtype
                {
                    Code = "1",
                    Name = "Active",
                    FieldOverrides = [new ConsoleSubtypeFieldOverride { FieldName = "phase", DefaultValueJson = "\"start\"" }],
                }
            ]);

        Assert.True(result.Succeeded);
        Assert.Equal("Updated", result.State);
        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("/api/v1/admin/metadata/layers/1/subtypes", path);
        Assert.Contains("\"clear\":false", body!, StringComparison.Ordinal);
        Assert.Contains("\"subtypeField\":\"status\"", body!, StringComparison.Ordinal);
        Assert.Contains("\"defaultSubtypeCode\":1", body!, StringComparison.Ordinal);
        Assert.Contains("\"code\":1", body!, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"Active\"", body!, StringComparison.Ordinal);
        // The field override is keyed by field name with its JSON-typed default passthrough.
        Assert.Contains("\"phase\":", body!, StringComparison.Ordinal);
        Assert.Contains("\"defaultValue\":\"start\"", body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClearSubtypes_IssuesPutWithClearTrue_AndNoSubtypes()
    {
        string? body = null;
        var operation = new HonuaServerConsoleLayerSubtypesOperation(CreateClient(request =>
        {
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminLayerSubtypes { LayerId = 1 });
        }));

        var result = await operation.SetSubtypesAsync(1, null, null, clear: true, subtypes: []);

        Assert.True(result.Succeeded);
        Assert.Contains("\"clear\":true", body!, StringComparison.Ordinal);
        // clear:true keeps the body minimal — subtypes is omitted entirely (WhenWritingNull).
        Assert.DoesNotContain("\"subtypes\":", body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSubtypes_ProjectsBoundSubtypesForEditor()
    {
        var client = CreateClient(_ => Ok(new HonuaAdminLayerSubtypes
        {
            LayerId = 1,
            SubtypeField = "status",
            DefaultSubtypeCode = ParseJson("1"),
            Subtypes =
            [
                new HonuaAdminLayerSubtype
                {
                    Code = ParseJson("1"),
                    Name = "Active",
                    FieldOverrides = new Dictionary<string, HonuaAdminSubtypeFieldOverride>
                    {
                        ["phase"] = new() { DefaultValue = ParseJson("\"start\"") },
                    },
                }
            ],
        }));
        var operation = new HonuaServerConsoleLayerSubtypesOperation(client);

        var view = await operation.GetSubtypesAsync(1);

        Assert.True(view.Bound);
        Assert.Equal("status", view.SubtypeField);
        Assert.Equal("1", view.DefaultSubtypeCode);
        var subtype = Assert.Single(view.Subtypes);
        Assert.Equal("1", subtype.Code);
        Assert.Equal("Active", subtype.Name);
        var ovr = Assert.Single(subtype.FieldOverrides);
        Assert.Equal("phase", ovr.FieldName);
        Assert.Equal("start", ovr.DefaultValueJson);
    }

    // ---- Client: attribute-rules route + body ----

    [Fact]
    public async Task UpdateAttributeRules_IssuesPutToAttributeRulesRoute_WithRuleBody()
    {
        string? path = null;
        HttpMethod? method = null;
        string? body = null;
        var operation = new HonuaServerConsoleLayerSubtypesOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminLayerAttributeRules { LayerId = 1 });
        }));

        var result = await operation.SetAttributeRulesAsync(1,
        [
            new ConsoleAttributeRule
            {
                Name = "calc_area",
                Type = "calculation",
                FieldName = "area",
                ScriptExpression = "$feature.area * 2",
                TriggeringEvents = ["insert", "update"],
                ErrorMessage = "Area must be positive",
                IsEnabled = true,
            }
        ]);

        Assert.True(result.Succeeded);
        Assert.Equal("Updated", result.State);
        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("/api/v1/admin/metadata/layers/1/attribute-rules", path);
        Assert.Contains("\"name\":\"calc_area\"", body!, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"calculation\"", body!, StringComparison.Ordinal);
        Assert.Contains("\"fieldName\":\"area\"", body!, StringComparison.Ordinal);
        Assert.Contains("\"scriptExpression\":\"$feature.area * 2\"", body!, StringComparison.Ordinal);
        Assert.Contains("\"triggeringEvents\":[\"insert\",\"update\"]", body!, StringComparison.Ordinal);
        Assert.Contains("\"errorMessage\":\"Area must be positive\"", body!, StringComparison.Ordinal);
        Assert.Contains("\"isEnabled\":true", body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateAttributeRules_Empty_IssuesPutWithEmptyRules_ToClear()
    {
        string? body = null;
        var operation = new HonuaServerConsoleLayerSubtypesOperation(CreateClient(request =>
        {
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminLayerAttributeRules { LayerId = 1 });
        }));

        var result = await operation.SetAttributeRulesAsync(1, []);

        Assert.True(result.Succeeded);
        Assert.Contains("\"rules\":[]", body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateAttributeRules_WhenServerRejectsDuplicateName_DoesNotClaimSuccess()
    {
        var operation = new HonuaServerConsoleLayerSubtypesOperation(CreateClient(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { success = false, message = "Duplicate rule name 'calc_area'." })
            }));

        var result = await operation.SetAttributeRulesAsync(1,
            [new ConsoleAttributeRule { Name = "calc_area", Type = "calculation" }]);

        Assert.False(result.Succeeded);
        Assert.NotEqual("Updated", result.State);
        Assert.Contains("Duplicate rule name", result.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateSubtypes_OnTransportError_DoesNotClaimSuccess()
    {
        var operation = new HonuaServerConsoleLayerSubtypesOperation(CreateClient(_ =>
            throw new HttpRequestException("connection refused")));

        var result = await operation.SetSubtypesAsync(1, "status", "1", clear: false, subtypes: []);

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
    }

    // ---- Unsupported (missing-binding) ----

    [Fact]
    public async Task Unsupported_SubtypesSave_NeverCallsNetwork_AndReturnsMissingBinding()
    {
        var operation = new UnsupportedConsoleLayerSubtypesOperation();

        var subtypes = await operation.SetSubtypesAsync(1, "status", "1", clear: false, subtypes: []);
        var rules = await operation.SetAttributeRulesAsync(1, []);

        Assert.False(subtypes.Succeeded);
        Assert.Equal("Missing binding", subtypes.State);
        Assert.Contains("HONUA_SERVER_BASE_URL", subtypes.Detail!, StringComparison.Ordinal);
        Assert.False(rules.Succeeded);
        Assert.Equal("Missing binding", rules.State);
        Assert.Contains("HONUA_SERVER_BASE_URL", rules.Detail!, StringComparison.Ordinal);
    }

    // ---- Page render ----

    [Fact]
    public void Page_WhenBound_RendersExistingSubtypeAndRuleRowsFromGet()
    {
        var fake = new FakeSubtypes
        {
            SubtypesRead = new ConsoleLayerSubtypes
            {
                Bound = true,
                LayerId = 1,
                SubtypeField = "status",
                DefaultSubtypeCode = "1",
                Subtypes = [new ConsoleLayerSubtype { Code = "1", Name = "Active", FieldOverrides = [] }],
            },
            RulesRead = new ConsoleLayerAttributeRules
            {
                Bound = true,
                LayerId = 1,
                Rules = [new ConsoleAttributeRule { Name = "calc_area", Type = "calculation", FieldName = "area", IsEnabled = true }],
            },
        };
        var page = RenderPage(fake);

        page.WaitForAssertion(
            () => Assert.Contains("data-subtype-row", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("value=\"Active\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-rule-row", page.Markup, StringComparison.Ordinal);
        Assert.Contains("value=\"calc_area\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-subtype-save", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-rule-save", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_AddSubtypeThenSave_IssuesSaveWithRows()
    {
        var fake = new FakeSubtypes
        {
            SubtypesRead = new ConsoleLayerSubtypes { Bound = true, LayerId = 1, Subtypes = [] },
            RulesRead = new ConsoleLayerAttributeRules { Bound = true, LayerId = 1, Rules = [] },
            SubtypesSaveResult = new ConsoleSetSubtypesResult { Succeeded = true, State = "Updated", Detail = "Saved 1 subtype(s) on this layer." },
        };
        var page = RenderPage(fake);

        page.WaitForAssertion(
            () => Assert.Contains("data-subtypes-empty", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        page.Find("[data-subtype-add]").Click();
        page.Find("[data-subtype-save]").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-subtypes-result", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.NotNull(fake.LastSavedSubtypes);
        Assert.Single(fake.LastSavedSubtypes!);
        Assert.Contains("Saved 1 subtype", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_MergedBuild_RendersMissingBindingThroughRealDi()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FakeTransition());
        ctx.Services.AddSingleton<IConsoleLayerSubtypesOperation, UnsupportedConsoleLayerSubtypesOperation>();

        var page = ctx.Render<OperateLayerSubtypesPage>(p => p.Add(x => x.ResourceId, ResourceId));

        page.WaitForAssertion(
            () => Assert.Contains("data-subtypes-unbound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("data-rules-unbound", page.Markup, StringComparison.Ordinal);
        Assert.Contains("HONUA_SERVER_BASE_URL", page.Markup, StringComparison.Ordinal);
    }

    // ---- Helpers ----

    private static IRenderedComponent<OperateLayerSubtypesPage> RenderPage(FakeSubtypes fake)
    {
        var ctx = new Bunit.BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FakeTransition());
        ctx.Services.AddSingleton<IConsoleLayerSubtypesOperation>(fake);
        return ctx.Render<OperateLayerSubtypesPage>(p => p.Add(x => x.ResourceId, ResourceId));
    }

    private static System.Text.Json.JsonElement ParseJson(string text)
    {
        using var document = System.Text.Json.JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static HttpResponseMessage Ok<T>(T data) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { success = true, data, timestamp = DateTimeOffset.UtcNow })
        };

    private static HonuaAdminOperateHttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var httpClient = new HttpClient(new StubHandler(responder)) { BaseAddress = BaseAddress };
        return new HonuaAdminOperateHttpClient(httpClient, new HonuaAdminOperateClientOptions(BaseAddress, "test-key"));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(responder(request));
        }
    }

    private sealed class FakeTransition : IOperateTransitionDataSource
    {
        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperateTransitionWorkspace(
                [],
                [],
                [
                    new OperateServiceDetail(
                        "svc", "Service", "FeatureServer", "running", "server",
                        [new OperateServiceLayerProjection(1, "Parcels", "polygon", ResourceId, "parcels")],
                        [],
                        [])
                ],
                [],
                []));

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateConnectionSummary?>(null);

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateResourceEditPreview?>(null);

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateServiceDetail?>(null);
    }

    private sealed class FakeSubtypes : IConsoleLayerSubtypesOperation
    {
        public ConsoleLayerSubtypes SubtypesRead { get; set; } = ConsoleLayerSubtypes.Unbound("test");
        public ConsoleLayerAttributeRules RulesRead { get; set; } = ConsoleLayerAttributeRules.Unbound("test");
        public ConsoleSetSubtypesResult? SubtypesSaveResult { get; set; }
        public ConsoleSetAttributeRulesResult? RulesSaveResult { get; set; }
        public IReadOnlyList<ConsoleLayerSubtype>? LastSavedSubtypes { get; private set; }
        public IReadOnlyList<ConsoleAttributeRule>? LastSavedRules { get; private set; }

        public Task<ConsoleLayerSubtypes> GetSubtypesAsync(int layerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SubtypesRead);

        public Task<ConsoleSetSubtypesResult> SetSubtypesAsync(
            int layerId, string? subtypeField, string? defaultSubtypeCode, bool clear,
            IReadOnlyList<ConsoleLayerSubtype> subtypes, CancellationToken cancellationToken = default)
        {
            LastSavedSubtypes = subtypes;
            return Task.FromResult(SubtypesSaveResult ?? new ConsoleSetSubtypesResult { Succeeded = true, State = "Updated" });
        }

        public Task<ConsoleLayerAttributeRules> GetAttributeRulesAsync(int layerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(RulesRead);

        public Task<ConsoleSetAttributeRulesResult> SetAttributeRulesAsync(
            int layerId, IReadOnlyList<ConsoleAttributeRule> rules, CancellationToken cancellationToken = default)
        {
            LastSavedRules = rules;
            return Task.FromResult(RulesSaveResult ?? new ConsoleSetAttributeRulesResult { Succeeded = true, State = "Updated" });
        }
    }
}
