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
/// Docker-free coverage for layer subtypes and attribute rules. The pinned server does not register
/// those routes, so the live client returns Unsupported and issues no HTTP. Page tests still prove a
/// bound fake renders rows, and the missing-binding operation names HONUA_SERVER_BASE_URL.
/// </summary>
public sealed class LayerSubtypesAndAttributeRulesTests
{
    private const string ResourceId = "conn-1-layer-1";
    private static readonly Uri BaseAddress = new("https://honua.test");

    // ---- Client: subtypes route + body ----

    [Fact]
    public async Task UnregisteredSubtypeAndAttributeRuleRoutes_FailClosedWithoutHttp()
    {
        var calls = 0;
        var operation = new HonuaServerConsoleLayerSubtypesOperation(CreateClient(_ =>
        {
            calls++;
            throw new InvalidOperationException("unregistered route must not send HTTP");
        }));

        var subtypes = await operation.GetSubtypesAsync(1);
        var rules = await operation.GetAttributeRulesAsync(1);
        var saveSubtypes = await operation.SetSubtypesAsync(1, "status", "1", clear: false, subtypes: []);
        var saveRules = await operation.SetAttributeRulesAsync(1, []);

        Assert.Equal(0, calls);
        Assert.False(subtypes.Bound);
        Assert.False(rules.Bound);
        Assert.Contains(UnregisteredServerRoutes.Detail, subtypes.Detail, StringComparison.Ordinal);
        Assert.False(saveSubtypes.Succeeded);
        Assert.Equal(UnregisteredServerRoutes.State, saveSubtypes.State);
        Assert.False(saveRules.Succeeded);
        Assert.Equal(UnregisteredServerRoutes.State, saveRules.State);
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
