using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Sdk.Admin.Models;

namespace Honua.Console.Shell.Tests;

public sealed class FolderPublishingTests
{
    [Theory]
    [InlineData("omit")]
    [InlineData("root")]
    [InlineData("folder")]
    public async Task PublishForwardsTypedFolderPresenceAndPublicName(string choice)
    {
        using var handler = new PublishHandler();
        using var http = new HttpClient(handler);
        using var client = new HonuaAdminOperateHttpClient(http, new(new Uri("https://fixture.invalid/prefix/")));
        var operation = new HonuaServerServiceLayerPublishOperation(client, new Authority());
        var result = await operation.PublishAsync(new()
        {
            ConnectionId = "connection",
            Schema = "public",
            Table = "parcels",
            LayerName = "Parcels",
            ServiceName = "logical service",
            PublicName = "Public-service",
            FolderId = choice == "omit" ? default : new(choice == "root" ? null : "folder-id"),
            AuthorityBinding = choice == "omit" ? null : "human-environment"
        });
        Assert.True(result.Succeeded, result.Detail);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("logical service", body.RootElement.GetProperty("serviceName").GetString());
        Assert.Equal("Public-service", body.RootElement.GetProperty("publicName").GetString());
        Assert.False(body.RootElement.TryGetProperty("namespace", out _));
        if (choice == "omit") Assert.False(body.RootElement.TryGetProperty("folderId", out _));
        else Assert.Equal(choice == "root" ? null : "folder-id", body.RootElement.GetProperty("folderId").GetString());
        Assert.Equal(choice == "omit" ? null : "human-environment", handler.Binding);
        Assert.Equal("/prefix/api/v1/admin/connections/connection/layers/", handler.Path);
    }

    [Fact]
    public async Task ExplicitFolderWithoutReviewedAuthorityCannotPublish()
    {
        using var handler = new PublishHandler();
        using var http = new HttpClient(handler);
        using var client = new HonuaAdminOperateHttpClient(http, new(new Uri("https://fixture.invalid/")));
        var result = await new HonuaServerServiceLayerPublishOperation(client).PublishAsync(new()
        {
            ConnectionId = "connection",
            Schema = "public",
            Table = "parcels",
            LayerName = "Parcels",
            ServiceName = "logical",
            FolderId = new("folder-id")
        });
        Assert.False(result.Succeeded);
        Assert.Null(handler.Body);
    }

    [Theory]
    [InlineData("FeatureServer", "/rest/services/Public%20Folder/Public-service/FeatureServer")]
    [InlineData("Wms", "/ogc/services/Public%20Folder/Public-service/wms")]
    [InlineData("Wmts", "/ogc/services/Public%20Folder/Public-service/wmts")]
    [InlineData("OgcFeatures", "/ogc/features")]
    public void ServingRoutesUsePublicNamesAndKeepTheFolderSeparate(string protocol, string expected)
    {
        Assert.Equal(expected, HonuaServedRoutes.PathFor("Public-service", protocol, "Public Folder"));
        Assert.Equal("/rest/services/legacy%2Flogical/FeatureServer", HonuaServedRoutes.PathFor("legacy/logical", "FeatureServer"));
    }

    [Fact]
    public async Task HostOnlyPostSendMarkerPreventsAFalsePublishFailure()
    {
        using var handler = new PublishHandler { Unverified = true };
        using var http = new HttpClient(handler);
        using var client = new HonuaAdminOperateHttpClient(http, new(new Uri("https://fixture.invalid/")));
        var result = await new HonuaServerServiceLayerPublishOperation(client, new Authority()).PublishAsync(new()
        {
            ConnectionId = "connection",
            Schema = "public",
            Table = "parcels",
            LayerName = "Parcels",
            ServiceName = "logical",
            AuthorityBinding = "human-environment"
        });
        Assert.False(result.Succeeded);
        Assert.Equal(ConsoleWriteOutcome.UnverifiedState, result.State);
        Assert.Contains("original environment", result.Detail);
    }

    private sealed class Authority : IConsoleAuthorityBindingValidator
    {
        public Task<bool> IsCurrentAsync(string binding, CancellationToken cancellationToken = default) => Task.FromResult(binding == "human-environment");
    }

    private sealed class PublishHandler : HttpMessageHandler
    {
        public bool Unverified { get; init; }
        public string? Body { get; private set; }
        public string? Binding { get; private set; }
        public string? Path { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Unverified) request.Options.Set(ConsoleWriteOutcome.Unverified, true);
            Path = request.RequestUri!.AbsolutePath;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Binding = request.Options.TryGetValue(ConsoleAuthorityRequestOptions.Expected, out var binding) ? binding : null;
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data = new { layerId = 7, layerName = "Parcels", serviceName = "logical service", enabled = true } }) };
        }
    }
}
