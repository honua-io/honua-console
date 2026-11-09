using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class DiscoveryMetadataEditorTests
{
    private static readonly DiscoveryMetadataResponse Initial = new()
    {
        Etag = "revision-1",
        EntityId = "canonical-roads",
        Title = "Roads",
        Description = "Street survey",
        Keywords = ["roads", "survey"],
        AffectedPublications = [new("pub-a", "transport", "7"), new("pub-b", "public-data", "streets")]
    };

    [Fact]
    public void AgentSummaryTracksEditsReloadAndSaveWithoutMetadataValues()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(new MetadataClient());
        var summaries = new List<ConsoleAgentUnsavedSummary>();
        var component = context.Render<DiscoveryMetadataEditor>(p => p
            .Add(c => c.Address, DiscoveryMetadataAddress.ForLayer(42))
            .Add(c => c.UnsavedChanged, summary => summaries.Add(summary)));
        Assert.Equal(0, summaries[^1].ChangedFieldCount);
        Assert.Contains(summaries, summary => summary.PendingOperationCount == 1);
        component.Find("[data-discovery-title]").Change("Private draft title");
        component.Find("[data-discovery-description]").Change("Private draft description");
        Assert.Equal(2, summaries[^1].ChangedFieldCount);
        Assert.DoesNotContain("Private draft", JsonSerializer.Serialize(summaries));
        component.Find("[data-discovery-reload]").Click();
        Assert.Equal(0, summaries[^1].ChangedFieldCount);
        component.Find("[data-discovery-title]").Change("Another title");
        component.Find("[data-discovery-save]").Click();
        Assert.Equal(0, summaries[^1].ChangedFieldCount);
        Assert.Equal(0, summaries[^1].PendingOperationCount);
    }

    [Theory]
    [InlineData(DiscoveryMetadataTargetKind.Resource)]
    [InlineData(DiscoveryMetadataTargetKind.Service)]
    public void ReadDiscovery_LoadsCanonicalValuesAndImpactBeforeSave(DiscoveryMetadataTargetKind kind)
    {
        using var context = new BunitContext();
        var client = new MetadataClient();
        context.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(client);
        var component = context.Render<DiscoveryMetadataEditor>(parameters => parameters.Add(editor => editor.Address, Address(kind)));
        component.WaitForAssertion(() => Assert.Equal("Roads", component.Find("[data-discovery-title]").GetAttribute("value")));
        Assert.Equal("Street survey", component.Find("[data-discovery-description]").GetAttribute("value"));
        Assert.Equal("roads\nsurvey", component.Find("[data-discovery-keywords]").GetAttribute("value"));
        Assert.Contains("canonical-roads", component.Find("[data-discovery-resource]").TextContent, StringComparison.Ordinal);
        Assert.Contains("transport / 7", component.Find("[data-discovery-impact]").TextContent, StringComparison.Ordinal);
        Assert.Contains("public-data / streets", component.Find("[data-discovery-impact]").TextContent, StringComparison.Ordinal);
        Assert.Equal(kind, client.LastAddress!.Kind);
        Assert.Equal(0, client.Writes);
    }

    [Fact]
    public void SaveDiscovery_UsesReadEtagAndShowsStoredResponse()
    {
        using var context = new BunitContext();
        var client = new MetadataClient
        {
            WriteResult = HonuaAdminEndpointResult<DiscoveryMetadataResponse>.FromData(
            Initial with { Etag = "revision-2", Title = "Stored survey" })
        };
        context.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(client);
        var component = context.Render<DiscoveryMetadataEditor>(parameters => parameters.Add(editor => editor.Address, DiscoveryMetadataAddress.ForLayer(42)));
        component.Find("[data-discovery-title]").Change("New survey");
        component.Find("[data-discovery-description]").Change("Surveyed streets");
        component.Find("[data-discovery-keywords]").Change("roads\n transport ");
        component.Find("[data-discovery-save]").Click();
        component.WaitForAssertion(() => Assert.NotNull(client.LastWrite));
        Assert.Equal("revision-1", client.LastWrite!.Etag);
        Assert.Equal("New survey", client.LastWrite.Title);
        Assert.Equal("Surveyed streets", client.LastWrite.Description);
        Assert.Equal(["roads", "transport"], client.LastWrite.Keywords);
        Assert.Equal("Stored survey", component.Find("[data-discovery-title]").GetAttribute("value"));
        Assert.NotNull(component.Find("[data-discovery-saved]"));
    }

    [Fact]
    public void SaveDescription_UnchangedKeywords_PreservesImportedEntries()
    {
        using var context = new BunitContext();
        var client = new MetadataClient
        {
            ReadResult = HonuaAdminEndpointResult<DiscoveryMetadataResponse>.FromData(
            Initial with { Title = null, Keywords = ["roads, streets", " survey ", "line\nbreak"] })
        };
        context.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(client);
        var component = context.Render<DiscoveryMetadataEditor>(parameters => parameters.Add(editor => editor.Address, DiscoveryMetadataAddress.ForLayer(42)));
        component.Find("[data-discovery-description]").Change("Updated description");
        component.Find("[data-discovery-save]").Click();
        component.WaitForAssertion(() => Assert.NotNull(client.LastWrite));
        Assert.Null(client.LastWrite!.Title);
        Assert.Equal(["roads, streets", " survey ", "line\nbreak"], client.LastWrite.Keywords);
    }

    [Fact]
    public void SaveDiscovery_Conflict_PreservesEditsUntilExplicitReload()
    {
        using var context = new BunitContext();
        var client = new MetadataClient
        {
            WriteResult = HonuaAdminEndpointResult<DiscoveryMetadataResponse>.FromIssue(
            AdminEndpointIssueFactory.CreateIssue("PUT discovery", HttpStatusCode.Conflict))
        };
        context.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(client);
        var component = context.Render<DiscoveryMetadataEditor>(parameters => parameters.Add(editor => editor.Address, DiscoveryMetadataAddress.ForLayer(42)));
        component.Find("[data-discovery-title]").Change("Unsaved title");
        component.Find("[data-discovery-save]").Click();
        component.WaitForAssertion(() => Assert.Contains("Conflict", component.Markup, StringComparison.Ordinal));
        Assert.Equal("Unsaved title", component.Find("[data-discovery-title]").GetAttribute("value"));
        Assert.Empty(component.FindAll("[data-discovery-saved]"));
        client.ReadResult = HonuaAdminEndpointResult<DiscoveryMetadataResponse>.FromData(Initial with { Etag = "revision-3", Title = "Concurrent title" });
        component.Find("[data-discovery-reload]").Click();
        component.WaitForAssertion(() => Assert.Equal("Concurrent title", component.Find("[data-discovery-title]").GetAttribute("value")));
        component.Find("[data-discovery-save]").Click();
        Assert.Equal("revision-3", client.LastWrite!.Etag);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.NotImplemented)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void ReadDiscovery_UnavailableContract_OffersNoSave(HttpStatusCode status)
    {
        using var context = new BunitContext();
        var client = new MetadataClient
        {
            ReadResult = HonuaAdminEndpointResult<DiscoveryMetadataResponse>.FromIssue(
            AdminEndpointIssueFactory.CreateIssue("GET discovery", status))
        };
        context.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(client);
        var component = context.Render<DiscoveryMetadataEditor>(parameters => parameters.Add(editor => editor.Address, DiscoveryMetadataAddress.ForLayer(42)));
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-discovery-save]")));
        Assert.Contains(client.ReadResult.Issue!.State, component.Markup, StringComparison.Ordinal);
        Assert.Equal(0, client.Writes);
    }

    [Fact]
    public void ReadDiscovery_MissingBinding_OffersNoSave()
    {
        using var context = new BunitContext();
        var component = context.Render<DiscoveryMetadataEditor>(parameters => parameters.Add(editor => editor.Address, DiscoveryMetadataAddress.ForLayer(42)));
        Assert.Contains("Missing binding", component.Markup, StringComparison.Ordinal);
        Assert.Empty(component.FindAll("[data-discovery-save]"));
    }

    [Fact]
    public async Task ReadDiscovery_ChangedLayer_DiscardsLateResponse()
    {
        using var context = new BunitContext();
        var oldAddress = DiscoveryMetadataAddress.ForLayer(42);
        var newAddress = DiscoveryMetadataAddress.ForLayer(43);
        var pending = new TaskCompletionSource<HonuaAdminEndpointResult<DiscoveryMetadataResponse>>();
        var client = new MetadataClient
        {
            ReadHandler = address => address == oldAddress ? pending.Task
            : Task.FromResult(HonuaAdminEndpointResult<DiscoveryMetadataResponse>.FromData(Initial with { EntityId = "canonical-rivers" }))
        };
        context.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(client);
        var component = context.Render<DiscoveryMetadataEditor>(parameters => parameters.Add(editor => editor.Address, oldAddress));
        component.Render(parameters => parameters.Add(editor => editor.Address, newAddress));
        await component.InvokeAsync(() => pending.SetResult(HonuaAdminEndpointResult<DiscoveryMetadataResponse>.FromData(Initial)));
        component.WaitForAssertion(() => Assert.Contains("canonical-rivers", component.Find("[data-discovery-resource]").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void LayerPage_UsesGlobalAdminLayerIdAndShowsSharedDataset()
    {
        using var context = new BunitContext();
        context.AddConsoleNotifications();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var client = new MetadataClient();
        context.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(client);
        context.Services.AddSingleton<IConsoleLayerFieldsOperation>(new UnsupportedConsoleLayerFieldsOperation());
        context.Services.AddSingleton<IOperateTransitionDataSource>(FixedServicesDataSource.WithLayer(
            new OperateServiceLayerProjection(42, "Roads", "Line", "connection-layer-42", "Roads")));
        var page = context.Render<OperateLayerDetailPage>(parameters => parameters.Add(layer => layer.ResourceId, "connection-layer-42"));
        page.WaitForAssertion(() => Assert.Equal(DiscoveryMetadataAddress.ForLayer(42), client.LastAddress));
        Assert.Contains("shared dataset metadata", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Console layer reference", page.Markup, StringComparison.Ordinal);
        Assert.Contains("canonical-roads", page.Find("[data-discovery-resource]").TextContent, StringComparison.Ordinal);
        Assert.Contains("transport / 7", page.Find("[data-discovery-impact]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ServicePage_OffersDiscoveryAndWorkingControlsWithUnsupportedMapServer()
    {
        using var context = new BunitContext();
        context.AddConsoleNotifications();
        var client = new MetadataClient();
        context.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(client);
        var service = new OperateServiceDetail("svc", "Service", "FeatureServer", "running", "server", [],
            [new("mapServer.defaultFormat", "png", "MapServer runtime defaults", RequiresRestart: false)], []);
        context.Services.AddSingleton<IOperateTransitionDataSource>(new FixedServicesDataSource(service));
        using var http = new HttpClient(new MetadataHttpHandler(_ => Envelope(new HonuaAdminServiceSettingsResponse
        { ServiceName = "svc", EnabledProtocols = ["FeatureServer"], AvailableProtocols = ["FeatureServer"] })));
        using var admin = new HonuaAdminOperateHttpClient(http, new(new Uri("https://server.example")));
        context.Services.AddSingleton<IServiceConfigurationOperation>(new HonuaServerServiceConfigurationOperation(admin));
        var page = context.Render<OperateServiceDetailPage>(parameters => parameters.Add(service => service.ServiceName, "svc"));
        page.WaitForAssertion(() => Assert.Equal(DiscoveryMetadataAddress.ForService("svc"), client.LastAddress));
        Assert.NotNull(page.Find("[data-save-protocols]"));
        Assert.NotNull(page.Find("[data-save-access]"));
        Assert.Contains("Unsupported", page.Find("[data-mapserver-unsupported]").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-save-mapserver]"));
        Assert.Contains("Compatibility default", page.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DiscoveryMetadataTargetKind.Resource, "/api/v1/admin/metadata/layers/42/discovery")]
    [InlineData(DiscoveryMetadataTargetKind.Service, "/api/v1/admin/services/roads%20west/discovery")]
    public async Task DiscoveryClient_ReadAndWrite_UsesTypedRouteAndConcurrency(DiscoveryMetadataTargetKind kind, string path)
    {
        var handler = new MetadataHttpHandler(_ => Envelope(Initial));
        using var http = new HttpClient(handler);
        using var client = new HonuaAdminOperateHttpClient(http, new(new Uri("https://server.example"), "test-key"));
        var address = Address(kind);
        var read = await client.ReadDiscoveryAsync(address);
        var write = await client.UpdateDiscoveryAsync(address, new() { Etag = read.Data!.Etag, Title = "Updated", Keywords = ["roads"] });
        Assert.Equal(Initial.Title, write.Data!.Title);
        Assert.Equal([HttpMethod.Get, HttpMethod.Put], handler.Requests.Select(request => request.Method));
        Assert.All(handler.Requests, request => Assert.Equal(path, request.Path));
        using var body = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.Equal("revision-1", body.RootElement.GetProperty("etag").GetString());
        Assert.Equal("Updated", body.RootElement.GetProperty("title").GetString());
        Assert.Equal(["etag", "title", "description", "keywords"], body.RootElement.EnumerateObject().Select(property => property.Name));
    }

    private static DiscoveryMetadataAddress Address(DiscoveryMetadataTargetKind kind) => kind == DiscoveryMetadataTargetKind.Resource
        ? DiscoveryMetadataAddress.ForLayer(42) : DiscoveryMetadataAddress.ForService("roads west");

    private static HttpResponseMessage Envelope<T>(T value) => new(HttpStatusCode.OK)
    { Content = JsonContent.Create(new { success = true, data = value }) };

    private sealed class MetadataClient : IHonuaDiscoveryMetadataClient
    {
        public HonuaAdminEndpointResult<DiscoveryMetadataResponse> ReadResult { get; set; } = HonuaAdminEndpointResult<DiscoveryMetadataResponse>.FromData(Initial);
        public HonuaAdminEndpointResult<DiscoveryMetadataResponse> WriteResult { get; set; } = HonuaAdminEndpointResult<DiscoveryMetadataResponse>.FromData(Initial);
        public DiscoveryMetadataAddress? LastAddress { get; private set; }
        public DiscoveryMetadataUpdateRequest? LastWrite { get; private set; }
        public int Writes { get; private set; }
        public Func<DiscoveryMetadataAddress, Task<HonuaAdminEndpointResult<DiscoveryMetadataResponse>>>? ReadHandler { get; init; }

        public Task<HonuaAdminEndpointResult<DiscoveryMetadataResponse>> ReadDiscoveryAsync(DiscoveryMetadataAddress address, CancellationToken cancellationToken = default)
        {
            LastAddress = address;
            return ReadHandler?.Invoke(address) ?? Task.FromResult(ReadResult);
        }

        public Task<HonuaAdminEndpointResult<DiscoveryMetadataResponse>> UpdateDiscoveryAsync(DiscoveryMetadataAddress address,
            DiscoveryMetadataUpdateRequest request, CancellationToken cancellationToken = default)
        {
            LastAddress = address;
            LastWrite = request;
            Writes++;
            return Task.FromResult(WriteResult);
        }
    }

    private sealed class MetadataHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }
}
