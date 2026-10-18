using System.Text.Json;
using Honua.Console.Web;

namespace Honua.Console.IntegrationTests;

public sealed class StudioReadBffTests
{
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
    [InlineData("json",true)]
    [InlineData("geojson",true)]
    [InlineData("html",false)]
    [InlineData("pjson",false)]
    public void FeatureRenderingAcceptsOnlySupportedJsonFormats(string format,bool accepted) =>
        Assert.Equal(accepted,StudioReadBff.IsFeatureFormat(format));
}
