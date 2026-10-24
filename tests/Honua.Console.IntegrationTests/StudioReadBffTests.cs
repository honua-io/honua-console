using System.Text.Json;
using Honua.Console.Web;

namespace Honua.Console.IntegrationTests;

public sealed class StudioReadBffTests
{
    [Theory]
    [InlineData("/api/studio/data/rest/services/a%2fb/FeatureServer/1", false)]
    [InlineData("/api/studio/data/rest/services/a%5cb/FeatureServer/1", false)]
    [InlineData("/api/studio/data/rest/services/%252e/FeatureServer/1", false)]
    [InlineData("/api/studio/data/rest/services/a/FeatureServer/1/query?where=name%3D%27a%2fb%27", true)]
    public void FeatureReadRejectsEncodedPathSeparatorsWithoutRejectingQueryValues(string rawTarget, bool expected)
        => Assert.Equal(expected, StudioReadBff.IsCanonicalReadPath(rawTarget));

    [Theory]
    [InlineData("folder/service/FeatureServer/0", "?f=json", true)]
    [InlineData("folder/service/FeatureServer/0/query", "?f=json&resultOffset=2000&resultRecordCount=2000&returnGeometry=true", true)]
    [InlineData("folder/service/FeatureServer/0", "?f=geojson", false)]
    [InlineData("folder/service/FeatureServer/0", "?where=1%3D1", false)]
    [InlineData("folder/service/FeatureServer/0/query", "?resultOffset=-1", false)]
    [InlineData("folder/service/FeatureServer/0/query", "?resultRecordCount=2001", false)]
    [InlineData("folder/service/FeatureServer/0/query", "?returnGeometry=other", false)]
    [InlineData("folder/%2fservice/FeatureServer/0", "?f=json", false)]
    [InlineData("folder/../service/FeatureServer/0", "?f=json", false)]
    [InlineData("folder/service/FeatureServer/0/updateFeatures", "?f=json", false)]
    public void MetadataAndPagingAreBoundedReadOnlyPaths(string path, string query, bool accepted)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString(query);
        Assert.Equal(accepted, StudioReadBff.FeatureReadPath(path, context.Request.Query) is not null);
    }

    [Theory]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"honua_list_layers\",\"arguments\":{\"offset\":0,\"limit\":100}}}")]
    [InlineData("{\"method\":\"tools/call\",\"params\":{\"name\":\"honua_get_style\",\"arguments\":{\"styleId\":\"style\",\"includeStylesheet\":true,\"encoding\":\"mapbox-style\"}}}")]
    [InlineData("{\"method\":\"tools/call\",\"params\":{\"name\":\"honua_describe_layer\",\"arguments\":{\"serviceId\":\"folder/service\",\"layerId\":0}}}")]
    public void CatalogTransport_OnlyAcceptsBoundedReadHandshake(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.True(StudioReadBff.IsCatalogRead(document.RootElement));
    }

    [Theory]
    [InlineData("{\"method\":\"tools/list\"}")]
    [InlineData("{\"method\":\"tools/call\",\"params\":{\"name\":\"honua_publish_service\"}}")]
    [InlineData("{\"method\":\"tools/call\",\"params\":{\"name\":\"honua_query_features\"}}")]
    [InlineData("{\"method\":\"tools/call\",\"params\":{\"name\":\"honua_list_layers\",\"arguments\":{\"limit\":1001}}}")]
    [InlineData("{\"method\":\"tools/call\",\"params\":{\"name\":\"honua_list_layers\",\"arguments\":{\"limit\":\"100\"}}}")]
    [InlineData("{\"method\":\"tools/call\",\"params\":{\"name\":\"honua_list_layers\",\"arguments\":{\"secret\":\"value\"}}}")]
    [InlineData("{\"method\":\"tools/list\",\"method\":\"initialize\"}")]
    [InlineData("{\"method\":42}")]
    [InlineData("[{\"method\":\"initialize\"}]")]
    public void CatalogTransport_RejectsMutationBypassAndMalformedArguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.False(StudioReadBff.IsCatalogRead(document.RootElement));
    }

    [Fact]
    public void NegotiatedMcpSessions_AreIsolatedByOperatorProfileAndWorkspace()
    {
        var sessions = new AgentMcpSessions();
        sessions.Put(("operator-a", "profile-a", "workspace-a"), "session");
        Assert.Equal("session", sessions.Get(("operator-a", "profile-a", "workspace-a")));
        Assert.Null(sessions.Get(("operator-b", "profile-a", "workspace-a")));
        Assert.Null(sessions.Get(("operator-a", "profile-b", "workspace-a")));
        Assert.Null(sessions.Get(("operator-a", "profile-a", "workspace-b")));
    }
    [Theory]
    [InlineData("collection", "?f=json&limit=2000", true)]
    [InlineData("collection", "", true)]
    [InlineData("collection", "?limit=2001", false)]
    [InlineData("collection", "?limit=1&limit=2", false)]
    [InlineData("collection", "?url=https://example.com", false)]
    [InlineData("../secret", "", false)]
    [InlineData("%2fsecret", "", false)]
    public void OgcRead_IsBoundedToOneCollection(string collectionId, string query, bool accepted)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString(query);
        Assert.Equal(accepted, StudioReadBff.OgcReadPath(collectionId, context.Request.Query) is not null);
    }
    [Theory]
    [InlineData("json", true)]
    [InlineData("geojson", true)]
    [InlineData("html", false)]
    [InlineData("pjson", false)]
    public void FeatureRenderingAcceptsOnlySupportedJsonFormats(string format, bool accepted) =>
        Assert.Equal(accepted, StudioReadBff.IsFeatureFormat(format));

    [Fact]
    public void StyleSuggestionAcceptsOnlyTheDraftBinding()
    {
        Assert.True(StudioReadBff.TryReadStyleSuggestionRequest(
            """{"draftRevision":"rev-1","composedLayerId":"parcels"}""", out var revision, out var layer));
        Assert.Equal("rev-1", revision);
        Assert.Equal("parcels", layer);
        var bounded = new string('b', 262144);
        Assert.True(StudioReadBff.TryReadStyleSuggestionRequest(
            JsonSerializer.Serialize(new { draftRevision = bounded, composedLayerId = "parcels" }), out revision, out _));
        Assert.Equal(262144, revision.Length);
    }

    [Theory]
    [InlineData("""{"draftRevision":"rev","composedLayerId":"parcels","principalId":"from-body"}""")]
    [InlineData("""{"draftRevision":"rev","composedLayerId":"parcels","environmentId":"from-body"}""")]
    [InlineData("""{"draftRevision":"rev","composedLayerId":"parcels","authorization":"Bearer secret"}""")]
    [InlineData("""{"draftRevision":"rev","composedLayerId":"parcels","api_key":"secret"}""")]
    [InlineData("""{"draftRevision":"rev","composedLayerId":"a/b"}""")]
    [InlineData("""{"draftRevision":"rev","composedLayerId":"a\\b"}""")]
    [InlineData("""{"draftRevision":"rev","composedLayerId":"../admin"}""")]
    [InlineData("""{"composedLayerId":"parcels"}""")]
    [InlineData("""{"draftRevision":"rev"}""")]
    [InlineData("""{"draftRevision":"","composedLayerId":"parcels"}""")]
    [InlineData("""{"draftRevision":"a\u0000b","composedLayerId":"parcels"}""")]
    public void StyleSuggestionRejectsExtraPropertiesCredentialsAndPathInjection(string json)
        => Assert.False(StudioReadBff.TryReadStyleSuggestionRequest(json, out _, out _));

    [Fact]
    public void StyleSuggestionRejectsARevisionAboveTheFieldLimit()
    {
        var huge = JsonSerializer.Serialize(new { draftRevision = new string('a', 262145), composedLayerId = "parcels" });
        Assert.False(StudioReadBff.TryReadStyleSuggestionRequest(huge, out _, out _));
    }

    [Fact]
    public void StyleSuggestionIntakeStopsAtEightKibibytes()
    {
        Assert.Equal(400, StudioReadBff.StyleSuggestionIntake(new byte[StudioReadBff.StyleSuggestionMaxBodyBytes], out _, out _));
        Assert.Equal(413, StudioReadBff.StyleSuggestionIntake(new byte[StudioReadBff.StyleSuggestionMaxBodyBytes + 1], out _, out _));
    }

    [Theory]
    [InlineData(0, "api/v1/admin/metadata/layers/0/suggest-style")]
    [InlineData(12, "api/v1/admin/metadata/layers/12/suggest-style")]
    [InlineData(999_999_999, "api/v1/admin/metadata/layers/999999999/suggest-style")]
    public void StyleSuggestionUpstreamPathIsTheAdminSuggestStyleRoute(int layerId, string path)
        => Assert.Equal(path, StudioReadBff.StyleSuggestionUpstreamPath(layerId));

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_000_000)]
    public void StyleSuggestionUpstreamPathRejectsNonCanonicalLayerIds(int layerId)
        => Assert.Null(StudioReadBff.StyleSuggestionUpstreamPath(layerId));

    [Fact]
    public void StyleSuggestionEnvelopeUsesTheCallerPartitionRatherThanTheServerBody()
    {
        var json = StudioReadBff.StyleSuggestionEnvelope(
            """{"success":true,"data":{"edition":"Community"},"principalId":"from-body","environmentId":"from-body"}""",
            "partition-a",
            "env-a",
            12,
            "rev-1",
            "parcels");
        Assert.NotNull(json);
        using var document = JsonDocument.Parse(json);
        var binding = document.RootElement.GetProperty("binding");
        Assert.Equal("partition-a", binding.GetProperty("principalId").GetString());
        Assert.Equal("env-a", binding.GetProperty("environmentId").GetString());
        Assert.Equal(12, binding.GetProperty("canonicalLayerId").GetInt32());
        Assert.Equal("rev-1", binding.GetProperty("draftRevision").GetString());
        Assert.Equal("parcels", binding.GetProperty("composedLayerId").GetString());
        Assert.Equal("from-body", document.RootElement.GetProperty("suggestion").GetProperty("principalId").GetString());
        Assert.Equal("Community", document.RootElement.GetProperty("suggestion").GetProperty("data").GetProperty("edition").GetString());
        Assert.Null(StudioReadBff.StyleSuggestionEnvelope("not-json", "partition-a", "env-a", 12, "rev-1", "parcels"));
    }
}
